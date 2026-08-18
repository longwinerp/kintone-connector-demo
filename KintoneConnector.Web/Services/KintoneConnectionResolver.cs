using System.Text.RegularExpressions;
using KintoneConnector.Web.Models;
using KintoneConnector.Web.Options;
using Microsoft.Extensions.Options;

namespace KintoneConnector.Web.Services;

public interface IKintoneConnectionResolver
{
    bool AdHocEnabled { get; }
    IReadOnlyList<string> AllowedHostSuffixes { get; }
    IReadOnlyList<KintoneProfileSummary> GetProfiles();

    /// <summary>把「profile 代號」或「呼叫端自填的連線」解析成已驗證的連線。</summary>
    KintoneConnection Resolve(KintoneConnectionRequest request);
}

public sealed partial class KintoneConnectionResolver(IOptionsMonitor<KintoneOptions> options)
    : IKintoneConnectionResolver
{
    private const string DefaultApiPath = "/k/v1/records.json";

    public bool AdHocEnabled => options.CurrentValue.AdHoc.Enabled;

    public IReadOnlyList<string> AllowedHostSuffixes =>
        options.CurrentValue.AdHoc.EffectiveHostSuffixes;

    public IReadOnlyList<KintoneProfileSummary> GetProfiles() =>
        options.CurrentValue.Profiles
            .Where(item => item.Value.Enabled)
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => new KintoneProfileSummary(
                item.Key,
                string.IsNullOrWhiteSpace(item.Value.DisplayName) ? item.Key : item.Value.DisplayName,
                item.Value.IsDemo ? "（內建示範資料，不會連出去）" : item.Value.BaseUrl,
                NormalizeApiPath(item.Value.ApiPath),
                item.Value.AppId,
                item.Value.IsDemo || !string.IsNullOrWhiteSpace(item.Value.ApiToken),
                item.Value.IsDemo))
            .ToArray();

    public KintoneConnection Resolve(KintoneConnectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var custom = request.Connection;
        var hasCustom = custom is not null && (
            !string.IsNullOrWhiteSpace(custom.BaseUrl) ||
            !string.IsNullOrWhiteSpace(custom.ApiToken) ||
            custom.AppId > 0);

        return hasCustom ? ResolveAdHoc(custom!) : ResolveProfile(request.Profile);
    }

    private KintoneConnection ResolveProfile(string profileCode)
    {
        var code = (profileCode ?? "").Trim();
        if (code.Length == 0)
            throw new KintoneRequestException(
                "CONNECTION_REQUIRED",
                "請指定 profile，或直接填入 Kintone URL、App ID 與 API Token。");

        var settings = options.CurrentValue;
        var entry = settings.Profiles.FirstOrDefault(item =>
            item.Key.Equals(code, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(entry.Key) || !entry.Value.Enabled)
            throw new KintoneRequestException("PROFILE_NOT_FOUND", "找不到指定的 Kintone profile。");

        var profile = entry.Value;

        // 示範 profile 不會真的連出去，所以跳過網址／Token 檢查。
        if (profile.IsDemo)
            return new KintoneConnection(
                entry.Key,
                string.IsNullOrWhiteSpace(profile.DisplayName) ? entry.Key : profile.DisplayName,
                "https://demo.local",
                DefaultApiPath,
                profile.AppId > 0 ? profile.AppId : 1,
                "",
                AdHoc: false,
                Demo: true);

        var baseUrl = ValidateBaseUrl(profile.BaseUrl, entry.Key, adHoc: false);
        var apiPath = ValidateApiPath(profile.ApiPath, entry.Key, adHoc: false);

        if (profile.AppId <= 0)
            throw new KintoneConfigurationException($"Profile「{entry.Key}」尚未設定有效 AppId。");
        if (string.IsNullOrWhiteSpace(profile.ApiToken))
            throw new KintoneConfigurationException($"Profile「{entry.Key}」尚未設定 ApiToken。");

        return new KintoneConnection(
            entry.Key,
            string.IsNullOrWhiteSpace(profile.DisplayName) ? entry.Key : profile.DisplayName,
            baseUrl,
            apiPath,
            profile.AppId,
            profile.ApiToken.Trim(),
            AdHoc: false);
    }

    private KintoneConnection ResolveAdHoc(KintoneConnectionInput input)
    {
        var adHoc = options.CurrentValue.AdHoc;
        if (!adHoc.Enabled)
            throw new KintoneRequestException(
                "ADHOC_DISABLED",
                "本閘道已關閉自訂連線，請改用伺服器端設定好的 profile。");

        var baseUrl = ValidateBaseUrl(input.BaseUrl, "自訂連線", adHoc: true);
        var apiPath = ValidateApiPath(input.ApiPath, "自訂連線", adHoc: true);
        var token = (input.ApiToken ?? "").Trim();

        if (input.AppId <= 0)
            throw new KintoneRequestException("APPID_REQUIRED", "請輸入有效的 App ID（正整數）。");
        if (token.Length == 0)
            throw new KintoneRequestException("TOKEN_REQUIRED", "請輸入 Kintone API Token。");
        if (token.Length > 512 || ContainsControlChar(token))
            throw new KintoneRequestException("TOKEN_INVALID", "API Token 格式不正確。");

        var host = new Uri(baseUrl).Host;
        if (!IsHostAllowed(host, adHoc.EffectiveHostSuffixes))
            throw new KintoneRequestException(
                "HOST_NOT_ALLOWED",
                $"網域「{host}」不在允許清單內：{string.Join("、", adHoc.EffectiveHostSuffixes)}。");

        return new KintoneConnection(
            "custom",
            $"{host} · App {input.AppId}",
            baseUrl,
            apiPath,
            input.AppId,
            token,
            AdHoc: true);
    }

    private static bool IsHostAllowed(string host, IReadOnlyList<string> suffixes)
    {
        if (suffixes.Contains("*")) return true;
        return suffixes.Any(suffix =>
            host.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static string ValidateBaseUrl(string value, string label, bool adHoc)
    {
        var raw = (value ?? "").Trim();
        if (raw.Length == 0)
        {
            if (adHoc) throw new KintoneRequestException("BASEURL_REQUIRED", "請輸入 Kintone 網址。");
            throw new KintoneConfigurationException($"Profile「{label}」尚未設定 BaseUrl。");
        }

        // 使用者常常整段網址貼進來（含 /k/100/ 之類的路徑），這裡只取通訊協定與主機。
        if (!raw.Contains("://", StringComparison.Ordinal)) raw = "https://" + raw;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            var message = $"「{label}」的網址必須是單純的 HTTPS 網址，例如 https://example.cybozu.com。";
            throw adHoc
                ? new KintoneRequestException("BASEURL_INVALID", message)
                : new KintoneConfigurationException(message);
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static string ValidateApiPath(string value, string label, bool adHoc)
    {
        var path = NormalizeApiPath(value);

        if (!GuestOrStandardApiPath().IsMatch(path))
        {
            var message =
                $"「{label}」的 API 路徑只允許 /k/v1/records.json 或 /k/guest/{{空間編號}}/v1/records.json。";
            throw adHoc
                ? new KintoneRequestException("APIPATH_INVALID", message)
                : new KintoneConfigurationException(message);
        }

        return path;
    }

    private static string NormalizeApiPath(string value)
    {
        var path = (value ?? "").Trim();
        if (path.Length == 0) return DefaultApiPath;
        if (!path.StartsWith('/')) path = "/" + path;
        return path;
    }

    private static bool ContainsControlChar(string value) =>
        value.Any(char.IsControl);

    [GeneratedRegex(@"^/k/(guest/\d{1,10}/)?v1/records\.json$", RegexOptions.IgnoreCase)]
    private static partial Regex GuestOrStandardApiPath();
}
