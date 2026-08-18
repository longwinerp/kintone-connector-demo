using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KintoneConnector.Web.Models;
using KintoneConnector.Web.Options;
using Microsoft.Extensions.Options;

namespace KintoneConnector.Web.Services;

public interface IKintoneClient
{
    /// <summary>查詢紀錄，直接回傳 Kintone 原始 JSON 與其 HTTP 狀態碼。</summary>
    Task<KintoneRawResponse> QueryAsync(
        KintoneConnection connection,
        KintoneQueryRequest input,
        string effectiveQuery,
        CancellationToken cancellationToken);

    /// <summary>讀取 App 欄位定義（用來取得中文欄位名稱與子表格結構）。</summary>
    Task<IReadOnlyList<KintoneFieldMeta>> GetFieldsAsync(
        KintoneConnection connection,
        CancellationToken cancellationToken);
}

public sealed class KintoneClient(
    HttpClient httpClient,
    IOptionsMonitor<KintoneOptions> options,
    ILogger<KintoneClient> logger) : IKintoneClient
{
    public async Task<KintoneRawResponse> QueryAsync(
        KintoneConnection connection,
        KintoneQueryRequest input,
        string effectiveQuery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(input);

        var fields = (input.Fields ?? [])
            .Select(value => (value ?? "").Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (fields.Length > 1_000)
            throw new KintoneRequestException("TOO_MANY_FIELDS", "一次最多可指定 1,000 個欄位。");

        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            app = connection.AppId,
            query = effectiveQuery,
            fields,
            totalCount = input.TotalCount
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(connection, connection.ApiPath))
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        // Kintone 的 GET 查詢改用 POST + Method Override，避免長查詢字串塞爆 URL。
        request.Headers.Add("X-HTTP-Method-Override", "GET");

        logger.LogInformation(
            "Kintone query started. Source={Source}, AppId={AppId}, FieldCount={FieldCount}",
            connection.Source, connection.AppId, fields.Length);

        var result = await SendAsync(connection, request, cancellationToken);

        logger.LogInformation(
            "Kintone query completed. Source={Source}, AppId={AppId}, Status={Status}",
            connection.Source, connection.AppId, result.StatusCode);

        return result;
    }

    public async Task<IReadOnlyList<KintoneFieldMeta>> GetFieldsAsync(
        KintoneConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var path = connection.ApiPath.Replace(
            "records.json",
            "app/form/fields.json",
            StringComparison.OrdinalIgnoreCase);
        var uri = new Uri($"{BuildUri(connection, path)}?app={connection.AppId}&lang=default");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var response = await SendAsync(connection, request, cancellationToken);

        if (response.StatusCode != StatusCodes.Status200OK)
            throw new KintoneUpstreamException(
                "FIELDS_UNAVAILABLE",
                "無法讀取欄位定義；API Token 可能沒有「檢視應用程式管理畫面」權限。",
                response.StatusCode);

        return ParseFields(response.Json);
    }

    private static Uri BuildUri(KintoneConnection connection, string path)
    {
        var baseUri = new Uri(connection.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(baseUri, path.TrimStart('/'));
    }

    private async Task<KintoneRawResponse> SendAsync(
        KintoneConnection connection,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        request.Headers.Add("X-Cybozu-API-Token", connection.ApiToken);

        var timeoutSeconds = Math.Clamp(settings.TimeoutSeconds, 5, 120);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeoutSource.Token);
        var json = await ReadResponseAsync(
            response.Content,
            Math.Clamp(settings.MaxResponseBytes, 1024, 100L * 1024 * 1024),
            timeoutSource.Token);

        if (!IsJson(json))
        {
            logger.LogWarning(
                "Kintone returned non-JSON content. Source={Source}, Status={Status}",
                connection.Source, (int)response.StatusCode);
            throw new KintoneUpstreamException(
                "INVALID_RESPONSE",
                "Kintone 回傳的內容不是有效 JSON；請確認網址與 App ID 是否正確。",
                StatusCodes.Status502BadGateway);
        }

        return new KintoneRawResponse((int)response.StatusCode, json);
    }

    private static IReadOnlyList<KintoneFieldMeta> ParseFields(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
            return [];

        return properties.EnumerateObject()
            .Select(item => ReadField(item.Name, item.Value))
            .OrderBy(field => field.Label, StringComparer.CurrentCulture)
            .ToArray();
    }

    private static KintoneFieldMeta ReadField(string fallbackCode, JsonElement element)
    {
        var code = ReadString(element, "code") ?? fallbackCode;
        var label = ReadString(element, "label");
        var type = ReadString(element, "type") ?? "";

        IReadOnlyList<KintoneFieldMeta> children = [];
        if (element.TryGetProperty("fields", out var inner) && inner.ValueKind == JsonValueKind.Object)
            children = inner.EnumerateObject()
                .Select(item => ReadField(item.Name, item.Value))
                .ToArray();

        return new KintoneFieldMeta(
            code,
            string.IsNullOrWhiteSpace(label) ? code : label,
            type,
            children);
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static async Task<string> ReadResponseAsync(
        HttpContent content,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > 0 &&
            content.Headers.ContentLength.Value > maxBytes)
            throw new KintoneUpstreamException(
                "RESPONSE_TOO_LARGE",
                "Kintone 回傳資料超過系統允許大小。",
                StatusCodes.Status502BadGateway);

        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[81_920];
        long total = 0;

        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > maxBytes)
                throw new KintoneUpstreamException(
                    "RESPONSE_TOO_LARGE",
                    "Kintone 回傳資料超過系統允許大小。",
                    StatusCodes.Status502BadGateway);
            output.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
    }

    private static bool IsJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
