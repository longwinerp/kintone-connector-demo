using System.Text.RegularExpressions;

namespace KintoneConnector.Web.Services;

/// <summary>
/// 把使用者輸入的 Kintone Query 與畫面上的分頁設定合併。
/// Kintone 規定 limit / offset 只能出現在查詢字串結尾，所以這裡只處理結尾的子句。
/// </summary>
public static partial class KintoneQueryComposer
{
    public const int MaxLimit = 500;
    public const int MaxOffset = 10_000;
    public const int MaxQueryLength = 32_000;

    public static string Compose(string? query, int? limit, int? offset)
    {
        var text = (query ?? "").Trim();

        if (limit is null && offset is null)
        {
            EnsureLength(text);
            return text;
        }

        if (limit is { } l && (l < 1 || l > MaxLimit))
            throw new KintoneRequestException("LIMIT_OUT_OF_RANGE", $"單次筆數必須介於 1 至 {MaxLimit}。");
        if (offset is { } o && (o < 0 || o > MaxOffset))
            throw new KintoneRequestException("OFFSET_OUT_OF_RANGE", $"位移必須介於 0 至 {MaxOffset}。");

        var body = TrailingPaging().Replace(text, "").TrimEnd();
        var builder = new List<string>(3);
        if (body.Length > 0) builder.Add(body);
        if (limit is { } limitValue) builder.Add($"limit {limitValue}");
        if (offset is { } offsetValue and > 0) builder.Add($"offset {offsetValue}");

        var composed = string.Join(' ', builder);
        EnsureLength(composed);
        return composed;
    }

    private static void EnsureLength(string value)
    {
        if (value.Length > MaxQueryLength)
            throw new KintoneRequestException(
                "QUERY_TOO_LONG",
                $"Kintone Query 不可超過 {MaxQueryLength:N0} 個字元。");
    }

    [GeneratedRegex(@"(\s*\b(limit|offset)\s+\d+\b)+\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingPaging();
}
