using System.Text.Json;
using KintoneConnector.Web.Models;

namespace KintoneConnector.Web.Services;

/// <summary>
/// 把 Kintone 的 records JSON 攤平成「單頭單身」：
/// 一般欄位 → 單頭（每筆紀錄一列）；SUBTABLE 子表格 → 單身（每個子表格一張表，列上帶父紀錄鍵值）。
/// </summary>
public static class KintoneRecordShaper
{
    private const string SubtableType = "SUBTABLE";
    private const string RecordNumberType = "RECORD_NUMBER";

    /// <summary>排在最前面的識別欄位。</summary>
    private static readonly string[] LeadingTypes = ["__ID__", RecordNumberType];

    /// <summary>排在最後面的系統欄位。</summary>
    private static readonly string[] TrailingTypes =
    [
        "__REVISION__", "CREATOR", "CREATED_TIME", "MODIFIER", "UPDATED_TIME",
        "STATUS", "STATUS_ASSIGNEE"
    ];

    public static KintoneShapedResult Shape(string json, IReadOnlyList<KintoneFieldMeta>? fieldMeta)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var totalCount = root.ValueKind == JsonValueKind.Object &&
                         root.TryGetProperty("totalCount", out var totalCountElement) &&
                         totalCountElement.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? totalCountElement.ToString()
            : null;

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("records", out var records) ||
            records.ValueKind != JsonValueKind.Array)
            return new KintoneShapedResult(
                totalCount,
                "",
                new KintoneHeaderTable([], []),
                []);

        var labels = BuildLabelIndex(fieldMeta);
        var headerColumns = new ColumnCollector(labels);
        var detailColumns = new Dictionary<string, ColumnCollector>(StringComparer.Ordinal);
        var detailRows = new Dictionary<string, List<KintoneDetailRow>>(StringComparer.Ordinal);
        var headerRows = new List<KintoneHeaderRow>();
        var keyField = "";
        var index = 0;

        foreach (var record in records.EnumerateArray())
        {
            if (record.ValueKind != JsonValueKind.Object) continue;

            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var key = ResolveKey(record, index, ref keyField);

            foreach (var field in record.EnumerateObject())
            {
                var type = ReadType(field.Value);
                if (type == SubtableType)
                {
                    counts[field.Name] = CollectDetail(
                        field.Name, field.Value, key, labels, detailColumns, detailRows);
                    continue;
                }

                headerColumns.Touch(field.Name, type);
                values[field.Name] = FormatField(field.Value);
            }

            headerRows.Add(new KintoneHeaderRow(key, index, values, counts));
            index++;
        }

        var details = detailRows
            .Select(item => new KintoneDetailTable(
                item.Key,
                labels.TryGetValue(item.Key, out var meta) ? meta.Label : item.Key,
                detailColumns[item.Key].Build(),
                item.Value))
            .OrderBy(table => table.Label, StringComparer.CurrentCulture)
            .ToArray();

        return new KintoneShapedResult(
            totalCount,
            keyField,
            new KintoneHeaderTable(headerColumns.Build(), headerRows),
            details);
    }

    private static int CollectDetail(
        string code,
        JsonElement field,
        string parentKey,
        IReadOnlyDictionary<string, KintoneFieldMeta> labels,
        Dictionary<string, ColumnCollector> detailColumns,
        Dictionary<string, List<KintoneDetailRow>> detailRows)
    {
        labels.TryGetValue(code, out var tableMeta);
        var childLabels = tableMeta is null
            ? new Dictionary<string, KintoneFieldMeta>(StringComparer.Ordinal)
            : tableMeta.Children.ToDictionary(child => child.Code, StringComparer.Ordinal);

        if (!detailColumns.TryGetValue(code, out var columns))
            detailColumns[code] = columns = new ColumnCollector(childLabels);
        if (!detailRows.TryGetValue(code, out var rows))
            detailRows[code] = rows = [];

        if (!field.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
            return 0;

        var added = 0;
        foreach (var row in value.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) continue;

            var rowId = row.TryGetProperty("id", out var id) ? id.ToString() : "";
            var cells = new Dictionary<string, string?>(StringComparer.Ordinal);

            if (row.TryGetProperty("value", out var cellObject) &&
                cellObject.ValueKind == JsonValueKind.Object)
                foreach (var cell in cellObject.EnumerateObject())
                {
                    columns.Touch(cell.Name, ReadType(cell.Value));
                    cells[cell.Name] = FormatField(cell.Value);
                }

            rows.Add(new KintoneDetailRow(parentKey, rowId, added, cells));
            added++;
        }

        return added;
    }

    private static string ResolveKey(JsonElement record, int index, ref string keyField)
    {
        if (record.TryGetProperty("$id", out var id) &&
            FormatField(id) is { Length: > 0 } idValue)
        {
            if (keyField.Length == 0) keyField = "$id";
            return idValue;
        }

        foreach (var field in record.EnumerateObject())
        {
            if (ReadType(field.Value) != RecordNumberType) continue;
            if (FormatField(field.Value) is not { Length: > 0 } number) continue;
            if (keyField.Length == 0) keyField = field.Name;
            return number;
        }

        if (keyField.Length == 0) keyField = "#";
        return (index + 1).ToString();
    }

    private static string ReadType(JsonElement field) =>
        field.ValueKind == JsonValueKind.Object &&
        field.TryGetProperty("type", out var type) &&
        type.ValueKind == JsonValueKind.String
            ? type.GetString() ?? ""
            : "";

    /// <summary>把 { "type": ..., "value": ... } 轉成可直接顯示的字串。</summary>
    private static string? FormatField(JsonElement field)
    {
        if (field.ValueKind != JsonValueKind.Object) return FormatValue(field);
        if (!field.TryGetProperty("value", out var value)) return null;
        return FormatValue(value);
    }

    private static string? FormatValue(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            case JsonValueKind.String:
                var text = value.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                return value.ToString();
            case JsonValueKind.Array:
                var items = value.EnumerateArray()
                    .Select(FormatValue)
                    .Where(item => !string.IsNullOrEmpty(item))
                    .ToArray();
                return items.Length == 0 ? null : string.Join("、", items);
            case JsonValueKind.Object:
                // 使用者、組織、群組、附件等欄位都是物件，優先取得可讀的名稱。
                foreach (var candidate in new[] { "name", "code", "value" })
                    if (value.TryGetProperty(candidate, out var inner) &&
                        FormatValue(inner) is { Length: > 0 } formatted)
                        return formatted;
                return value.GetRawText();
            default:
                return null;
        }
    }

    private static Dictionary<string, KintoneFieldMeta> BuildLabelIndex(
        IReadOnlyList<KintoneFieldMeta>? fieldMeta)
    {
        var index = new Dictionary<string, KintoneFieldMeta>(StringComparer.Ordinal);
        foreach (var field in fieldMeta ?? [])
            index[field.Code] = field;
        return index;
    }

    /// <summary>依「第一次出現的順序」收集欄位，並把識別欄位排前面、系統欄位排後面。</summary>
    private sealed class ColumnCollector(IReadOnlyDictionary<string, KintoneFieldMeta> labels)
    {
        private readonly Dictionary<string, (int Order, string Type)> seen = new(StringComparer.Ordinal);

        public void Touch(string code, string type)
        {
            if (seen.ContainsKey(code)) return;
            seen[code] = (seen.Count, type);
        }

        public IReadOnlyList<KintoneColumn> Build() =>
            seen
                .OrderBy(item => Rank(item.Key, item.Value.Type))
                .ThenBy(item => item.Value.Order)
                .Select(item => new KintoneColumn(
                    item.Key,
                    labels.TryGetValue(item.Key, out var meta) ? meta.Label : item.Key,
                    item.Value.Type))
                .ToArray();

        private static int Rank(string code, string type)
        {
            if (LeadingTypes.Contains(type, StringComparer.Ordinal) || code == "$id") return 0;
            if (TrailingTypes.Contains(type, StringComparer.Ordinal) || code == "$revision") return 2;
            return 1;
        }
    }
}
