using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using KintoneConnector.Web.Models;

namespace KintoneConnector.Web.Services;

public interface IDemoDataSource
{
    /// <summary>回傳與 Kintone `records.json` 相同格式的示範資料。</summary>
    KintoneRawResponse Query(string effectiveQuery, string[] fields, bool totalCount);

    /// <summary>回傳與 Kintone `app/form/fields.json` 對應的欄位定義。</summary>
    IReadOnlyList<KintoneFieldMeta> Fields();
}

/// <summary>
/// Demo 版專用的假資料來源：一份「請款單」App，含兩張子表格（費用明細、簽核紀錄），
/// 讓沒有 Kintone 帳號的人也能完整體驗單頭單身的呈現方式。
///
/// 支援 `limit` / `offset` / `order by $id asc|desc` 與欄位篩選；
/// 其餘查詢條件（where 子句）在示範模式下會被忽略。
/// </summary>
public sealed partial class DemoDataSource : IDemoDataSource
{
    private const int TotalRecords = 36;

    private static readonly string[] BillTypes = ["匯款一次付清", "支票", "零用金撥補", "信用卡"];
    private static readonly string[] Currencies = ["NTD", "NTD", "NTD", "USD", "JPY"];
    private static readonly string[] Statuses = ["進行中", "已完成", "已完成", "退回"];
    private static readonly string[] Reasons =
    [
        "零用金撥補", "設備維修費", "差旅費用報支", "教育訓練費", "辦公用品採購",
        "外包加工費", "客戶招待費", "軟體授權年費", "運費與報關費", "廠務水電費"
    ];
    private static readonly string[] Accounts =
    [
        "文具用品", "修繕維護", "旅費", "運費", "水電瓦斯", "教育訓練", "郵電費", "雜項購置"
    ];
    private static readonly string[] Vendors =
    [
        "昕福喜科技有限公司", "全誠工業社", "宏遠精密有限公司", "群力機械股份有限公司",
        "建順物流有限公司", "順興電機行", "永新文具行"
    ];
    private static readonly string[] Members =
    [
        "王小明", "陳怡君", "林建宏", "張淑芬", "黃士豪", "吳佩璇", "李家瑋"
    ];
    private static readonly string[] Departments = ["業務部", "生產技術部", "管理部", "資材部"];
    private static readonly string[] InvoiceTypes = ["電子發票或收銀式發票", "三聯式發票", "二聯式發票"];
    private static readonly string[] ApprovalSteps = ["部門主管", "會計覆核", "財務經理", "總經理"];

    private static readonly Lazy<IReadOnlyList<Dictionary<string, object?>>> Records =
        new(BuildRecords, LazyThreadSafetyMode.ExecutionAndPublication);

    public KintoneRawResponse Query(string effectiveQuery, string[] fields, bool totalCount)
    {
        var all = Records.Value;
        var query = effectiveQuery ?? "";

        var ordered = DescendingOrder().IsMatch(query)
            ? all.Reverse().ToList()
            : all.ToList();

        var offset = ReadNumber(OffsetClause(), query, 0);
        var limit = ReadNumber(LimitClause(), query, 100);
        var page = ordered.Skip(offset).Take(Math.Clamp(limit, 1, 500)).ToList();

        var wanted = (fields ?? [])
            .Select(field => (field ?? "").Trim())
            .Where(field => field.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var records = wanted.Count == 0
            ? page
            : page.Select(record => record
                    .Where(pair => wanted.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
                .ToList();

        var payload = totalCount
            ? new Dictionary<string, object?> { ["records"] = records, ["totalCount"] = all.Count.ToString() }
            : new Dictionary<string, object?> { ["records"] = records };

        return new KintoneRawResponse(
            StatusCodes.Status200OK,
            JsonSerializer.Serialize(payload, JsonOptions));
    }

    public IReadOnlyList<KintoneFieldMeta> Fields() =>
    [
        new("billNo", "請款單號", "SINGLE_LINE_TEXT", []),
        new("billType", "請款型態", "DROP_DOWN", []),
        new("reason", "事由", "SINGLE_LINE_TEXT", []),
        new("amount", "待付款金額", "NUMBER", []),
        new("currency", "幣別", "DROP_DOWN", []),
        new("payee", "付款對象", "SINGLE_LINE_TEXT", []),
        new("dept", "申請部門", "ORGANIZATION_SELECT", []),
        new("applicant", "申請人", "USER_SELECT", []),
        new("manager", "部門最高管理者", "USER_SELECT", []),
        new("status", "簽核狀態", "DROP_DOWN", []),
        new("applyDate", "申請日", "DATE", []),
        new("attachment", "發票附檔", "FILE", []),
        new("memo", "備註", "MULTI_LINE_TEXT", []),
        new("items", "費用明細", "SUBTABLE",
        [
            new("seq", "NO", "NUMBER", []),
            new("account", "費用科目", "DROP_DOWN", []),
            new("summary", "摘要", "SINGLE_LINE_TEXT", []),
            new("unitPrice", "單價", "NUMBER", []),
            new("qty", "數量", "NUMBER", []),
            new("taxable", "是否課稅", "DROP_DOWN", []),
            new("tax", "稅金", "NUMBER", []),
            new("invoiceType", "發票種類", "DROP_DOWN", []),
            new("invoiceNo", "憑證:發票號碼", "SINGLE_LINE_TEXT", []),
            new("total", "總價", "NUMBER", [])
        ]),
        new("approvals", "簽核紀錄", "SUBTABLE",
        [
            new("step", "關卡", "SINGLE_LINE_TEXT", []),
            new("approver", "簽核人", "USER_SELECT", []),
            new("approvedAt", "簽核時間", "DATETIME", []),
            new("comment", "意見", "SINGLE_LINE_TEXT", [])
        ])
    ];

    // ── 產生資料 ────────────────────────────────────────────────────

    // 與 Program.cs 的設定一致：中文直接輸出 UTF-8，不要變成 \uXXXX。
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(
            UnicodeRanges.BasicLatin,
            UnicodeRanges.CjkUnifiedIdeographs,
            UnicodeRanges.CjkSymbolsandPunctuation,
            UnicodeRanges.HalfwidthandFullwidthForms)
    };

    private static List<Dictionary<string, object?>> BuildRecords()
    {
        // 固定種子，每次啟動看到的示範資料都一樣，方便截圖與比對。
        var random = new Random(20260816);
        var records = new List<Dictionary<string, object?>>(TotalRecords);

        for (var index = 0; index < TotalRecords; index++)
        {
            var id = index + 1;
            var applyDate = new DateOnly(2026, 1, 1).AddDays(random.Next(0, 220));
            var currency = Pick(random, Currencies);
            var itemCount = random.Next(1, 10);
            var items = new List<object>(itemCount);
            long amount = 0;

            for (var line = 0; line < itemCount; line++)
            {
                var unitPrice = random.Next(1, 40) * 50L;
                var qty = random.Next(1, 12);
                var taxable = random.Next(0, 3) > 0;
                var subtotal = unitPrice * qty;
                var tax = taxable ? (long)Math.Round(subtotal * 0.05) : 0;
                amount += subtotal + tax;

                items.Add(new
                {
                    id = (1000 + id * 20 + line).ToString(),
                    value = new Dictionary<string, object?>
                    {
                        ["seq"] = Field("NUMBER", (line + 1).ToString()),
                        ["account"] = Field("DROP_DOWN", Pick(random, Accounts)),
                        ["summary"] = Field("SINGLE_LINE_TEXT", $"{Pick(random, Reasons)} - 第 {line + 1} 項"),
                        ["unitPrice"] = Field("NUMBER", unitPrice.ToString()),
                        ["qty"] = Field("NUMBER", qty.ToString()),
                        ["taxable"] = Field("DROP_DOWN", taxable ? "課稅" : "免稅"),
                        ["tax"] = Field("NUMBER", taxable ? tax.ToString() : ""),
                        ["invoiceType"] = Field("DROP_DOWN", taxable ? Pick(random, InvoiceTypes) : ""),
                        ["invoiceNo"] = Field(
                            "SINGLE_LINE_TEXT",
                            taxable ? $"{(char)random.Next('A', 'Z')}{(char)random.Next('A', 'Z')}{random.Next(10000000, 99999999)}" : ""),
                        ["total"] = Field("NUMBER", (subtotal + tax).ToString())
                    }
                });
            }

            var status = Pick(random, Statuses);
            var approvalCount = status == "進行中" ? random.Next(1, 3) : random.Next(2, 5);
            var approvals = new List<object>(approvalCount);
            for (var step = 0; step < approvalCount; step++)
                approvals.Add(new
                {
                    id = (5000 + id * 10 + step).ToString(),
                    value = new Dictionary<string, object?>
                    {
                        ["step"] = Field("SINGLE_LINE_TEXT", ApprovalSteps[step % ApprovalSteps.Length]),
                        ["approver"] = User(Pick(random, Members)),
                        ["approvedAt"] = Field(
                            "DATETIME",
                            applyDate.AddDays(step + 1).ToDateTime(new TimeOnly(random.Next(9, 18), 0))
                                .ToString("yyyy-MM-ddTHH:mm:ssZ")),
                        ["comment"] = Field("SINGLE_LINE_TEXT", step == 0 ? "確認無誤" : "")
                    }
                });

            var hasAttachment = random.Next(0, 3) > 0;
            var applicant = Pick(random, Members);

            records.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["$id"] = Field("__ID__", id.ToString()),
                ["recNo"] = Field("RECORD_NUMBER", $"PR-2026-{id:D4}"),
                ["billNo"] = Field("SINGLE_LINE_TEXT", $"ZZ{random.Next(6100, 6199)}"),
                ["billType"] = Field("DROP_DOWN", Pick(random, BillTypes)),
                ["reason"] = Field("SINGLE_LINE_TEXT", Pick(random, Reasons)),
                ["amount"] = Field("NUMBER", amount.ToString()),
                ["currency"] = Field("DROP_DOWN", currency),
                ["payee"] = Field("SINGLE_LINE_TEXT", Pick(random, Vendors)),
                ["dept"] = new
                {
                    type = "ORGANIZATION_SELECT",
                    value = new[] { new { code = "org", name = Pick(random, Departments) } }
                },
                ["applicant"] = User(applicant),
                ["manager"] = User(Pick(random, Members)),
                ["status"] = Field("DROP_DOWN", status),
                ["applyDate"] = Field("DATE", applyDate.ToString("yyyy-MM-dd")),
                ["attachment"] = new
                {
                    type = "FILE",
                    value = hasAttachment
                        ? new[] { new { fileKey = $"demo-{id}", name = $"發票-{id:D4}.pdf", size = "20480" } }
                        : []
                },
                ["memo"] = Field("MULTI_LINE_TEXT", random.Next(0, 4) == 0 ? "急件，請優先處理。" : ""),
                ["items"] = new { type = "SUBTABLE", value = items },
                ["approvals"] = new { type = "SUBTABLE", value = approvals },
                ["建立者"] = User(applicant),
                ["建立時間"] = Field(
                    "CREATED_TIME",
                    applyDate.ToDateTime(new TimeOnly(9, 30)).ToString("yyyy-MM-ddTHH:mm:ssZ")),
                ["更新時間"] = Field(
                    "UPDATED_TIME",
                    applyDate.AddDays(approvalCount).ToDateTime(new TimeOnly(16, 45)).ToString("yyyy-MM-ddTHH:mm:ssZ")),
                ["$revision"] = Field("__REVISION__", (approvalCount + 1).ToString())
            });
        }

        return records;
    }

    private static object Field(string type, string value) => new { type, value };

    private static object User(string name) => new
    {
        type = "USER_SELECT",
        value = new[] { new { code = name, name } }
    };

    private static string Pick(Random random, string[] source) => source[random.Next(source.Length)];

    private static int ReadNumber(Regex pattern, string query, int fallback)
    {
        var match = pattern.Match(query);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : fallback;
    }

    [GeneratedRegex(@"\blimit\s+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex LimitClause();

    [GeneratedRegex(@"\boffset\s+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetClause();

    [GeneratedRegex(@"order\s+by\s+[^\s]+\s+desc", RegexOptions.IgnoreCase)]
    private static partial Regex DescendingOrder();
}
