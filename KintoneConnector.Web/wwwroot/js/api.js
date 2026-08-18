// 與 Connector 後端溝通的薄薄一層。Connector API Key 一律放 Header。

let gatewayKeyProvider = () => "";

export function useGatewayKey(provider) {
    gatewayKeyProvider = provider;
}

function headers() {
    const result = { "Content-Type": "application/json" };
    const key = (gatewayKeyProvider() || "").trim();
    if (key) result["X-Connector-Api-Key"] = key;
    return result;
}

async function parse(response) {
    const text = await response.text();
    try {
        return JSON.parse(text);
    } catch {
        return {
            ok: false,
            error: "INVALID_RESPONSE",
            message: text || `伺服器回應無法解析（HTTP ${response.status}）。`
        };
    }
}

async function call(url, init) {
    let response;
    try {
        response = await fetch(url, init);
    } catch {
        const error = new Error("無法連線 Connector，請確認服務是否啟動。");
        error.code = "NETWORK_ERROR";
        throw error;
    }

    const data = await parse(response);
    if (!response.ok || data.ok === false) {
        const error = new Error(data.message || `HTTP ${response.status}`);
        error.code = data.error || `HTTP_${response.status}`;
        error.status = response.status;
        error.payload = data;
        throw error;
    }
    return data;
}

export const api = {
    profiles: () => call("/api/profiles", { headers: headers() }),

    fields: payload => call("/api/kintone/fields", {
        method: "POST",
        headers: headers(),
        body: JSON.stringify(payload)
    }),

    records: payload => call("/api/kintone/records", {
        method: "POST",
        headers: headers(),
        body: JSON.stringify(payload)
    })
};
