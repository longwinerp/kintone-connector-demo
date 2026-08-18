// 把後端回傳的單頭單身結構，攤成幾種可直接畫成表格的資料集：
//   header        單頭（每筆紀錄一列）
//   detail:{代碼}  單身（子表格原樣，帶父紀錄鍵值）
//   join:{代碼}    單頭 ＋ 單身合併（ERP 常用的明細報表格式）

export const PARENT_KEY = "__parent";
export const ROW_NO = "__row";

export function buildDatasets(result) {
    if (!result?.header) return [];

    const header = result.header;
    const datasets = [{
        id: "header",
        label: "單頭（紀錄）",
        kind: "header",
        columns: header.columns,
        rows: header.rows.map(row => ({ key: row.key, cells: row.values }))
    }];

    for (const table of result.details ?? []) {
        datasets.push({
            id: `detail:${table.code}`,
            label: `單身：${table.label}`,
            kind: "detail",
            detailCode: table.code,
            columns: [
                { code: PARENT_KEY, label: "單頭鍵值", type: "__KEY__" },
                { code: ROW_NO, label: "列", type: "NUMBER" },
                ...table.columns
            ],
            rows: table.rows.map(row => ({
                key: row.parentKey,
                cells: { [PARENT_KEY]: row.parentKey, [ROW_NO]: String(row.index + 1), ...row.values }
            }))
        });

        datasets.push({
            id: `join:${table.code}`,
            label: `單頭＋${table.label}（合併）`,
            kind: "join",
            detailCode: table.code,
            columns: [
                ...header.columns,
                { code: ROW_NO, label: `${table.label}·列`, type: "NUMBER" },
                ...table.columns.map(column => ({
                    code: `${table.code}::${column.code}`,
                    label: `${table.label}·${column.label}`,
                    type: column.type
                }))
            ],
            rows: buildJoinRows(header, table)
        });
    }

    return datasets;
}

function buildJoinRows(header, table) {
    const parents = new Map(header.rows.map(row => [row.key, row.values]));
    return table.rows.map(row => {
        const cells = { ...(parents.get(row.parentKey) ?? {}), [ROW_NO]: String(row.index + 1) };
        for (const [code, value] of Object.entries(row.values)) cells[`${table.code}::${code}`] = value;
        return { key: row.parentKey, cells };
    });
}

export function findDataset(datasets, id) {
    return datasets.find(item => item.id === id) ?? datasets[0] ?? null;
}
