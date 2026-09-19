// sOC Credentials — popup: las entradas del sitio de la pestaña activa (o una búsqueda en toda la
// bóveda), rellenar/copiar, el código TOTP en vivo y un generador de contraseñas.
const api = globalThis.browser ?? globalThis.chrome;
const t = (k, ...s) => api.i18n.getMessage(k, s) || k;
const $ = (id) => document.getElementById(id);

let tab = null;
let host = "";
let entries = [];
let retryTimer = null;

function hostOf(url) {
  try { const u = new URL(url); return (u.protocol === "http:" || u.protocol === "https:") ? u.hostname : ""; }
  catch { return ""; }
}

async function init() {
  for (const [id, key] of [["genTitle", "generator"], ["lenLabel", "length"], ["upperLabel", "upper"], ["digitsLabel", "digits"], ["symbolsLabel", "symbols"], ["regen", "generate"], ["copyGen", "copyPass"], ["useGen", "useInPage"], ["openApp", "openApp"]])
    $(id).textContent = t(key);
  $("search").placeholder = t("search");
  const tabs = await api.tabs.query({ active: true, currentWindow: true });
  tab = tabs[0] ?? null;
  host = hostOf(tab?.url ?? "");
  $("site").textContent = host || "";
  $("search").addEventListener("input", onSearch);
  $("openApp").addEventListener("click", () => api.runtime.sendMessage({ type: "show" }));
  $("len").addEventListener("input", () => { $("lenVal").textContent = $("len").value; regen(); });
  for (const id of ["optUpper", "optDigits", "optSymbols"]) $(id).addEventListener("change", regen);
  $("regen").addEventListener("click", regen);
  $("copyGen").addEventListener("click", () => copy($("generated").value));
  $("useGen").addEventListener("click", async () => {
    if (!tab) return;
    await api.runtime.sendMessage({ type: "fillText", tabId: tab.id, text: $("generated").value });
    window.close();
  });
  regen();
  await load();
  setInterval(tickTotp, 1000);
}

async function load() {
  const q = $("search").value.trim();
  const r = q ? await api.runtime.sendMessage({ type: "search", query: q }) : await api.runtime.sendMessage({ type: "list", host });
  render(r, q);
}

function render(r, q) {
  const status = $("status");
  const open = $("openApp");
  status.hidden = true; open.hidden = true;
  entries = [];
  if (!r || r.error) {
    status.hidden = false;
    const noApp = r?.error === "nohost" || r?.error === "noapp";
    status.textContent = noApp ? t("noHost") : (r?.detail ?? r?.error ?? "?");
    open.hidden = noApp;
    $("list").innerHTML = "";
    return;
  }
  if (r.locked) {
    status.hidden = false;
    status.textContent = t("locked") + " " + t("retrying");
    open.hidden = false;
    $("list").innerHTML = "";
    if (!retryTimer) retryTimer = setInterval(async () => { const rr = await api.runtime.sendMessage(q ? { type: "search", query: q } : { type: "list", host }); if (!rr?.locked) { clearInterval(retryTimer); retryTimer = null; render(rr, q); } }, 2000);
    return;
  }
  entries = r.entries ?? [];
  if (entries.length === 0) {
    status.hidden = false;
    status.textContent = q ? t("noMatches") : (host ? t("noMatches") : "");
  }
  const list = $("list");
  list.innerHTML = "";
  for (const e of entries) {
    const li = document.createElement("li");
    li.dataset.id = e.id;
    const name = document.createElement("div"); name.className = "name"; name.textContent = e.title; li.appendChild(name);
    const user = document.createElement("div"); user.className = "user"; user.textContent = e.username || (e.url || ""); li.appendChild(user);
    const actions = document.createElement("div"); actions.className = "actions";
    actions.appendChild(btn("⤵", t("fill"), async () => { if (tab) { await api.runtime.sendMessage({ type: "fill", tabId: tab.id, username: e.username, password: e.password, totp: e.totp }); window.close(); } }, true));
    if (e.username) actions.appendChild(btn("👤", t("copyUser"), () => copy(e.username)));
    if (e.password) actions.appendChild(btn("🔑", t("copyPass"), () => copy(e.password)));
    li.appendChild(actions);
    if (e.totp) {
      const totp = document.createElement("div"); totp.className = "totp";
      const code = document.createElement("span"); code.className = "code"; code.textContent = pretty(e.totp);
      const left = document.createElement("span"); left.className = "left"; left.textContent = e.totpLeft != null ? e.totpLeft + " s" : "";
      totp.appendChild(code); totp.appendChild(left);
      totp.appendChild(btn("📋", t("copyTotp"), () => copy(e.totp)));
      li.appendChild(totp);
    }
    list.appendChild(li);
  }
}

function btn(text, title, onClick, primary = false) {
  const b = document.createElement("button");
  b.className = "icon" + (primary ? " primary" : "");
  b.textContent = text; b.title = title;
  b.addEventListener("click", onClick);
  return b;
}

function pretty(code) { return code.length >= 6 ? code.slice(0, Math.ceil(code.length / 2)) + " " + code.slice(Math.ceil(code.length / 2)) : code; }

let searchTimer = null;
function onSearch() { clearTimeout(searchTimer); searchTimer = setTimeout(load, 200); }

// Los códigos TOTP caducan: se vuelven a pedir cuando a alguno le quede poco.
let lastTick = Date.now();
async function tickTotp() {
  if (!entries.some(e => e.totp)) return;
  let refresh = false;
  for (const e of entries) {
    if (e.totp && e.totpLeft != null) { e.totpLeft -= 1; if (e.totpLeft <= 0) refresh = true; }
  }
  for (const li of document.querySelectorAll("li")) {
    const e = entries.find(x => x.id === li.dataset.id);
    const left = li.querySelector(".left");
    if (e && left) left.textContent = e.totpLeft != null ? Math.max(0, e.totpLeft) + " s" : "";
  }
  if (refresh) await load();
}

async function copy(text) {
  try { await navigator.clipboard.writeText(text); toast(t("copied")); }
  catch {
    const ta = document.createElement("textarea"); ta.value = text; document.body.appendChild(ta); ta.select(); document.execCommand("copy"); ta.remove(); toast(t("copied"));
  }
}

function toast(msg) {
  const d = document.createElement("div"); d.className = "toast"; d.textContent = msg;
  document.body.appendChild(d); setTimeout(() => d.remove(), 1200);
}

// Generador: mismos criterios que la aplicación (al menos uno de cada clase elegida).
function regen() {
  const len = parseInt($("len").value, 10);
  const sets = ["abcdefghijkmnopqrstuvwxyz"];
  if ($("optUpper").checked) sets.push("ABCDEFGHJKLMNPQRSTUVWXYZ");
  if ($("optDigits").checked) sets.push("23456789");
  if ($("optSymbols").checked) sets.push("!@#$%&*+-=?_");
  const all = sets.join("");
  const rnd = (n) => { const a = new Uint32Array(1); crypto.getRandomValues(a); return a[0] % n; };
  const out = sets.map(s => s[rnd(s.length)]);
  while (out.length < len) out.push(all[rnd(all.length)]);
  for (let i = out.length - 1; i > 0; i--) { const j = rnd(i + 1); [out[i], out[j]] = [out[j], out[i]]; }
  $("generated").value = out.join("");
}

init();
