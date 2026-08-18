namespace KintoneConnector.Web.Models;

/// <summary>呼叫端自行指定的 Kintone 連線（URL / App ID / API Token）。</summary>
public sealed class KintoneConnectionInput
{
    public string BaseUrl { get; set; } = "";
    public string ApiPath { get; set; } = "";
    public long AppId { get; set; }
    public string ApiToken { get; set; } = "";
}

/// <summary>只要連線資訊、不需要查詢條件的請求（例如讀取欄位定義、測試連線）。</summary>
public class KintoneConnectionRequest
{
    /// <summary>伺服器端設定檔中的 profile 代號；與 <see cref="Connection"/> 二擇一。</summary>
    public string Profile { get; set; } = "";

    /// <summary>由呼叫端直接帶入的連線資訊；有值時優先於 <see cref="Profile"/>。</summary>
    public KintoneConnectionInput? Connection { get; set; }
}

public sealed class KintoneQueryRequest : KintoneConnectionRequest
{
    public string Query { get; set; } = "";
    public string[] Fields { get; set; } = [];
    public bool TotalCount { get; set; } = true;

    /// <summary>單次筆數（1–500）。有值時會覆寫 Query 結尾的 limit 子句。</summary>
    public int? Limit { get; set; }

    /// <summary>位移（0–10000）。有值時會覆寫 Query 結尾的 offset 子句。</summary>
    public int? Offset { get; set; }

    /// <summary>整理成單頭單身時，是否一併讀取欄位定義以取得中文欄位名稱。</summary>
    public bool WithLabels { get; set; } = true;

    /// <summary>整理成單頭單身時，是否附上 Kintone 原始 JSON。</summary>
    public bool IncludeRaw { get; set; } = true;
}

/// <summary>已驗證、可直接拿去打 Kintone 的連線。</summary>
public sealed record KintoneConnection(
    string Code,
    string Label,
    string BaseUrl,
    string ApiPath,
    long AppId,
    string ApiToken,
    bool AdHoc,
    bool Demo = false)
{
    public string Source => Demo ? "demo" : AdHoc ? "custom" : "profile";
}

public sealed record KintoneProfileSummary(
    string Code,
    string DisplayName,
    string BaseUrl,
    string ApiPath,
    long AppId,
    bool Configured,
    bool IsDemo);

public sealed record KintoneRawResponse(int StatusCode, string Json);

// ── 單頭單身（Header / Detail）結構 ──────────────────────────────────

public sealed record KintoneColumn(string Code, string Label, string Type);

/// <summary>單頭：一筆 Kintone 紀錄扣掉子表格之後的欄位。</summary>
public sealed record KintoneHeaderRow(
    string Key,
    int Index,
    IReadOnlyDictionary<string, string?> Values,
    IReadOnlyDictionary<string, int> DetailCounts);

/// <summary>單身：子表格（SUBTABLE）中的一列。</summary>
public sealed record KintoneDetailRow(
    string ParentKey,
    string RowId,
    int Index,
    IReadOnlyDictionary<string, string?> Values);

public sealed record KintoneDetailTable(
    string Code,
    string Label,
    IReadOnlyList<KintoneColumn> Columns,
    IReadOnlyList<KintoneDetailRow> Rows)
{
    public int RowCount => Rows.Count;
}

public sealed record KintoneHeaderTable(
    IReadOnlyList<KintoneColumn> Columns,
    IReadOnlyList<KintoneHeaderRow> Rows)
{
    public int RowCount => Rows.Count;
}

public sealed record KintoneShapedResult(
    string? TotalCount,
    string KeyField,
    KintoneHeaderTable Header,
    IReadOnlyList<KintoneDetailTable> Details);

/// <summary>App 欄位定義；SUBTABLE 會帶出 <see cref="Children"/>。</summary>
public sealed record KintoneFieldMeta(
    string Code,
    string Label,
    string Type,
    IReadOnlyList<KintoneFieldMeta> Children);
