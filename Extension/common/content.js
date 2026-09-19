// sOC Credentials — script de contenido. Dos cosas: rellenar los campos de usuario y contraseña
// cuando lo pide el popup (o el menú contextual) y, al enviar un formulario con contraseña, avisar
// al fondo para que ofrezca guardarlo. No manda nada a ningún sitio: solo habla con la extensión.
(() => {
  if (globalThis.__socCredentials) return;   // inyectado dos veces (scripting.executeScript): una basta
  globalThis.__socCredentials = true;
  const api = globalThis.browser ?? globalThis.chrome;
  const t = (k, ...s) => api.i18n.getMessage(k, s) || k;

  // ---------------------------------------------------------------- campos

  function visible(el) {
    if (!el || !el.isConnected) return false;
    const r = el.getBoundingClientRect();
    if (r.width === 0 && r.height === 0) return false;
    const cs = getComputedStyle(el);
    return cs.visibility !== "hidden" && cs.display !== "none";
  }

  const USER_TYPES = new Set(["text", "email", "tel", "", null, undefined]);
  function looksLikeUser(input) {
    if (!USER_TYPES.has(input.type)) return false;
    const s = `${input.name} ${input.id} ${input.autocomplete} ${input.placeholder} ${input.getAttribute("aria-label") ?? ""}`.toLowerCase();
    if (/search|buscar|captcha|otp|code|código|codigo|token|pin\b/.test(s)) return false;
    return true;
  }

  /** Campos de un formulario de acceso: la contraseña y el usuario más cercano por delante. */
  function findFields(preferFocused = true) {
    const inputs = [...document.querySelectorAll("input")].filter(visible);
    const passwords = inputs.filter(i => i.type === "password");
    if (passwords.length === 0) return { user: inputs.find(looksLikeUser) ?? null, pass: null, inputs };
    let pass = passwords[0];
    const active = document.activeElement;
    if (preferFocused && active instanceof HTMLInputElement) {
      if (active.type === "password") pass = active;
      else {
        const after = passwords.find(p => (active.compareDocumentPosition(p) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0);
        if (after) pass = after;
      }
    }
    // El usuario: por autocomplete=username; si no, el último campo de texto antes de la contraseña
    // (en el mismo formulario si lo hay).
    const scope = pass.form ?? document;
    const scoped = [...scope.querySelectorAll("input")].filter(visible);
    let user = scoped.find(i => (i.autocomplete || "").includes("username") && i !== pass) ?? null;
    if (!user) {
      const before = scoped.filter(i => i !== pass && (i.compareDocumentPosition(pass) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0 && looksLikeUser(i));
      user = before.length ? before[before.length - 1] : null;
    }
    if (!user) {
      // Acceso en dos pasos (primero el usuario, luego la contraseña): el usuario puede estar solo.
      user = inputs.find(looksLikeUser) ?? null;
    }
    return { user, pass, inputs };
  }

  function setValue(input, value) {
    if (!input) return false;
    const proto = Object.getPrototypeOf(input);
    const setter = Object.getOwnPropertyDescriptor(proto, "value")?.set ?? Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
    input.focus();
    if (setter) setter.call(input, value); else input.value = value;
    for (const type of ["input", "change"])
      input.dispatchEvent(new Event(type, { bubbles: true }));
    input.dispatchEvent(new KeyboardEvent("keyup", { bubbles: true }));
    return true;
  }

  function fill({ username, password, totp }) {
    const { user, pass } = findFields();
    let done = 0;
    if (username && user) done += setValue(user, username) ? 1 : 0;
    if (password && pass) done += setValue(pass, password) ? 1 : 0;
    if (!pass && !user && totp && document.activeElement instanceof HTMLInputElement)
      done += setValue(document.activeElement, totp) ? 1 : 0;
    // Si solo hay usuario (acceso en dos pasos) y también hay TOTP pedido en un campo enfocado, se respeta.
    if (pass) pass.focus();
    return done;
  }

  function fillText(text) {
    const el = document.activeElement;
    if (el instanceof HTMLInputElement || el instanceof HTMLTextAreaElement) return setValue(el, text) ? 1 : 0;
    const { pass } = findFields(false);
    return pass ? (setValue(pass, text) ? 1 : 0) : 0;
  }

  // ---------------------------------------------------------------- envío de formularios

  // Lo último escrito en un campo de contraseña y en su usuario, para ofrecer guardarlo al enviar.
  let last = { username: "", password: "" };
  function remember() {
    const { user, pass } = findFields(false);
    if (pass && pass.value) last = { username: user?.value ?? last.username, password: pass.value };
    else if (user && user.value) last.username = user.value;
  }
  document.addEventListener("input", (e) => {
    if (e.target instanceof HTMLInputElement) remember();
  }, true);

  function submitted() {
    remember();
    if (!last.password) return;
    try { api.runtime.sendMessage({ type: "submitted", username: last.username, password: last.password }); } catch { }
    last = { username: last.username, password: "" };
  }
  document.addEventListener("submit", submitted, true);
  document.addEventListener("keydown", (e) => {
    if (e.key === "Enter" && e.target instanceof HTMLInputElement && (e.target.type === "password" || looksLikeUser(e.target)))
      setTimeout(submitted, 0);
  }, true);
  document.addEventListener("click", (e) => {
    const el = e.target instanceof Element ? e.target.closest("button, input[type=submit], [role=button]") : null;
    if (!el) return;
    const { pass } = findFields(false);
    if (pass && pass.value) setTimeout(submitted, 0);
  }, true);

  // ---------------------------------------------------------------- barra «¿Guardar?»

  let bar = null;
  function showSaveBar({ mode, username, site, token }) {
    if (bar) bar.remove();
    bar = document.createElement("div");
    bar.style.cssText = "all:initial;position:fixed;top:12px;right:12px;z-index:2147483647;";
    const root = bar.attachShadow({ mode: "closed" });
    root.innerHTML = `
      <style>
        .b{font:14px 'Segoe UI',system-ui,sans-serif;background:#1E2130;color:#EEE;border:1px solid #3525CD;border-radius:12px;
           padding:12px 14px;box-shadow:0 8px 24px rgba(0,0,0,.35);max-width:360px;display:flex;flex-direction:column;gap:10px}
        .t{display:flex;align-items:center;gap:8px;font-weight:600}
        .t img{width:20px;height:20px;border-radius:5px}
        .m{opacity:.9;line-height:1.35}
        .r{display:flex;gap:8px;justify-content:flex-end}
        button{font:inherit;border-radius:8px;padding:6px 12px;cursor:pointer;border:1px solid #3525CD;background:transparent;color:#B5B0FF}
        button.p{background:#3525CD;color:#fff}
      </style>
      <div class="b">
        <div class="t"><img src="${api.runtime.getURL("icons/icon32.png")}" alt=""> sOC Credentials</div>
        <div class="m">${escapeHtml(t(mode === "update" ? "updateQuestion" : "saveQuestion", username || "—", site))}</div>
        <div class="r"><button class="n">${escapeHtml(t("notNow"))}</button><button class="p">${escapeHtml(t(mode === "update" ? "update" : "save"))}</button></div>
      </div>`;
    root.querySelector(".n").addEventListener("click", () => { bar.remove(); bar = null; });
    root.querySelector(".p").addEventListener("click", async () => {
      const btn = root.querySelector(".p");
      btn.disabled = true; btn.textContent = "…";
      try {
        // La contraseña la guarda el fondo desde que se envió el formulario: aquí solo va el token.
        const r = await api.runtime.sendMessage({ type: "saveOffered", token });
        root.querySelector(".m").textContent = r?.ok ? t("saved") : (r?.locked ? t("locked") : (r?.error ?? "?"));
        root.querySelector(".r").style.display = "none";
        setTimeout(() => { bar?.remove(); bar = null; }, r?.ok ? 2500 : 6000);
      } catch (e) {
        root.querySelector(".m").textContent = String(e?.message ?? e);
      }
    });
    (document.body ?? document.documentElement).appendChild(bar);
    setTimeout(() => { if (bar) { bar.remove(); bar = null; } }, 45000);
  }
  function escapeHtml(s) { return String(s).replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c])); }

  // ---------------------------------------------------------------- mensajes del fondo y del popup

  api.runtime.onMessage.addListener((msg, sender, sendResponse) => {
    switch (msg.type) {
      case "fill": sendResponse({ filled: fill(msg) }); break;
      case "fillText": sendResponse({ filled: fillText(msg.text) }); break;
      case "showSaveBar":
        if (window !== window.top) { sendResponse({ ok: false }); break; }   // solo en la ventana principal
        showSaveBar(msg);
        sendResponse({ ok: true });
        break;
      case "hasLogin": { const f = findFields(false); sendResponse({ pass: !!f.pass, user: !!f.user }); break; }
      default: sendResponse({});
    }
    return false;
  });
})();
