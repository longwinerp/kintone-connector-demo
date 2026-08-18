using KintoneConnector.Web.Models;

namespace KintoneConnector.Web.Services;

/// <summary>
/// Demo 版才有的一層轉接：連線標記為示範時改用內建假資料，其餘一律走真正的 <see cref="KintoneClient"/>。
/// 這樣示範資料同樣會經過 QueryComposer 與 RecordShaper，看到的就是真實流程的結果。
/// </summary>
public sealed class DemoAwareKintoneClient(KintoneClient inner, IDemoDataSource demo) : IKintoneClient
{
    public async Task<KintoneRawResponse> QueryAsync(
        KintoneConnection connection,
        KintoneQueryRequest input,
        string effectiveQuery,
        CancellationToken cancellationToken)
    {
        if (!connection.Demo)
            return await inner.QueryAsync(connection, input, effectiveQuery, cancellationToken);

        // 稍微延遲，讓畫面上的「查詢中…」與耗時數字看起來像真的在連線。
        await Task.Delay(80, cancellationToken);
        return demo.Query(effectiveQuery, input.Fields, input.TotalCount);
    }

    public async Task<IReadOnlyList<KintoneFieldMeta>> GetFieldsAsync(
        KintoneConnection connection,
        CancellationToken cancellationToken)
    {
        if (!connection.Demo)
            return await inner.GetFieldsAsync(connection, cancellationToken);

        await Task.Delay(60, cancellationToken);
        return demo.Fields();
    }
}
