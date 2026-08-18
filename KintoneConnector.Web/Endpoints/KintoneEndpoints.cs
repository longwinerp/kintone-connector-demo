using System.Diagnostics;
using System.Text;
using System.Text.Json;
using KintoneConnector.Web.Models;
using KintoneConnector.Web.Security;
using KintoneConnector.Web.Services;

namespace KintoneConnector.Web.Endpoints;

public static class KintoneEndpoints
{
    public static void MapKintoneEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api")
            .AddEndpointFilter<GatewayKeyFilter>();

        api.MapGet("/profiles", (IKintoneConnectionResolver resolver) => Results.Json(new
        {
            ok = true,
            adHocEnabled = resolver.AdHocEnabled,
            allowedHostSuffixes = resolver.AllowedHostSuffixes,
            profiles = resolver.GetProfiles()
        }));

        // 原始 Kintone JSON（維持既有外部程式的呼叫方式）
        api.MapGet("/kintone/query", QueryFromGetAsync);
        api.MapPost("/kintone/query", QueryFromPostAsync);

        // 整理成單頭單身的結果（給前端與需要現成表格的呼叫端）
        api.MapPost("/kintone/records", RecordsAsync);

        // App 欄位定義（欄位挑選器、中文欄位名稱）
        api.MapPost("/kintone/fields", FieldsAsync);
    }

    private static Task<IResult> QueryFromGetAsync(
        HttpRequest request,
        HttpResponse response,
        IKintoneConnectionResolver resolver,
        IKintoneClient client,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var fields = request.Query["fields"]
            .SelectMany(value => (value ?? "").Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
        var hasTotalCount = request.Query.ContainsKey("totalCount");
        var totalCount = !hasTotalCount ||
            (bool.TryParse(request.Query["totalCount"], out var parsed) && parsed);

        var input = new KintoneQueryRequest
        {
            Profile = request.Query["profile"].ToString(),
            Query = request.Query["query"].ToString(),
            Fields = fields,
            TotalCount = totalCount,
            Limit = ReadInt(request, "limit"),
            Offset = ReadInt(request, "offset")
        };

        // 自訂連線也可以走 GET，但 API Token 一律只能放在 Header，不可出現在網址列。
        var baseUrl = request.Query["baseUrl"].ToString();
        var appId = ReadLong(request, "appId");
        if (!string.IsNullOrWhiteSpace(baseUrl) || appId > 0)
            input.Connection = new KintoneConnectionInput
            {
                BaseUrl = baseUrl,
                ApiPath = request.Query["apiPath"].ToString(),
                AppId = appId,
                ApiToken = request.Headers["X-Kintone-Api-Token"].ToString()
            };

        return RunAsync(
            () => QueryRawAsync(input, resolver, client, cancellationToken),
            response,
            loggerFactory,
            cancellationToken);
    }

    private static Task<IResult> QueryFromPostAsync(
        KintoneQueryRequest input,
        HttpResponse response,
        IKintoneConnectionResolver resolver,
        IKintoneClient client,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
        RunAsync(
            () => QueryRawAsync(input, resolver, client, cancellationToken),
            response,
            loggerFactory,
            cancellationToken);

    private static async Task<IResult> QueryRawAsync(
        KintoneQueryRequest input,
        IKintoneConnectionResolver resolver,
        IKintoneClient client,
        CancellationToken cancellationToken)
    {
        var connection = resolver.Resolve(input);
        var query = KintoneQueryComposer.Compose(input.Query, input.Limit, input.Offset);
        var result = await client.QueryAsync(connection, input, query, cancellationToken);

        return Results.Text(
            result.Json,
            "application/json; charset=utf-8",
            Encoding.UTF8,
            result.StatusCode);
    }

    private static Task<IResult> RecordsAsync(
        KintoneQueryRequest input,
        HttpResponse response,
        IKintoneConnectionResolver resolver,
        IKintoneClient client,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
        RunAsync(
            async () =>
            {
                var connection = resolver.Resolve(input);
                var query = KintoneQueryComposer.Compose(input.Query, input.Limit, input.Offset);
                var stopwatch = Stopwatch.StartNew();
                var result = await client.QueryAsync(connection, input, query, cancellationToken);
                var elapsed = stopwatch.ElapsedMilliseconds;

                if (result.StatusCode != StatusCodes.Status200OK)
                    return KintoneError(result, connection);

                IReadOnlyList<KintoneFieldMeta> fields = [];
                string? fieldsWarning = null;
                if (input.WithLabels)
                    try
                    {
                        fields = await client.GetFieldsAsync(connection, cancellationToken);
                    }
                    catch (Exception ex) when (ex is KintoneUpstreamException or HttpRequestException)
                    {
                        // 欄位定義只是為了顯示中文名稱，取不到就退回顯示欄位代碼。
                        fieldsWarning = "無法讀取欄位定義，改以欄位代碼顯示。";
                    }

                var shaped = KintoneRecordShaper.Shape(result.Json, fields);

                return Results.Json(new
                {
                    ok = true,
                    statusCode = result.StatusCode,
                    elapsedMs = elapsed,
                    connection = new
                    {
                        label = connection.Label,
                        baseUrl = connection.BaseUrl,
                        appId = connection.AppId,
                        source = connection.Source
                    },
                    query = new { effective = query, input.Limit, input.Offset },
                    totalCount = shaped.TotalCount,
                    recordCount = shaped.Header.RowCount,
                    keyField = shaped.KeyField,
                    header = shaped.Header,
                    details = shaped.Details,
                    fields = fields.Select(field => new
                    {
                        field.Code,
                        field.Label,
                        field.Type,
                        children = field.Children.Select(child => new
                        {
                            child.Code,
                            child.Label,
                            child.Type
                        })
                    }),
                    fieldsWarning,
                    raw = input.IncludeRaw ? result.Json : null
                });
            },
            response,
            loggerFactory,
            cancellationToken);

    private static Task<IResult> FieldsAsync(
        KintoneConnectionRequest input,
        HttpResponse response,
        IKintoneConnectionResolver resolver,
        IKintoneClient client,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
        RunAsync(
            async () =>
            {
                var connection = resolver.Resolve(input);
                var fields = await client.GetFieldsAsync(connection, cancellationToken);

                return Results.Json(new
                {
                    ok = true,
                    connection = new
                    {
                        label = connection.Label,
                        baseUrl = connection.BaseUrl,
                        appId = connection.AppId,
                        source = connection.Source
                    },
                    fields = fields.Select(field => new
                    {
                        field.Code,
                        field.Label,
                        field.Type,
                        children = field.Children.Select(child => new
                        {
                            child.Code,
                            child.Label,
                            child.Type
                        })
                    })
                });
            },
            response,
            loggerFactory,
            cancellationToken);

    /// <summary>把所有端點共用的錯誤處理集中在同一個地方。</summary>
    private static async Task<IResult> RunAsync(
        Func<Task<IResult>> action,
        HttpResponse response,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            return await action();
        }
        catch (KintoneRequestException ex)
        {
            return Error(StatusCodes.Status400BadRequest, ex.Code, ex.Message);
        }
        catch (KintoneConfigurationException ex)
        {
            loggerFactory.CreateLogger("KintoneEndpoints")
                .LogError(ex, "Kintone profile configuration error.");
            return Error(
                StatusCodes.Status500InternalServerError,
                "PROFILE_NOT_CONFIGURED",
                ex.Message);
        }
        catch (KintoneUpstreamException ex)
        {
            return Error(ex.StatusCode, ex.Code, ex.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error(
                StatusCodes.Status504GatewayTimeout,
                "KINTONE_TIMEOUT",
                "Kintone 查詢逾時。");
        }
        catch (HttpRequestException ex)
        {
            loggerFactory.CreateLogger("KintoneEndpoints")
                .LogError(ex, "Kintone connection failed.");
            return Error(
                StatusCodes.Status502BadGateway,
                "KINTONE_UNAVAILABLE",
                "目前無法連線 Kintone，請確認網址是否正確。");
        }
        catch (JsonException)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "INVALID_JSON",
                "JSON 請求內容格式不正確。");
        }
    }

    /// <summary>Kintone 回了非 200：把它的錯誤碼與訊息原封不動帶回前端。</summary>
    private static IResult KintoneError(KintoneRawResponse result, KintoneConnection connection)
    {
        string code = "KINTONE_ERROR";
        string message = $"Kintone 回應 HTTP {result.StatusCode}。";

        try
        {
            using var document = JsonDocument.Parse(result.Json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (document.RootElement.TryGetProperty("code", out var codeElement) &&
                    codeElement.ValueKind == JsonValueKind.String)
                    code = codeElement.GetString() ?? code;
                if (document.RootElement.TryGetProperty("message", out var messageElement) &&
                    messageElement.ValueKind == JsonValueKind.String)
                    message = messageElement.GetString() ?? message;
            }
        }
        catch (JsonException)
        {
            // 保留預設訊息即可。
        }

        return Results.Json(
            new
            {
                ok = false,
                error = code,
                message,
                statusCode = result.StatusCode,
                connection = new { label = connection.Label, appId = connection.AppId },
                raw = result.Json
            },
            statusCode: result.StatusCode);
    }

    private static int? ReadInt(HttpRequest request, string name) =>
        int.TryParse(request.Query[name], out var value) ? value : null;

    private static long ReadLong(HttpRequest request, string name) =>
        long.TryParse(request.Query[name], out var value) ? value : 0;

    private static IResult Error(int statusCode, string code, string message) =>
        Results.Json(
            new { ok = false, error = code, message },
            statusCode: statusCode);
}
