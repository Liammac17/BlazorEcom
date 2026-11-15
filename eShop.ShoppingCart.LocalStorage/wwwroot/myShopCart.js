const storageKey = "MyShop.ShoppingCart";
const legacyKey = "eShop.ShoppingCart";
const maximumQuantity = 99;
const maximumLines = 100;

function fingerprint(document) {
    return document.items.map(item => `${item.productId}:${item.quantity}`).join(",");
}

function normalizeItems(source) {
    const grouped = new Map();
    for (const item of Array.isArray(source) ? source.slice(0, 1000) : []) {
        if (!item || typeof item !== "object") continue;
        const id = item.productId ?? item.ProductId;
        const quantity = item.quantity ?? item.Quantity;
        if (!Number.isSafeInteger(id) || id <= 0 || id > 2147483647
            || !Number.isSafeInteger(quantity) || quantity <= 0) continue;
        grouped.set(id, Math.min(maximumQuantity, (grouped.get(id) ?? 0) + Math.min(quantity, maximumQuantity)));
    }
    return [...grouped].sort((a, b) => a[0] - b[0]).slice(0, maximumLines)
        .map(([productId, quantity]) => ({ productId, quantity }));
}

function readDocument() {
    let text = localStorage.getItem(storageKey);
    let original = null;
    if (!text) text = localStorage.getItem(legacyKey);
    if (text && text.length <= 131072) {
        try { original = JSON.parse(text); } catch { /* Recover from damaged persisted data. */ }
    }
    const modern = original && original.version === 1;
    const document = {
        version: 1,
        revision: modern && Number.isSafeInteger(original.revision) && original.revision >= 0
            ? original.revision : 0,
        checkoutToken: modern && typeof original.checkoutToken === "string"
            && original.checkoutToken.length <= 8192 ? original.checkoutToken : null,
        items: normalizeItems(modern ? original.items : original?.LineItems ?? original?.lineItems)
    };
    // Rewriting the allow-listed document also removes old customer/address data.
    localStorage.setItem(storageKey, JSON.stringify(document));
    localStorage.removeItem(legacyKey);
    return document;
}

function change(document) {
    document.revision = document.revision >= 2147483646 ? 0 : document.revision + 1;
    document.checkoutToken = null;
}

function operateSynchronous(operation, payload) {
    const document = readDocument();
    if (operation === "token") {
        if (document.revision === payload.revision && fingerprint(document) === payload.fingerprint)
            document.checkoutToken = payload.checkoutToken;
    } else if (operation === "add") {
        const previous = document.items.find(item => item.productId === payload.productId);
        if (previous) {
            if (previous.quantity >= maximumQuantity) throw new Error("A product is limited to 99 units.");
            previous.quantity++;
        } else {
            if (document.items.length >= maximumLines) throw new Error("A basket is limited to 100 products.");
            document.items.push({ productId: payload.productId, quantity: 1 });
        }
        change(document);
    } else if (operation === "remove" || operation === "quantity") {
        const previous = fingerprint(document);
        if (operation === "remove" || payload.quantity === 0)
            document.items = document.items.filter(item => item.productId !== payload.productId);
        else {
            const line = document.items.find(item => item.productId === payload.productId);
            if (line) line.quantity = payload.quantity;
        }
        if (previous !== fingerprint(document)) change(document);
    } else if (operation === "prune") {
        const unavailable = new Set(payload.productIds);
        document.items = document.items.filter(item => !unavailable.has(item.productId));
        change(document);
    } else if (operation === "replace") {
        const items = normalizeItems(payload.items);
        if (JSON.stringify(items) !== JSON.stringify(document.items)) {
            document.items = items;
            change(document);
        }
    } else if (operation === "clear") {
        // A completed checkout must not erase a newer basket created in another tab.
        if (document.checkoutToken === payload.checkoutToken) {
            document.items = [];
            change(document);
        }
    } else if (operation !== "read") {
        throw new Error("Unsupported basket operation.");
    }
    document.items = normalizeItems(document.items);
    localStorage.setItem(storageKey, JSON.stringify(document));
    return document;
}

export async function operate(operation, payload = null) {
    // Web Locks serializes tabs as well as circuits. On older browsers the operation
    // still executes synchronously, with the server also serializing its own circuit.
    if (navigator.locks?.request)
        return await navigator.locks.request(storageKey, () => operateSynchronous(operation, payload));
    return operateSynchronous(operation, payload);
}
