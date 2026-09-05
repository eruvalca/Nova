const prefix = "nova:campaign-recovery:";
export function read(scope, name) {
    for (let i = sessionStorage.length - 1; i >= 0; i--) {
        const key = sessionStorage.key(i);
        if (key?.startsWith(prefix) && !key.startsWith(prefix + scope + ":")) sessionStorage.removeItem(key);
    }
    const raw = sessionStorage.getItem(prefix + scope + ":" + name);
    if (!raw) return null;
    try { return JSON.parse(raw); } catch { sessionStorage.removeItem(prefix + scope + ":" + name); return null; }
}
export function write(scope, name, value) { sessionStorage.setItem(prefix + scope + ":" + name, JSON.stringify(value)); }
export function remove(scope, name) { sessionStorage.removeItem(prefix + scope + ":" + name); }
export function acknowledgeOpeningReceipt(scope, campaignId, operationId) {
    const key = prefix + scope + ":receipt:" + campaignId;
    const raw = sessionStorage.getItem(key);
    if (!raw) return;
    let receipt;
    try { receipt = JSON.parse(raw); } catch { return; }
    if (receipt?.operationId === operationId) sessionStorage.removeItem(key);
}
export function clear(scope) {
    for (let i = sessionStorage.length - 1; i >= 0; i--) {
        const key = sessionStorage.key(i);
        if (key?.startsWith(prefix + scope + ":")) sessionStorage.removeItem(key);
    }
}
export function focus(element) { element?.focus(); }
