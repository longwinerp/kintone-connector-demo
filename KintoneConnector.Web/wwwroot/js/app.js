// 主控制器：連線設定 → 查詢 → 交給 views.js 呈現。

import { $, $$, escapeHtml, toast } from "./dom.js";
import { prefs, form, secrets, connections } from "./store.js";
import { api, useGatewayKey } from "./api.js";
import { initViews, setResult, setBusy, showError, setStatus, activateView, applyPresentation } from "./views.js";

/**
 * 網址 hash 可以指定畫面呈現方式，例如：
 *   #view=table&table=join:items&theme=light&accent=violet&panel=collapsed
 * 方便把「某個檢視的樣子」直接分享給別人，也用於自動化截圖。
 */
function readHashOptions() {
    const params = new URLSearchParams((location.hash || "").replace(/^#/, ""));
    return {
        view: params.get("view"),
        table: params.get("table"),
        theme: params.get("theme"),
        accent: params.get("accent"),
        panel: params.get("panel")
    };
}

const hashOptions = readHashOptions();

/** 只在示範模式用來判斷「這台瀏覽器有沒有查過」，用來決定要不要自動跑一次。 */
const RAN_KEY = "kc.everRan";

const el = {};
const state = {
    source: "profile",
    profiles: [],
    fields: [],
    selectedFields: new Set(),
    activeConnection: "",
    autoRan: false
};

document.addEventListener("DOMContentLoaded", () => {
    cacheElements();
    initViews();
    useGatewayKey(() => el.gatewayKey.value);
    applyPreferences();
    bindEvents();
    restoreForm();
    loadProfiles();
    renderSavedConnections();
});

function cacheElements() {
    Object.assign(el, {
        themeToggle: $("#themeToggle"),
        themeLabel: $("#themeLabel"),
        layout: $("#layout"),
        panelToggle: $("#panelToggle"),
        panelToggleIcon: $("#panelToggleIcon"),
        panelToggleLabel: $("#panelToggleLabel"),
        panelRestore: $("#panelRestore"),
        targetPill: $("#targetPill"),
        targetText: $("#targetText"),

        profile: $("#profile"),
        profileMeta: $("#profileMeta"),
        reloadProfiles: $("#reloadProfiles"),
        connectionName: $("#connectionName"),
        baseUrl: $("#baseUrl"),
        appId: $("#appId"),
        apiPath: $("#apiPath"),
        apiToken: $("#apiToken"),
        toggleToken: $("#toggleToken"),
        rememberToken: $("#rememberToken"),
        saveConnection: $("#saveConnection"),
        savedList: $("#savedList"),
        gatewayKey: $("#gatewayKey"),
        toggleKey: $("#toggleKey"),
        testButton: $("#testButton"),
        connectionStatus: $("#connectionStatus"),

        query: $("#query"),
        fields: $("#fields"),
        fieldChips: $("#fieldChips"),
        toggleFieldPicker: $("#toggleFieldPicker"),
        clearFields: $("#clearFields"),
        limit: $("#limit"),
        offset: $("#offset"),
        prevPage: $("#prevPage"),
        nextPage: $("#nextPage"),
        totalCount: $("#totalCount"),
        withLabels: $("#withLabels"),
        runButton: $("#runButton"),
        runHint: $("#runHint")
    });
}

/* ── 外觀 ───────────────────────────────────────────────────────── */

function applyPreferences() {
    setTheme(hashOptions.theme === "light" || hashOptions.theme === "dark" ? hashOptions.theme : prefs.theme);
    setAccent(hashOptions.accent ?? prefs.accent);
    setPanelCollapsed(hashOptions.panel ? hashOptions.panel === "collapsed" : prefs.panelCollapsed);
}

/** 收起左側工作台，把整個寬度讓給結果區。 */
function setPanelCollapsed(collapsed) {
    prefs.panelCollapsed = collapsed;
    el.layout.classList.toggle("panel-collapsed", collapsed);
    el.panelRestore.classList.toggle("hidden", !collapsed);
    el.panelToggleIcon.textContent = collapsed ? "⟩" : "⟨";
    el.panelToggleLabel.textContent = collapsed ? "展開面板" : "收合面板";
}

function setTheme(theme) {
    document.documentElement.dataset.theme = theme;
    prefs.theme = theme;
    el.themeLabel.textContent = theme === "dark" ? "深色" : "淺色";
}

function setAccent(accent) {
    document.documentElement.dataset.accent = accent;
    prefs.accent = accent;
    $$(".swatch").forEach(swatch =>
        swatch.setAttribute("aria-pressed", String(swatch.dataset.accent === accent)));
}

/* ── 事件 ───────────────────────────────────────────────────────── */

function bindEvents() {
    el.themeToggle.addEventListener("click", () =>
        setTheme(document.documentElement.dataset.theme === "dark" ? "light" : "dark"));
    el.panelToggle.addEventListener("click", () =>
        setPanelCollapsed(!el.layout.classList.contains("panel-collapsed")));
    el.panelRestore.addEventListener("click", () => setPanelCollapsed(false));
    $$(".swatch").forEach(swatch =>
        swatch.addEventListener("click", () => setAccent(swatch.dataset.accent)));

    $$(".segmented .seg[data-source]").forEach(button =>
        button.addEventListener("click", () => switchSource(button.dataset.source)));

    el.reloadProfiles.addEventListener("click", loadProfiles);
    el.profile.addEventListener("change", () => { updateProfileMeta(); persistForm(); });

    el.toggleToken.addEventListener("click", () => toggleSecret(el.apiToken, el.toggleToken));
    el.toggleKey.addEventListener("click", () => toggleSecret(el.gatewayKey, el.toggleKey));
    el.gatewayKey.addEventListener("change", () => { secrets.gatewayKey = el.gatewayKey.value; });
    el.apiToken.addEventListener("change", () =>
        secrets.setToken(el.apiToken.value, el.rememberToken.checked));
    el.rememberToken.addEventListener("change", () =>
        secrets.setToken(el.apiToken.value, el.rememberToken.checked));

    el.saveConnection.addEventListener("click", saveCurrentConnection);
    el.savedList.addEventListener("click", event => {
        const remove = event.target.closest("[data-remove]");
        if (remove) {
            connections.remove(remove.dataset.remove);
            if (state.activeConnection === remove.dataset.remove) state.activeConnection = "";
            renderSavedConnections();
            return toast("已移除連線", "ok");
        }

        const test = event.target.closest("[data-test]");
        if (test) {
            applySavedConnection(test.dataset.test, { silent: true });
            return testConnection();
        }

        const item = event.target.closest("[data-name]");
        if (item) applySavedConnection(item.dataset.name);
    });

    el.testButton.addEventListener("click", testConnection);

    $$(".snippet-chips .chip").forEach(chip =>
        chip.addEventListener("click", () => applySnippet(chip.dataset.snippet)));

    el.toggleFieldPicker.addEventListener("click", () => {
        const hidden = el.fieldChips.classList.toggle("hidden");
        el.toggleFieldPicker.textContent = hidden ? "展開" : "收合";
    });
    el.clearFields.addEventListener("click", () => {
        state.selectedFields.clear();
        el.fields.value = "";
        syncFieldChips();
        persistForm();
    });
    el.fields.addEventListener("change", () => {
        state.selectedFields = new Set(splitFields(el.fields.value));
        syncFieldChips();
        persistForm();
    });

    el.prevPage.addEventListener("click", () => movePage(-1));
    el.nextPage.addEventListener("click", () => movePage(1));
    el.runButton.addEventListener("click", run);

    document.addEventListener("keydown", event => {
        if ((event.ctrlKey || event.metaKey) && event.key === "Enter") {
            event.preventDefault();
            run();
            return;
        }
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "b") {
            event.preventDefault();
            setPanelCollapsed(!el.layout.classList.contains("panel-collapsed"));
        }
    });

    [el.connectionName, el.baseUrl, el.appId, el.apiPath, el.query,
     el.limit, el.offset, el.totalCount, el.withLabels]
        .forEach(input => input.addEventListener("change", () => { persistForm(); updateTarget(); }));
}

function toggleSecret(input, button) {
    const show = input.type === "password";
    input.type = show ? "text" : "password";
    button.textContent = show ? "隱藏" : "顯示";
}

function switchSource(source) {
    state.source = source;
    $$(".segmented .seg[data-source]").forEach(button =>
        button.classList.toggle("active", button.dataset.source === source));
    $$(".source-pane").forEach(pane =>
        pane.classList.toggle("hidden", pane.dataset.pane !== source));
    persistForm();
    updateTarget();
}

/* ── 連線 ───────────────────────────────────────────────────────── */

async function loadProfiles() {
    el.profile.innerHTML = `<option value="">讀取中…</option>`;
    try {
        const data = await api.profiles();
        state.profiles = data.profiles ?? [];
        el.profile.innerHTML = state.profiles.length
            ? state.profiles.map(item =>
                `<option value="${escapeHtml(item.code)}">${escapeHtml(item.displayName)}</option>`).join("")
            : `<option value="">伺服器沒有設定 profile</option>`;

        const saved = form.load().profile;
        if (saved && state.profiles.some(item => item.code === saved)) el.profile.value = saved;

        if (!data.adHocEnabled) {
            $(`.seg[data-source="custom"]`).disabled = true;
            $(`.seg[data-source="custom"]`).title = "伺服器已關閉自訂連線";
        }
        updateProfileMeta();
        updateTarget();

        // Demo 版：第一次進站且選中示範 profile 時直接跑一次，畫面不要空著。
        const selected = state.profiles.find(item => item.code === el.profile.value);
        if (selected?.isDemo && !state.autoRan && !localStorage.getItem(RAN_KEY)) {
            state.autoRan = true;
            run();
        }
    } catch (error) {
        state.profiles = [];
        el.profile.innerHTML = `<option value="">無法讀取 profile</option>`;
        el.profileMeta.textContent = error.message;
        if (error.code === "UNAUTHORIZED" || error.code === "GATEWAY_KEY_NOT_CONFIGURED")
            toast("請先填入 Connector API Key", "warn");
    }
}

function updateProfileMeta() {
    const profile = state.profiles.find(item => item.code === el.profile.value);
    el.profileMeta.textContent = profile
        ? `${profile.baseUrl} · App ${profile.appId} · ${profile.configured ? "Token 已設定" : "Token 尚未設定"}`
        : "—";
    updateTarget();
}

function connectionPayload() {
    if (state.source === "profile") return { profile: el.profile.value };
    return {
        connection: {
            baseUrl: el.baseUrl.value.trim(),
            apiPath: el.apiPath.value.trim(),
            appId: Number(el.appId.value || 0),
            apiToken: el.apiToken.value.trim()
        }
    };
}

function updateTarget(label) {
    if (label) {
        el.targetText.textContent = label;
        el.targetPill.classList.add("ready");
        el.targetPill.classList.remove("error");
        return;
    }

    if (state.source === "profile") {
        const profile = state.profiles.find(item => item.code === el.profile.value);
        el.targetText.textContent = profile
            ? `${profile.displayName} · App ${profile.appId}`
            : "尚未選擇 Profile";
        el.targetPill.classList.toggle("ready", Boolean(profile));
        return;
    }

    const host = el.baseUrl.value.trim().replace(/^https?:\/\//, "").replace(/\/.*$/, "");
    const appId = el.appId.value.trim();
    const ready = Boolean(host && appId && el.apiToken.value.trim());
    el.targetText.textContent = host && appId ? `${host} · App ${appId}` : "尚未設定自訂連線";
    el.targetPill.classList.toggle("ready", ready);
}

async function testConnection() {
    el.testButton.disabled = true;
    el.connectionStatus.className = "hint status-line";
    el.connectionStatus.textContent = "連線中…";

    try {
        const data = await api.fields(connectionPayload());
        state.fields = data.fields ?? [];
        renderFieldChips();
        el.connectionStatus.className = "hint status-line ok";
        el.connectionStatus.textContent =
            `連線成功：${data.connection.label}，共 ${state.fields.length} 個欄位。`;
        updateTarget(`${data.connection.label}`);
        markReadyToRun();
        toast("連線成功，接著按左下角「執行查詢」取資料", "ok", 4600);
    } catch (error) {
        el.connectionStatus.className = "hint status-line err";
        el.connectionStatus.textContent = `${error.code}：${error.message}`;
        el.targetPill.classList.add("error");
        el.targetPill.classList.remove("ready");
        if (error.code === "FIELDS_UNAVAILABLE")
            toast("Token 可能沒有讀取欄位定義的權限，查詢仍可能正常。", "warn", 5000);
        else
            toast(error.message, "err", 5000);
    } finally {
        el.testButton.disabled = false;
    }
}

function hostOf(url) {
    return url.trim().replace(/^https?:\/\//, "").replace(/\/.*$/, "");
}

/** 測試連線只驗證連得到，真正取資料還是要按「執行查詢」，這裡把注意力導過去。 */
function markReadyToRun() {
    el.runHint.className = "hint ready";
    el.runHint.textContent = "連線就緒 —— 按這裡才會真的取資料。";
    el.runButton.classList.remove("pulse");
    void el.runButton.offsetWidth; // 重新觸發動畫
    el.runButton.classList.add("pulse");
    setTimeout(() => el.runButton.classList.remove("pulse"), 3600);
}

function saveCurrentConnection() {
    const baseUrl = el.baseUrl.value.trim();
    const appId = Number(el.appId.value || 0);
    if (!baseUrl || !appId) return toast("請先填好網址與 App ID", "warn");

    // 沒取名字就用「主機 · App」當名稱，同名視為更新、不同名就是新的一組。
    const name = el.connectionName.value.trim() || `${hostOf(baseUrl)} · ${appId}`;
    const remember = el.rememberToken.checked;

    connections.save({
        name,
        baseUrl,
        appId,
        apiPath: el.apiPath.value.trim(),
        token: remember ? el.apiToken.value.trim() : ""
    });

    el.connectionName.value = name;
    state.activeConnection = name;
    renderSavedConnections();
    persistForm();
    toast(remember ? `已存「${name}」（含 Token）` : `已存「${name}」（不含 Token）`, "ok");
}

function renderSavedConnections() {
    const list = connections.list();
    if (!list.length) {
        el.savedList.innerHTML =
            `<p class="hint">還沒有存任何連線。填好上面的欄位後按「＋ 存成一組」，可以存多組隨時切換。</p>`;
        return;
    }

    el.savedList.innerHTML = list.map(item => `
        <div class="conn-item${item.name === state.activeConnection ? " active" : ""}"
             data-name="${escapeHtml(item.name)}" title="點一下套用這組連線">
            <div>
                <div class="cn-name">
                    ${escapeHtml(item.name)}
                    ${item.token ? `<span class="key-mark">TOKEN</span>` : ""}
                </div>
                <div class="cn-meta">${escapeHtml(hostOf(item.baseUrl))} · App ${escapeHtml(item.appId)}</div>
            </div>
            <div class="cn-actions">
                <button type="button" class="icon-btn" data-test="${escapeHtml(item.name)}" title="套用並測試連線">測</button>
                <button type="button" class="icon-btn danger" data-remove="${escapeHtml(item.name)}" title="刪除">✕</button>
            </div>
        </div>`).join("");
}

function applySavedConnection(name, { silent = false } = {}) {
    const item = connections.find(name);
    if (!item) return;

    el.connectionName.value = item.name;
    el.baseUrl.value = item.baseUrl;
    el.appId.value = item.appId;
    el.apiPath.value = item.apiPath ?? "";
    if (item.token) {
        el.apiToken.value = item.token;
        el.rememberToken.checked = true;
        secrets.setToken(item.token, true);
    }

    state.activeConnection = item.name;
    state.fields = [];
    switchSource("custom");
    renderSavedConnections();
    persistForm();
    updateTarget();
    if (!silent) toast(`已套用「${item.name}」`, "ok");
}

/* ── 查詢條件 ───────────────────────────────────────────────────── */

function applySnippet(snippet) {
    if (!snippet) {
        el.query.value = "";
    } else {
        const current = el.query.value.trim();
        el.query.value = current ? `${current} ${snippet}` : snippet;
    }
    persistForm();
    el.query.focus();
}

function splitFields(value) {
    return value.split(",").map(item => item.trim()).filter(Boolean);
}

function renderFieldChips() {
    if (!state.fields.length) {
        el.fieldChips.className = "chips empty-chips";
        el.fieldChips.textContent = "這個 App 沒有可讀取的欄位定義";
        return;
    }

    el.fieldChips.className = "chips";
    el.fieldChips.classList.remove("hidden");
    el.toggleFieldPicker.textContent = "收合";
    el.fieldChips.innerHTML = state.fields.map(field => `
        <button type="button" class="chip" data-code="${escapeHtml(field.code)}"
                title="${escapeHtml(field.type)}${field.children?.length ? `（子表格 ${field.children.length} 欄）` : ""}">
            ${escapeHtml(field.label)}${field.children?.length ? " ⊞" : ""}
        </button>`).join("");

    el.fieldChips.querySelectorAll(".chip").forEach(chip =>
        chip.addEventListener("click", () => {
            const code = chip.dataset.code;
            if (state.selectedFields.has(code)) state.selectedFields.delete(code);
            else state.selectedFields.add(code);
            el.fields.value = [...state.selectedFields].join(", ");
            syncFieldChips();
            persistForm();
        }));

    syncFieldChips();
}

function syncFieldChips() {
    el.fieldChips.querySelectorAll(".chip").forEach(chip =>
        chip.classList.toggle("on", state.selectedFields.has(chip.dataset.code)));
}

function movePage(direction) {
    const limit = Number(el.limit.value || 100);
    const offset = Number(el.offset.value || 0);
    const next = Math.max(0, offset + direction * limit);
    el.offset.value = Math.min(next, 10000);
    persistForm();
    run();
}

/* ── 執行 ───────────────────────────────────────────────────────── */

async function run() {
    if (state.source === "custom" && !el.apiToken.value.trim())
        return toast("請先輸入 API Token", "warn");
    if (state.source === "profile" && !el.profile.value)
        return toast("請先選擇 Profile", "warn");

    persistForm();
    localStorage.setItem(RAN_KEY, "1");
    secrets.gatewayKey = el.gatewayKey.value;
    secrets.setToken(el.apiToken.value, el.rememberToken.checked);

    el.runButton.disabled = true;
    setBusy();

    const payload = {
        ...connectionPayload(),
        query: el.query.value.trim(),
        fields: splitFields(el.fields.value),
        totalCount: el.totalCount.checked,
        withLabels: el.withLabels.checked,
        includeRaw: true,
        limit: Number(el.limit.value || 100),
        offset: Number(el.offset.value || 0)
    };

    try {
        const result = await api.records(payload);
        setResult(result);
        activateView(hashOptions.view ?? "masterDetail");
        applyPresentation({ view: hashOptions.view, tableSource: hashOptions.table });
        updateTarget(`${result.connection.label}`);
        el.runHint.className = "hint ready";
        el.runHint.textContent = `上次取得 ${result.recordCount} 筆（位移 ${payload.offset}）。`;
        toast(`取得 ${result.recordCount} 筆紀錄`, "ok");
    } catch (error) {
        showError(`${error.code}：${error.message}`);
        setStatus(error.status ? `HTTP ${error.status}` : "失敗", "err");
        el.runHint.className = "hint";
        el.runHint.textContent = "查詢失敗，詳細訊息在右側。";
        toast(error.message, "err", 5200);
    } finally {
        el.runButton.disabled = false;
    }
}

/* ── 表單狀態保存 ───────────────────────────────────────────────── */

function persistForm() {
    form.save({
        source: state.source,
        profile: el.profile.value,
        connectionName: el.connectionName.value,
        activeConnection: state.activeConnection,
        baseUrl: el.baseUrl.value,
        appId: el.appId.value,
        apiPath: el.apiPath.value,
        query: el.query.value,
        fields: el.fields.value,
        limit: el.limit.value,
        offset: el.offset.value,
        totalCount: el.totalCount.checked,
        withLabels: el.withLabels.checked
    });
}

function restoreForm() {
    const saved = form.load();
    el.gatewayKey.value = secrets.gatewayKey;
    el.apiToken.value = secrets.getToken();
    el.rememberToken.checked = secrets.tokenRemembered;

    if (saved.connectionName) el.connectionName.value = saved.connectionName;
    if (saved.activeConnection) state.activeConnection = saved.activeConnection;
    if (saved.baseUrl) el.baseUrl.value = saved.baseUrl;
    if (saved.appId) el.appId.value = saved.appId;
    if (saved.apiPath) el.apiPath.value = saved.apiPath;
    if (saved.query) el.query.value = saved.query;
    if (saved.fields) {
        el.fields.value = saved.fields;
        state.selectedFields = new Set(splitFields(saved.fields));
    }
    if (saved.limit) el.limit.value = saved.limit;
    if (saved.offset) el.offset.value = saved.offset;
    if (saved.totalCount !== undefined) el.totalCount.checked = saved.totalCount;
    if (saved.withLabels !== undefined) el.withLabels.checked = saved.withLabels;

    switchSource(saved.source === "custom" ? "custom" : "profile");
}
