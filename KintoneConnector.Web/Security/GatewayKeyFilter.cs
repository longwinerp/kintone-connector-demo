using System.Net;
using System.Security.Cryptography;
using System.Text;
using KintoneConnector.Web.Options;
using Microsoft.Extensions.Options;

namespace KintoneConnector.Web.Security;

public sealed class GatewayKeyFilter(
    IOptionsMonitor<ConnectorSecurityOptions> options,
    IHostEnvironment environment) : IEndpointFilter
{
    public const string HeaderName = "X-Connector-Api-Key";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var expected = options.CurrentValue.GatewayApiKey?.Trim() ?? "";

        // 開發機第一次設定時，僅允許本機使用；Production 一律必須設定 GatewayApiKey。
        if (expected.Length == 0 &&
            environment.IsDevelopment() &&
            httpContext.Connection.RemoteIpAddress is { } remoteAddress &&
            IPAddress.IsLoopback(remoteAddress))
            return await next(context);

        if (expected.Length == 0)
            return Results.Json(
                new
                {
                    ok = false,
                    error = "GATEWAY_KEY_NOT_CONFIGURED",
                    message = "伺服器尚未設定 Connector API Key。"
                },
                statusCode: StatusCodes.Status503ServiceUnavailable);

        if (!httpContext.Request.Headers.TryGetValue(HeaderName, out var suppliedValues) ||
            !FixedTimeEquals(expected, suppliedValues.ToString()))
            return Results.Json(
                new
                {
                    ok = false,
                    error = "UNAUTHORIZED",
                    message = $"請在 {HeaderName} 標頭提供有效金鑰。"
                },
                statusCode: StatusCodes.Status401Unauthorized);

        return await next(context);
    }

    private static bool FixedTimeEquals(string expected, string supplied)
    {
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }
}
