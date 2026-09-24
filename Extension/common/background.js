// sOC Credentials — fondo de la extensión (service worker en Chromium, página de eventos en Firefox).
// Todo pasa por aquí: el popup y los scripts de contenido piden, y esto habla con la aplicación del
// PC por mensajería nativa (host com.socratic.credentials). La bóveda nunca sale del PC.

const api = globalThis.browser ?? globalThis.chrome;
const HOST = "com.socratic.credentials";

// ------------------------------------------------------------------ conexión con la aplicación

let port = null;
let nextId = 1;
const pending = new Map();      // id → {resolve, reject, timer}

function connect() {
  if (port) return port;
  try {
    port = api.runtime.connectNative(HOST);
  } catch (e) {
    port = null;
    throw new Error("nohost");
  }
  port.onMessage.addListener((msg) => {
    const p = pending.get(msg.id);
    if (!p) return;
    pending.delete(msg.id);
    clearTimeout(p.timer);
    p.resolve(msg);
  });
  port.onDisconnect.addListener(() => {
    const err = api.runtime.lastError?.message ?? port?.error?.message ?? "";
    port = null;
    const reason = /not found|no encontr|specified native messaging host/i.test(err) ? "nohost" : "disconnected";
    for (const [id, p] of pending) { clearTimeout(p.timer); p.reject(new Error(reason)); }
    pending.clear();
  });
  return port;
}

/** Envía una petición a la aplicación y espera su respuesta (con tiempo límite). */
function ask(message, timeoutMs = 20000) {
  return new Promise((resolve, reject) => {
    let p;
    try { p = connect(); } catch (e) { reject(e); return; }
    const id = nextId++;
    const timer = setTimeout(() => { pending.delete(id); reject(new Error("timeout")); }, timeoutMs);
    pending.set(id, { resolve, reject, timer });
    try { p.postMessage({ id, ...message }); }
    catch (e) { pending.delete(id); clearTimeout(timer); port = null; reject(new Error("disconnected")); }
  });
}

function browserName() {
  const ua = navigator.userAgent;
  if (/Firefox\//.test(ua)) return "firefox";
  if (/Edg\//.test(ua)) return "edge";
  return "chrome";
}

function hostOf(url) {
  try { const u = new URL(url); return (u.protocol === "http:" || u.protocol === "https:") ? u.hostname : ""; }
  catch { return ""; }
}

// Al arrancar el navegador se saluda a la aplicación: así sabe que la extensión está instalada.
async function hello() {
  try { await ask({ type: "hello", browser: browserName() }, 5000); } catch { /* sin app: nada */ }
}
api.runtime.onStartup?.addListener(hello);
api.runtime.onInstalled.addListener(() => {
  hello();
  try {
    api.contextMenus.removeAll(() => {
      api.contextMenus.create({ id: "soc-fill", title: api.i18n.getMessage("contextFill"), contexts: ["editable"] });
    });
  } catch { /* Firefox sin contextMenus en algún contexto: da igual */ }
});

// ------------------------------------------------------------------ insignia con el número de entradas

async function updateBadge(tabId, url) {
  const host = hostOf(url ?? "");
  if (!host) { setBadge(tabId, ""); return; }
  try {
    const r = await ask({ type: "list", host }, 4000);
    if (r.locked) setBadge(tabId, "!", "#BA1A1A");   // bóveda cerrada: aviso en rojo, como el del popup (sin emoji)
    else setBadge(tabId, r.entries?.length ? String(r.entries.length) : "");
  } catch { setBadge(tabId, ""); }
}
function setBadge(tabId, text, color = "#3525CD") {
  try {
    api.action.setBadgeText({ tabId, text });
    if (text) api.action.setBadgeBackgroundColor({ tabId, color });
  } catch { }
}
api.tabs.onActivated.addListener(async ({ tabId }) => {
  try { const tab = await api.tabs.get(tabId); updateBadge(tabId, tab.url); } catch { }
});
api.tabs.onUpdated.addListener((tabId, info, tab) => {
  if (info.status === "complete") {
    updateBadge(tabId, tab.url);
    offerPendingSave(tabId, tab.url);
  }
});

// ------------------------------------------------------------------ guardar lo que se escribe

// Lo que el usuario acaba de enviar en un formulario, por pestaña: se ofrece guardarlo cuando la
// página siguiente termina de cargar (o al momento, si la página no cambia).
const pendingSaves = new Map();   // tabId → {host, username, password, at, token}
const offered = new Map();        // token → {host, username, password}: lo que está en una barra «¿Guardar?»

async function offerPendingSave(tabId, url) {
  const p = pendingSaves.get(tabId);
  if (!p) return;
  if (Date.now() - p.at > 60000) { pendingSaves.delete(tabId); return; }
  const host = hostOf(url ?? "");
  // Solo si seguimos en el mismo sitio (o un subdominio): si el envío redirige fuera, no es un login.
  if (!host || !(host === p.host || host.endsWith("." + p.host) || p.host.endsWith("." + host))) return;
  pendingSaves.delete(tabId);
  let mode = "save";
  try {
    const r = await ask({ type: "list", host: p.host }, 5000);
    if (r.locked) mode = "save";
    else {
      const same = (r.entries ?? []).find(e => e.username.toLowerCase() === p.username.toLowerCase());
      if (same && same.password === p.password) return;     // ya está tal cual
      if (same) mode = "update";
    }
  } catch { return; }                                        // sin aplicación no hay donde guardar
  offered.set(p.token, { host: p.host, username: p.username, password: p.password });
  setTimeout(() => offered.delete(p.token), 5 * 60 * 1000);
  try {
    await sendToTab(tabId, { type: "showSaveBar", mode, username: p.username, site: p.host, token: p.token });
  } catch { }
}

async function sendToTab(tabId, message) {
  try { return await api.tabs.sendMessage(tabId, message); }
  catch (e) {
    // La página se cargó antes de instalar la extensión: se inyecta el script y se reintenta.
    await api.scripting.executeScript({ target: { tabId }, files: ["content.js"] });
    return await api.tabs.sendMessage(tabId, message);
  }
}

// ------------------------------------------------------------------ peticiones del popup y del contenido

api.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  (async () => {
    try {
      switch (msg.type) {
        case "list": return await ask({ type: "list", host: msg.host });
        case "search": return await ask({ type: "search", query: msg.query });
        case "show": return await ask({ type: "show" }, 5000);
        case "save":
          return await ask({ type: "save", host: msg.host || hostOf(sender.tab?.url ?? sender.url ?? ""), username: msg.username, password: msg.password }, 120000);
        case "saveOffered": {
          const o = offered.get(msg.token);
          if (!o) return { error: "expired" };
          const r = await ask({ type: "save", host: o.host, username: o.username, password: o.password }, 120000);
          if (r?.ok) offered.delete(msg.token);
          return r;
        }
        case "submitted": {
          // El contenido avisa de que se envió un formulario con contraseña.
          if (sender.tab?.id != null && msg.password) {
            pendingSaves.set(sender.tab.id, { host: hostOf(sender.tab.url ?? sender.url ?? ""), username: msg.username ?? "", password: msg.password, at: Date.now(), token: Math.random().toString(36).slice(2) });
            // Si la página no navega (login por AJAX), se ofrece a los 1,5 s igualmente.
            setTimeout(() => { try { api.tabs.get(sender.tab.id).then(t => offerPendingSave(sender.tab.id, t.url)); } catch { } }, 1500);
          }
          return { ok: true };
        }
        case "fill": {
          const tabId = msg.tabId;
          return await sendToTab(tabId, { type: "fill", username: msg.username, password: msg.password, totp: msg.totp });
        }
        case "fillText": return await sendToTab(msg.tabId, { type: "fillText", text: msg.text });
        case "hello": return await ask({ type: "hello", browser: browserName() }, 5000);
        default: return { error: "unknown" };
      }
    } catch (e) {
      return { error: e.message || String(e) };
    }
  })().then(sendResponse);
  return true;   // respuesta asíncrona
});

// Menú contextual sobre un campo: si hay una sola entrada para el sitio se rellena; si hay varias, el popup.
api.contextMenus?.onClicked.addListener(async (info, tab) => {
  if (info.menuItemId !== "soc-fill" || !tab?.id) return;
  const host = hostOf(tab.url ?? "");
  try {
    const r = await ask({ type: "list", host });
    if (r.locked) { await ask({ type: "show" }, 5000); return; }
    const entries = r.entries ?? [];
    if (entries.length === 1)
      await sendToTab(tab.id, { type: "fill", username: entries[0].username, password: entries[0].password, totp: entries[0].totp });
    else
      try { await api.action.openPopup(); } catch { }
  } catch { }
});
