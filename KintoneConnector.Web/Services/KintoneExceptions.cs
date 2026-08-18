namespace KintoneConnector.Web.Services;

/// <summary>呼叫端輸入有問題（400）。</summary>
public sealed class KintoneRequestException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>伺服器端 profile 設定有問題（500）。</summary>
public sealed class KintoneConfigurationException(string message) : Exception(message);

/// <summary>Kintone 端回應異常（502 / 504 …）。</summary>
public sealed class KintoneUpstreamException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
