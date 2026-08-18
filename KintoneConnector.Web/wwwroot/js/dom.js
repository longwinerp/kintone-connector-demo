// 共用的小工具：DOM 查詢、字串處理、提示訊息。

export const $ = (selector, scope = document) => scope.querySelector(selector);
export const $$ = (selector, scope = document) => [...scope.querySelectorAll(selector)];

export function escapeHtml(value) {
    return String(value ?? "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#039;");
}

/** 空值統一顯示成破折號，避免表格出現大量空白格。 */
export const EMPTY = "—";

export function display(value) {
    return value === null || value === undefined || value === "" ? EMPTY : String(value);
}

const NUMERIC = /^-?\d{1,3}(,\d{3})*(\.\d+)?$|^-?\d+(\.\d+)?$/;

export function isNumeric(value) {
    return typeof value === "string" && value.length > 0 && NUMERIC.test(value.trim());
}

export function toNumber(value) {
    return Number(String(value).replaceAll(",", ""));
}

export function formatCount(value) {
    if (value === null || value === undefined || value === "") return EMPTY;
    const number = Number(value);
    return Number.isFinite(number) ? number.toLocaleString("zh-TW") : String(value);
}

/** 依滑鼠點擊以外的地方關閉浮動選單。 */
export function closeOnOutsideClick(menu, trigger) {
    const handler = event => {
        if (menu.contains(event.target) || trigger.contains(event.target)) return;
        menu.classList.add("hidden");
        document.removeEventListener("mousedown", handler);
    };
    document.addEventListener("mousedown", handler);
}

let toastHost = null;

export function toast(message, kind = "info", timeout = 3200) {
    toastHost ??= $("#toastHost");
    const node = document.createElement("div");
    node.className = `toast ${kind}`;
    node.textContent = message;
    toastHost.appendChild(node);
    setTimeout(() => {
        node.style.opacity = "0";
        node.style.transition = "opacity .25s";
        setTimeout(() => node.remove(), 260);
    }, timeout);
}

export function download(filename, content, mime) {
    const blob = new Blob([content], { type: `${mime};charset=utf-8` });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = filename;
    link.click();
    URL.revokeObjectURL(url);
}

export function timestamp() {
    const now = new Date();
    const pad = value => String(value).padStart(2, "0");
    return `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}-${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;
}
