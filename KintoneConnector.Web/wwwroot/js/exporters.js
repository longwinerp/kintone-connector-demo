// 匯出：CSV（含 BOM，Excel 直接開）與 JSON。

import { download, timestamp } from "./dom.js";

function cell(value) {
    const text = value === null || value === undefined ? "" : String(value);
    return /[",\r\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}

export function datasetToCsv(dataset, { useLabels = true } = {}) {
    const head = dataset.columns.map(column => cell(useLabels ? column.label : column.code));
    const body = dataset.rows.map(row =>
        dataset.columns.map(column => cell(row.cells[column.code])).join(","));
    return [head.join(","), ...body].join("\r\n");
}

export function downloadCsv(dataset, prefix = "kintone") {
    const safe = dataset.label.replace(/[\\/:*?"<>|]/g, "_");
    download(`${prefix}-${safe}-${timestamp()}.csv`, "﻿" + datasetToCsv(dataset), "text/csv");
}

export function downloadJson(filename, value) {
    const text = typeof value === "string" ? value : JSON.stringify(value, null, 2);
    download(`${filename}-${timestamp()}.json`, text, "application/json");
}
