// 瀏覽器端的偏好設定與已存連線。
// Token 預設只放在 sessionStorage（關掉分頁就消失），使用者勾選後才落地到 localStorage。

const KEYS = {
    theme: "kc.theme",
    accent: "kc.accent",
    panel: "kc.panelCollapsed",
    list: "kc.listCollapsed",
    form: "kc.form",
    connections: "kc.connections",
    gatewayKey: "kc.gatewayKey",
    token: "kc.token"
};

function readJson(storage, key, fallback) {
    try {
        const raw = storage.getItem(key);
        return raw ? JSON.parse(raw) : fallback;
    } catch {
        return fallback;
    }
}

export const prefs = {
    get theme() { return localStorage.getItem(KEYS.theme) || "dark"; },
    set theme(value) { localStorage.setItem(KEYS.theme, value); },
    get accent() { return localStorage.getItem(KEYS.accent) || "cyan"; },
    set accent(value) { localStorage.setItem(KEYS.accent, value); },

    get panelCollapsed() { return localStorage.getItem(KEYS.panel) === "1"; },
    set panelCollapsed(value) { localStorage.setItem(KEYS.panel, value ? "1" : "0"); },

    get listCollapsed() { return localStorage.getItem(KEYS.list) === "1"; },
    set listCollapsed(value) { localStorage.setItem(KEYS.list, value ? "1" : "0"); }
};

export const form = {
    load() { return readJson(localStorage, KEYS.form, {}); },
    save(values) { localStorage.setItem(KEYS.form, JSON.stringify(values)); }
};

export const secrets = {
    get gatewayKey() { return sessionStorage.getItem(KEYS.gatewayKey) || ""; },
    set gatewayKey(value) { sessionStorage.setItem(KEYS.gatewayKey, value); },

    /** 記住 Token 時寫入 localStorage，否則只留在本分頁。 */
    getToken() {
        return localStorage.getItem(KEYS.token) ?? sessionStorage.getItem(KEYS.token) ?? "";
    },
    setToken(value, remember) {
        sessionStorage.setItem(KEYS.token, value);
        if (remember) localStorage.setItem(KEYS.token, value);
        else localStorage.removeItem(KEYS.token);
    },
    get tokenRemembered() { return localStorage.getItem(KEYS.token) !== null; }
};

export const connections = {
    list() { return readJson(localStorage, KEYS.connections, []); },

    /** 以「連線名稱」為主鍵：同名覆蓋，不同名就是新的一組。 */
    save(entry) {
        const all = connections.list();
        const index = all.findIndex(item => item.name === entry.name);
        if (index >= 0) all[index] = entry; else all.push(entry);
        localStorage.setItem(KEYS.connections, JSON.stringify(all));
        return all;
    },

    find(name) {
        return connections.list().find(item => item.name === name) ?? null;
    },

    remove(name) {
        const all = connections.list().filter(item => item.name !== name);
        localStorage.setItem(KEYS.connections, JSON.stringify(all));
        return all;
    }
};
