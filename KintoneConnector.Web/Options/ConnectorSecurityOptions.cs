namespace KintoneConnector.Web.Options;

public sealed class ConnectorSecurityOptions
{
    public const string SectionName = "Security";

    public string GatewayApiKey { get; set; } = "";
}
