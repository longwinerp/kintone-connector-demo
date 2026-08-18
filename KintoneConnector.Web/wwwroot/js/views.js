// 結果檢視：單頭單身 / 總表 / 卡片 / JSON。
// 這一層只負責「把資料畫出來」，查詢與連線設定留在 app.js。

import { $, $$, escapeHtml, display, isNumeric, toNumber, formatCount, toast, closeOnOutsideClick, EMPTY } from "./dom.js";
import { prefs } from "./store.js";
import { buildDatasets, findDataset, PARENT_KEY, ROW_NO } from "./datasets.js";
import { downloadCsv, downloadJson } from "./exporters.js";

const MAX_TABLE_ROWS = 2000;
const MAX_CARDS = 300;

const state = {
    result: null,
    datasets: [],
    selectedKey: null,
    recordFilter: "",
    cardFilter: "",
    hideEmpty: false,
    view: "masterDetail",
    table: { sourceId: "header", search: "", sort: null, hidden: new Map() }
};

const dom = {};

export function initViews() {
    Object.assign(dom, {
        metaStatus: $("#metaStatus"),
        metaCount: $("#metaCount"),
        metaTotal: $("#metaTotal"),
        metaDetails: $("#metaDetails"),
        metaElapsed: $("#metaElapsed"),
        metaQuery: $("#metaQuery"),
        recordSearch: $("#recordSearch"),
        recordCountBadge: $("#recordCountBadge"),
        recordList: $("#recordList"),
        detailPane: $("#detailPane"),
        mdSplit: $("#mdSplit"),
        listToggle: $("#listToggle"),
        listRestore: $("#listRestore"),
        tableSource: $("#tableSource"),
        tableSearch: $("#tableSearch"),
        tableRowInfo: $("#tableRowInfo"),
        flatTable: $("#flatTable"),
        columnsButton: $("#columnsButton"),
        columnsMenu: $("#columnsMenu"),
        cardSearch: $("#cardSearch"),
        cardInfo: $("#cardInfo"),
        cardGrid: $("#cardGrid"),
        jsonOutput: $("#jsonOutput"),
        exportButton: $("#exportButton"),
        exportMenu: $("#exportMenu")
    });

    $$(".vtab").forEach(tab =>
        tab.addEventListener("click", () => activateView(tab.dataset.view)));

    setListCollapsed(prefs.listCollapsed);
    dom.listToggle.addEventListener("click", () => setListCollapsed(true));
    dom.listRestore.addEventListener("click", () => setListCollapsed(false));

    dom.recordSearch.addEventListener("input", () => {
        state.recordFilter = dom.recordSearch.value.trim().toLowerCase();
        renderRecordList();
    });

    dom.recordList.addEventListener("click", event => {
        const item = event.target.closest(".record-item");
        if (item) selectRecord(item.dataset.key);
    });

    dom.detailPane.addEventListener("click", event => {
        const nav = event.target.closest("[data-nav]");
        if (nav) return stepRecord(nav.dataset.nav === "next" ? 1 : -1);

        const toggle = event.target.closest("[data-toggle-empty]");
        if (toggle) {
            state.hideEmpty = !state.hideEmpty;
            return renderDetailPane();
        }

        const csv = event.target.closest("[data-csv]");
        if (csv) exportDataset(csv.dataset.csv);
    });

    dom.tableSource.addEventListener("change", () => {
        state.table.sourceId = dom.tableSource.value;
        state.table.sort = null;
        renderTable();
    });
    dom.tableSearch.addEventListener("input", () => {
        state.table.search = dom.tableSearch.value.trim().toLowerCase();
        renderTable();
    });
    dom.flatTable.addEventListener("click", event => {
        const head = event.target.closest("thead th");
        if (head) return sortBy(head.dataset.code);
        const row = event.target.closest("tbody tr.clickable");
        if (row) {
            selectRecord(row.dataset.key);
            activateView("masterDetail");
        }
    });
    dom.columnsButton.addEventListener("click", () => {
        dom.columnsMenu.classList.toggle("hidden");
        if (!dom.columnsMenu.classList.contains("hidden"))
            closeOnOutsideClick(dom.columnsMenu, dom.columnsButton);
    });

    dom.cardSearch.addEventListener("input", () => {
        state.cardFilter = dom.cardSearch.value.trim().toLowerCase();
        renderCards();
    });
    dom.cardGrid.addEventListener("click", event => {
        const card = event.target.closest(".rec-card");
        if (!card) return;
        selectRecord(card.dataset.key);
        activateView("masterDetail");
    });

    $$("#jsonMode .seg").forEach(button => button.addEventListener("click", () => {
        $$("#jsonMode .seg").forEach(item => item.classList.toggle("active", item === button));
        renderJson(button.dataset.json);
    }));
    $("#copyJson").addEventListener("click", async () => {
        await navigator.clipboard.writeText(dom.jsonOutput.textContent);
        toast("已複製 JSON", "ok");
    });

    dom.exportButton.addEventListener("click", () => {
        dom.exportMenu.classList.toggle("hidden");
        if (!dom.exportMenu.classList.contains("hidden"))
            closeOnOutsideClick(dom.exportMenu, dom.exportButton);
    });
    dom.exportMenu.addEventListener("click", event => {
        const button = event.target.closest("button");
        if (!button) return;
        dom.exportMenu.classList.add("hidden");
        if (button.dataset.csv) return exportDataset(button.dataset.csv);
        if (button.dataset.json === "raw") return downloadJson("kintone-raw", state.result?.raw ?? "{}");
        if (button.dataset.json === "shaped") return downloadJson("kintone-shaped", shapedPayload());
    });
}

/** 收起紀錄清單，讓右邊的單頭欄位與明細表格更寬。 */
function setListCollapsed(collapsed) {
    prefs.listCollapsed = collapsed;
    dom.mdSplit.classList.toggle("list-collapsed", collapsed);
    dom.listRestore.classList.toggle("hidden", !collapsed);
}

/* ── 狀態列 ─────────────────────────────────────────────────────── */

export function setStatus(text, kind = "idle") {
    dom.metaStatus.textContent = text;
    dom.metaStatus.className = kind;
}

export function setBusy() {
    setStatus("查詢中…", "busy");
    dom.metaCount.textContent = EMPTY;
    dom.metaTotal.textContent = EMPTY;
    dom.metaDetails.textContent = EMPTY;
    dom.metaElapsed.textContent = EMPTY;
}

export function showError(message) {
    setStatus("失敗", "err");
    dom.detailPane.innerHTML = emptyState("查詢沒有成功", message, true);
    dom.recordList.innerHTML = "";
    dom.recordCountBadge.textContent = "0";
    dom.exportButton.disabled = true;
}

/* ── 主要進入點 ─────────────────────────────────────────────────── */

export function setResult(result) {
    state.result = result;
    state.datasets = buildDatasets(result);
    state.selectedKey = result.header?.rows?.[0]?.key ?? null;
    state.recordFilter = "";
    state.cardFilter = "";
    state.table = { sourceId: "header", search: "", sort: null, hidden: new Map() };
    dom.recordSearch.value = "";
    dom.tableSearch.value = "";
    dom.cardSearch.value = "";

    const detailRows = (result.details ?? []).reduce((sum, table) => sum + table.rowCount, 0);
    setStatus(result.recordCount > 0 ? "成功" : "查無資料", result.recordCount > 0 ? "ok" : "idle");
    dom.metaCount.textContent = formatCount(result.recordCount);
    dom.metaTotal.textContent = formatCount(result.totalCount);
    dom.metaDetails.textContent = (result.details ?? []).length
        ? `${result.details.length} 張 / ${formatCount(detailRows)} 列`
        : "無";
    dom.metaElapsed.textContent = `${formatCount(result.elapsedMs)} ms`;
    dom.metaQuery.textContent = result.query?.effective || "（無條件）";
    dom.metaQuery.title = result.query?.effective || "（無條件）";
    dom.exportButton.disabled = false;

    renderSourceOptions();
    renderExportMenu();
    renderRecordList();
    renderDetailPane();
    renderTable();
    renderCards();
    renderJson($("#jsonMode .seg.active")?.dataset.json ?? "raw");

    if (result.fieldsWarning) toast(result.fieldsWarning, "warn", 4200);
}

/** 依網址 hash 指定要呈現的檢視與總表資料來源（供分享連結與截圖使用）。 */
export function applyPresentation({ view, tableSource } = {}) {
    if (tableSource && state.datasets.some(dataset => dataset.id === tableSource)) {
        state.table.sourceId = tableSource;
        state.table.sort = null;
        dom.tableSource.value = tableSource;
        renderTable();
    }
    if (view) activateView(view);
}

export function activateView(name) {
    state.view = name;
    $$(".vtab").forEach(tab => tab.classList.toggle("active", tab.dataset.view === name));
    $$(".view").forEach(view => view.classList.toggle("active", view.dataset.view === name));
}

/* ── 單頭單身 ───────────────────────────────────────────────────── */

function headerRows() {
    return state.result?.header?.rows ?? [];
}

function visibleRecords() {
    const keyword = state.recordFilter;
    if (!keyword) return headerRows();
    return headerRows().filter(row =>
        row.key.toLowerCase().includes(keyword) ||
        Object.values(row.values).some(value => (value ?? "").toLowerCase().includes(keyword)));
}

/** 找一個適合當標題的欄位值（第一個有值的非識別欄位）。 */
function primaryValue(row) {
    const columns = state.result?.header?.columns ?? [];
    for (const column of columns) {
        if (column.type === "__ID__" || column.type === "RECORD_NUMBER") continue;
        const value = row.values[column.code];
        if (value) return { label: column.label, value };
    }
    return null;
}

function renderRecordList() {
    const rows = visibleRecords();
    dom.recordCountBadge.textContent = formatCount(rows.length);

    if (!rows.length) {
        dom.recordList.innerHTML = `<li class="empty-chips" style="padding:14px">沒有符合的紀錄</li>`;
        return;
    }

    dom.recordList.innerHTML = rows.map(row => {
        const primary = primaryValue(row);
        const detailTotal = Object.values(row.detailCounts ?? {}).reduce((sum, value) => sum + value, 0);
        return `
        <li class="record-item${row.key === state.selectedKey ? " active" : ""}" data-key="${escapeHtml(row.key)}">
            <div class="ri-top">
                <span class="ri-key">#${escapeHtml(row.key)}</span>
                ${detailTotal ? `<span class="ri-count">明細 ${detailTotal}</span>` : ""}
            </div>
            <div class="ri-sub">${escapeHtml(primary ? primary.value : EMPTY)}</div>
            ${primary ? `<div class="ri-meta">${escapeHtml(primary.label)}</div>` : ""}
        </li>`;
    }).join("");
}

export function selectRecord(key) {
    state.selectedKey = key;
    renderRecordList();
    renderDetailPane();
    const active = dom.recordList.querySelector(".record-item.active");
    active?.scrollIntoView({ block: "nearest" });
}

function stepRecord(delta) {
    const rows = visibleRecords();
    const index = rows.findIndex(row => row.key === state.selectedKey);
    const next = rows[Math.min(Math.max(index + delta, 0), rows.length - 1)];
    if (next) selectRecord(next.key);
}

function renderDetailPane() {
    const rows = headerRows();
    const row = rows.find(item => item.key === state.selectedKey);

    if (!row) {
        dom.detailPane.innerHTML = state.result
            ? emptyState("沒有可顯示的紀錄", "換個查詢條件或放寬 limit 再試一次。")
            : emptyState("還沒有資料", "設定好連線與查詢條件後按「執行查詢」。");
        return;
    }

    const position = rows.indexOf(row) + 1;
    const primary = primaryValue(row);
    const columns = state.result.header.columns
        .filter(column => !state.hideEmpty || row.values[column.code]);

    const headerPanel = `
    <div class="panel-block">
        <div class="panel-title">
            <div class="panel-title-left">
                <span class="tag-head">單頭</span>
                <span>紀錄欄位</span>
                <span class="sub">${columns.length} 個欄位</span>
            </div>
            <button class="mini-button" data-csv="header">匯出單頭 CSV</button>
        </div>
        <dl class="kv-grid">
            ${columns.map(column => {
                const value = row.values[column.code];
                return `<div class="kv">
                    <dt>${escapeHtml(column.label)}</dt>
                    <dd class="${value ? "" : "empty"}">${escapeHtml(display(value))}</dd>
                    ${column.label === column.code ? "" : `<span class="code">${escapeHtml(column.code)}</span>`}
                </div>`;
            }).join("")}
        </dl>
    </div>`;

    const detailPanels = (state.result.details ?? []).map(table => {
        const lines = table.rows.filter(line => line.parentKey === row.key);
        return `
        <div class="panel-block">
            <div class="panel-title">
                <div class="panel-title-left">
                    <span class="tag-body">單身</span>
                    <span>${escapeHtml(table.label)}</span>
                    <span class="sub">${lines.length} 列 · ${escapeHtml(table.code)}</span>
                </div>
                <button class="mini-button" data-csv="detail:${escapeHtml(table.code)}">匯出全部 CSV</button>
            </div>
            ${lines.length ? `
            <div class="table-wrap" style="max-height:340px">
                <table class="grid">
                    <thead><tr>
                        <th style="width:52px">#</th>
                        ${table.columns.map(column => `<th>${escapeHtml(column.label)}</th>`).join("")}
                    </tr></thead>
                    <tbody>
                        ${lines.map(line => `<tr>
                            <td class="num">${line.index + 1}</td>
                            ${table.columns.map(column => formatCell(line.values[column.code])).join("")}
                        </tr>`).join("")}
                    </tbody>
                </table>
            </div>` : `<p class="hint" style="padding:14px">這筆紀錄的子表格沒有資料。</p>`}
        </div>`;
    }).join("");

    dom.detailPane.innerHTML = `
    <div class="detail-head">
        <h3>
            <span class="key-tag">#${escapeHtml(row.key)}</span>
            <span>${escapeHtml(primary ? primary.value : "（無標題欄位）")}</span>
        </h3>
        <div class="detail-nav">
            <button class="mini-button" data-nav="prev" ${position <= 1 ? "disabled" : ""}>‹ 上一筆</button>
            <span class="badge">${position} / ${rows.length}</span>
            <button class="mini-button" data-nav="next" ${position >= rows.length ? "disabled" : ""}>下一筆 ›</button>
            <button class="mini-button" data-toggle-empty>${state.hideEmpty ? "顯示空欄位" : "隱藏空欄位"}</button>
        </div>
    </div>
    ${headerPanel}
    ${detailPanels || `<p class="hint">這個 App 沒有子表格欄位，單身區塊會是空的。</p>`}`;
}

/* ── 總表 ───────────────────────────────────────────────────────── */

function renderSourceOptions() {
    dom.tableSource.innerHTML = state.datasets
        .map(dataset => `<option value="${escapeHtml(dataset.id)}">${escapeHtml(dataset.label)}</option>`)
        .join("");
    dom.tableSource.value = state.table.sourceId;
}

function currentDataset() {
    return findDataset(state.datasets, state.table.sourceId);
}

function hiddenSet(datasetId) {
    if (!state.table.hidden.has(datasetId)) state.table.hidden.set(datasetId, new Set());
    return state.table.hidden.get(datasetId);
}

function sortBy(code) {
    const sort = state.table.sort;
    if (!sort || sort.code !== code) state.table.sort = { code, dir: "asc" };
    else if (sort.dir === "asc") state.table.sort = { code, dir: "desc" };
    else state.table.sort = null;
    renderTable();
}

function renderTable() {
    const dataset = currentDataset();
    if (!dataset) {
        dom.flatTable.innerHTML = "";
        dom.tableRowInfo.textContent = "0 列";
        return;
    }

    const hidden = hiddenSet(dataset.id);
    const columns = dataset.columns.filter(column => !hidden.has(column.code));
    const keyword = state.table.search;

    let rows = dataset.rows;
    if (keyword)
        rows = rows.filter(row =>
            Object.values(row.cells).some(value => (value ?? "").toLowerCase().includes(keyword)));

    const sort = state.table.sort;
    if (sort) {
        const direction = sort.dir === "asc" ? 1 : -1;
        rows = [...rows].sort((left, right) => {
            const a = left.cells[sort.code] ?? "";
            const b = right.cells[sort.code] ?? "";
            if (isNumeric(a) && isNumeric(b)) return (toNumber(a) - toNumber(b)) * direction;
            return a.localeCompare(b, "zh-Hant") * direction;
        });
    }

    const shown = rows.slice(0, MAX_TABLE_ROWS);
    dom.tableRowInfo.textContent = rows.length > shown.length
        ? `顯示 ${formatCount(shown.length)} / ${formatCount(rows.length)} 列`
        : `${formatCount(rows.length)} 列`;

    dom.flatTable.innerHTML = `
    <thead><tr>${columns.map(column => `
        <th data-code="${escapeHtml(column.code)}">
            ${escapeHtml(column.label)}${sort?.code === column.code ? `<span class="sort">${sort.dir === "asc" ? "▲" : "▼"}</span>` : ""}
            ${column.label === column.code ? "" : `<span class="code">${escapeHtml(column.code)}</span>`}
        </th>`).join("")}</tr></thead>
    <tbody>${shown.map(row => `
        <tr class="clickable" data-key="${escapeHtml(row.key)}">
            ${columns.map(column => formatCell(row.cells[column.code], column)).join("")}
        </tr>`).join("")}</tbody>`;

    renderColumnsMenu(dataset, hidden);
}

function renderColumnsMenu(dataset, hidden) {
    dom.columnsMenu.innerHTML = `
        <div class="menu-title">要顯示的欄位</div>
        ${dataset.columns.map(column => `
            <label>
                <input type="checkbox" data-code="${escapeHtml(column.code)}" ${hidden.has(column.code) ? "" : "checked"}>
                <span>${escapeHtml(column.label)}</span>
            </label>`).join("")}`;

    dom.columnsMenu.querySelectorAll("input").forEach(input =>
        input.addEventListener("change", () => {
            if (input.checked) hidden.delete(input.dataset.code);
            else hidden.add(input.dataset.code);
            renderTable();
            dom.columnsMenu.classList.remove("hidden");
        }));
}

function formatCell(value, column) {
    if (column?.code === PARENT_KEY) return `<td class="key-cell">#${escapeHtml(display(value))}</td>`;
    if (value === null || value === undefined || value === "")
        return `<td class="empty-cell">${EMPTY}</td>`;
    const numeric = isNumeric(value) || column?.code === ROW_NO;
    return `<td class="${numeric ? "num" : ""}" title="${escapeHtml(value)}">${escapeHtml(value)}</td>`;
}

/* ── 卡片 ───────────────────────────────────────────────────────── */

function renderCards() {
    const columns = (state.result?.header?.columns ?? [])
        .filter(column => column.type !== "__ID__" && column.type !== "RECORD_NUMBER");
    const keyword = state.cardFilter;
    let rows = headerRows();
    if (keyword)
        rows = rows.filter(row =>
            row.key.toLowerCase().includes(keyword) ||
            Object.values(row.values).some(value => (value ?? "").toLowerCase().includes(keyword)));

    dom.cardInfo.textContent = `${formatCount(rows.length)} 筆`;

    if (!rows.length) {
        dom.cardGrid.innerHTML = `<p class="hint">沒有符合的紀錄。</p>`;
        return;
    }

    dom.cardGrid.innerHTML = rows.slice(0, MAX_CARDS).map(row => {
        const filled = columns
            .filter(column => row.values[column.code])
            .slice(0, 6);
        const badges = Object.entries(row.detailCounts ?? {})
            .map(([code, count]) => {
                const table = state.result.details.find(item => item.code === code);
                return `<span class="badge${count ? " accent" : ""}">${escapeHtml(table?.label ?? code)} ${count}</span>`;
            }).join("");

        return `
        <article class="rec-card" data-key="${escapeHtml(row.key)}">
            <div class="rc-head">
                <span class="rc-key">#${escapeHtml(row.key)}</span>
                <span class="badge">${row.index + 1}</span>
            </div>
            <dl>
                ${filled.map(column => `
                    <div class="rc-row">
                        <dt title="${escapeHtml(column.label)}">${escapeHtml(column.label)}</dt>
                        <dd title="${escapeHtml(row.values[column.code])}">${escapeHtml(row.values[column.code])}</dd>
                    </div>`).join("") || `<div class="rc-row"><dt>—</dt><dd>此筆沒有可顯示的欄位</dd></div>`}
            </dl>
            ${badges ? `<div class="rc-foot">${badges}</div>` : ""}
        </article>`;
    }).join("");
}

/* ── JSON ───────────────────────────────────────────────────────── */

function shapedPayload() {
    if (!state.result) return {};
    const { raw, ...rest } = state.result;
    return rest;
}

function renderJson(mode) {
    if (!state.result) return;
    if (mode === "shaped") {
        dom.jsonOutput.textContent = JSON.stringify(shapedPayload(), null, 2);
        return;
    }
    const raw = state.result.raw;
    dom.jsonOutput.textContent = raw
        ? JSON.stringify(JSON.parse(raw), null, 2)
        : "（本次查詢未附帶原始 JSON）";
}

/* ── 匯出 ───────────────────────────────────────────────────────── */

function renderExportMenu() {
    dom.exportMenu.innerHTML = `
        <div class="menu-title">CSV</div>
        ${state.datasets.map(dataset =>
            `<button type="button" data-csv="${escapeHtml(dataset.id)}">${escapeHtml(dataset.label)}</button>`).join("")}
        <div class="menu-title">JSON</div>
        <button type="button" data-json="raw">Kintone 原始 JSON</button>
        <button type="button" data-json="shaped">整理後（單頭單身）</button>`;
}

function exportDataset(id) {
    const dataset = findDataset(state.datasets, id);
    if (!dataset) return;
    downloadCsv(dataset);
    toast(`已下載「${dataset.label}」CSV`, "ok");
}

/* ── 共用片段 ───────────────────────────────────────────────────── */

function emptyState(title, message, isError = false) {
    return `
    <div class="empty-state">
        <svg viewBox="0 0 120 90" class="empty-art" aria-hidden="true">
            <rect x="8" y="10" width="60" height="34" rx="6"${isError ? ' fill="currentColor"' : ""}></rect>
            <rect x="8" y="52" width="104" height="10" rx="4" class="dim"></rect>
            <rect x="8" y="66" width="104" height="10" rx="4" class="dim"></rect>
            <rect x="76" y="10" width="36" height="34" rx="6" class="dim"></rect>
        </svg>
        <h3>${escapeHtml(title)}</h3>
        <p>${escapeHtml(message)}</p>
    </div>`;
}
