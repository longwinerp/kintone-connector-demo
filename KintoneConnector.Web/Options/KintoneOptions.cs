namespace KintoneConnector.Web.Options;

public sealed class KintoneOptions
{
    public const string SectionName = "Kintone";

    public int TimeoutSeconds { get; set; } = 30;
    public long MaxResponseBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>呼叫端自行輸入 URL / App ID / API Token 的規則。</summary>
    public KintoneAdHocOptions AdHoc { get; set; } = new();

    /// <summary>伺服器端預先保管好的連線；呼叫端只需要指定代號。</summary>
    public Dictionary<string, KintoneProfileOptions> Profiles { get; set; } = [];
}

public sealed class KintoneProfileOptions
{
    public string DisplayName { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string ApiPath { get; set; } = "/k/v1/records.json";
    public long AppId { get; set; }
    public string ApiToken { get; set; } = "";
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 示範用 profile：不會連出去，改由內建的假資料回應，
    /// 讓沒有 Kintone 帳號的人也能完整體驗這個查詢台。
    /// </summary>
    public bool IsDemo { get; set; }
}

public sealed class KintoneAdHocOptions
{
    /// <summary>是否允許呼叫端在請求中直接帶入 BaseUrl / AppId / ApiToken。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 允許連線的網域結尾白名單，用來擋掉把本閘道當跳板打內網的請求（SSRF）。
    /// 留空時採用 <see cref="DefaultHostSuffixes"/>；填入 "*" 代表不限制（僅建議封閉內網使用）。
    /// </summary>
    public List<string> AllowedHostSuffixes { get; set; } = [];

    public static readonly string[] DefaultHostSuffixes =
    [
        "cybozu.com",
        "kintone.com",
        "cybozu.cn",
        "kintone.cn",
        "cybozu-dev.com"
    ];

    public IReadOnlyList<string> EffectiveHostSuffixes
    {
        get
        {
            var configured = AllowedHostSuffixes
                .Select(value => (value ?? "").Trim().TrimStart('.').ToLowerInvariant())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return configured.Length > 0 ? configured : DefaultHostSuffixes;
        }
    }
}
