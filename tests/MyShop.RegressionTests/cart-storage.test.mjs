import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

const values = new Map();
globalThis.localStorage = {
    getItem: key => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, String(value)),
    removeItem: key => values.delete(key)
};
let lockQueue = Promise.resolve();
Object.defineProperty(globalThis, "navigator", { configurable: true, value: {
    locks: { request: (_key, action) => {
        const operation = lockQueue.then(action);
        lockQueue = operation.catch(() => {});
        return operation;
    } }
} });
const source = await readFile(new URL("../../eShop.ShoppingCart.LocalStorage/wwwroot/myShopCart.js", import.meta.url), "utf8");
const { operate } = await import(`data:text/javascript;base64,${Buffer.from(source).toString("base64")}`);
const key = "MyShop.ShoppingCart";
const legacy = "eShop.ShoppingCart";

values.set(legacy, "{");
assert.deepEqual((await operate("read")).items, []);
assert.equal(values.has(legacy), false);

values.delete(key);
values.set(legacy, JSON.stringify({ CustomerName: "Private Customer", CustomerAddress: "Private Address",
    UniqueId: "forged", DateProcessed: "2026-01-01", AdminUser: "forged-admin", LineItems: [
        { ProductId: 1, Quantity: 2, Price: 0.01 }, { ProductId: 1, Quantity: 3 },
        { ProductId: 2, Quantity: -1 }, { ProductId: 0, Quantity: 4 }, { ProductId: 3, Quantity: 999 }
    ] }));
let document = await operate("read");
assert.deepEqual(document.items, [{ productId: 1, quantity: 5 }, { productId: 3, quantity: 99 }]);
assert.equal(document.checkoutToken, null);
assert.equal(values.has(legacy), false);
assert.equal(values.get(key).includes("Private"), false);
assert.equal(values.get(key).includes("Price"), false);
assert.equal(values.get(key).includes("AdminUser"), false);

await operate("token", { revision: document.revision, fingerprint: "1:5,3:99", checkoutToken: "signed-by-server" });
assert.equal((await operate("read")).checkoutToken, "signed-by-server");
await operate("quantity", { productId: 1, quantity: 4 });
assert.equal((await operate("read")).checkoutToken, null);
await operate("clear", { checkoutToken: "signed-by-server" });
assert.equal((await operate("read")).items.length, 2);
await operate("prune", { productIds: [3] });
assert.deepEqual((await operate("read")).items, [{ productId: 1, quantity: 4 }]);

await operate("clear", { checkoutToken: null });
await Promise.all([operate("add", { productId: 10 }), operate("add", { productId: 20 })]);
assert.deepEqual((await operate("read")).items, [{ productId: 10, quantity: 1 }, { productId: 20, quantity: 1 }]);
await operate("quantity", { productId: 10, quantity: 99 });
await assert.rejects(() => operate("add", { productId: 10 }), /99 units/);
await operate("quantity", { productId: 20, quantity: 0 });
assert.equal((await operate("read")).items.length, 1);

values.set(key, "x".repeat(131073));
assert.deepEqual((await operate("read")).items, []);
values.set(key, JSON.stringify({ version: 1, items: null }));
assert.deepEqual((await operate("read")).items, []);

// Exercise the documented compatibility fallback when the Web Locks API is absent.
delete navigator.locks;
await operate("add", { productId: 42 });
assert.deepEqual((await operate("read")).items, [{ productId: 42, quantity: 1 }]);
console.log("PASS: actual browser-storage module regression checks (recovery, migration, privacy, tokens, locking, limits).");
