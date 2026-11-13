"use strict";
/* Fotokronika – Pracovna (etapa 1 + úpravy po prvním testu, specifikace 2.7).
   Popisek se ukládá až při další akci (Další, Předchozí, Bez popisku…), Zpět/Znovu vrací celé akce. */

const $ = id => document.getElementById(id);
const S = {
  state: null, host: "web", canPick: false,
  tree: null, open: new Set([""]), browseOpen: new Set([""]), view: "work", folder: null, photos: [], cur: null,
  photoFilter: "all",     // Vše / Nepopsané / Popsané (jen v Pracovně)
  sort: (() => { try { return localStorage.getItem("fotoalbum-razeni") || "name"; } catch { return "name"; } })(),
  side: true, menu: false,
  keep: new Set(),        // fotky vyřízené během zapnutého filtru – zůstanou vidět do přepnutí filtru
  saved: "",              // uložený popisek aktuální fotky
  busy: false, closed: false,
  hist: [], hi: 0,        // historie akcí pro Zpět/Znovu (jen v aktuální složce, do zavření okna)
};
const DRAFT_KEY = "fotoalbum-rozepsano";

/* ---------- API ---------- */
async function api(method, url, body, keepalive) {
  const res = await fetch(url, {
    method, credentials: "same-origin", keepalive: !!keepalive,
    headers: body ? { "Content-Type": "application/json" } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!res.ok) {
    let msg = "Něco se nepovedlo (" + res.status + ").";
    try { msg = (await res.json()).error || msg; } catch { /* bez těla */ }
    throw new Error(msg);
  }
  const t = res.headers.get("content-type") || "";
  return t.includes("json") ? res.json() : null;
}
const q = p => encodeURIComponent(p);

/* ---------- pomocné ---------- */
function fmtDate(iso) {
  if (!iso) return "";
  const [d] = iso.split("T"); const [y, m, dd] = d.split("-").map(Number);
  return `${dd}. ${m}. ${y}`;
}
function esc(s) { return String(s).replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c])); }
let toastT;
function toast(msg, act, ms) {
  const h = $("toastHost");
  h.innerHTML = `<div class="toast" role="status"><span>${esc(msg)}</span>${act ? `<button>${esc(act.label)}</button>` : ""}</div>`;
  if (act) h.querySelector("button").onclick = () => { h.innerHTML = ""; act.fn(); };
  clearTimeout(toastT); toastT = setTimeout(() => h.innerHTML = "", ms || 5000);
}
function modal(html, onAction) {
  const h = $("modalHost");
  h.innerHTML = `<div class="modal"><div class="dlg" role="dialog" aria-modal="true">${html}</div></div>`;
  const first = h.querySelector("[data-a]"); if (first) first.focus();
  h.querySelector(".modal").onclick = e => {
    const b = e.target.closest("[data-a]");
    if (!b) return;
    h.innerHTML = ""; onAction(b.dataset.a);
  };
}
function fail(e) { toast(e.message || String(e), null, 8000); }
function draftSave(path, text) { try { localStorage.setItem(DRAFT_KEY, JSON.stringify({ path, text })); } catch { } }
function draftClear() { try { localStorage.removeItem(DRAFT_KEY); } catch { } }
function draftGet() { try { return JSON.parse(localStorage.getItem(DRAFT_KEY) || "null"); } catch { return null; } }
const isDirty = () => !!S.cur && $("ta").value !== S.saved;
const handled = p => p.status === "done" || p.status === "manual" || p.status === "skip";
/** Stav popisku pro historii Zpět/Znovu: text, původ (ai/manual/file) a nadpis. */
const snap = p => ({ c: S.saved, s: p.source || "manual", t: p.source === "ai" ? p.title : null });

/* ---------- start a nastavení ---------- */
async function boot() {
  try {
    const r = await api("GET", "/api/state");
    S.state = r.state; S.host = r.host; S.canPick = r.canPickFolder; renderApp(r.app); applyTextSize();
  } catch (e) {
    document.body.innerHTML = `<div class="setup"><div class="card"><h1>Fotokronika</h1><p>${esc(e.message)}</p></div></div>`;
    return;
  }
  await loadAi();
  if (!S.state.configured) return showSetup(true);
  $("setup").hidden = true; $("work").hidden = false;
  applyPanelWidths(); updateView();
  await loadTree();
  const start = S.state.lastFolder ?? firstFolderWithPhotos(S.tree);
  if (start != null) await openFolder(start, true);
  updatePending();
}

/** Velikost písma a ovládání (nastavení „Velikost písma“), uložená v počítači. */
function applyTextSize() {
  const z = ((S.state && S.state.textSize) || 100) / 100;
  document.documentElement.style.zoom = z === 1 ? "" : String(z);
}
function textSizeSection() {
  return `<h2>Velikost písma</h2>
    <p>Zvětší nebo zmenší písmo i tlačítka v celé aplikaci.</p>
    <div class="row" style="align-items:center"><button class="b small" id="tsMinus" aria-label="Menší písmo">A− Menší</button>
      <b id="tsVal" style="min-width:70px;text-align:center;font-size:20px">${(S.state && S.state.textSize) || 100} %</b>
      <button class="b small" id="tsPlus" aria-label="Větší písmo">A+ Větší</button>
      <button class="b small" id="tsReset">Výchozí velikost</button></div>`;
}
function wireTextSize() {
  const set = async v => {
    v = Math.max(80, Math.min(140, v));
    try { S.state = await api("POST", "/api/settings", { textSize: v }); applyTextSize(); $("tsVal").textContent = S.state.textSize + " %"; } catch (e) { fail(e); }
  };
  const curSize = () => (S.state && S.state.textSize) || 100;
  $("tsMinus").onclick = () => set(curSize() - 10);
  $("tsPlus").onclick = () => set(curSize() + 10);
  $("tsReset").onclick = () => set(100);
}

function showSetup(first) {
  const st = S.state;
  $("work").hidden = true; const el = $("setup"); el.hidden = false; updateView();
  el.innerHTML = `<div class="card">
    <div class="head"><h1>${first ? "Vítejte ve Fotokronice" : "Nastavení"}</h1>${first ? "" : `<button class="b small solid" id="back">Zpět do Fotokroniky</button>`}</div>
    ${st.problem ? `<div class="note bad">${esc(st.problem)}</div>` : ""}
    <div class="sec" style="border-top:0;padding-top:0">
      ${first ? "" : "<h2>Složka s fotkami</h2>"}
      <p>Vyberte složku, ve které jsou fotky. Aplikace v ní nic nemaže; popisky zapisuje přímo do fotek.</p>
      <input type="text" id="rootIn" value="${esc(st.root || "")}" placeholder="např. D:\\Fotky" autocomplete="off" aria-label="Složka s fotkami">
      <div class="row">${S.canPick ? `<button class="b small" id="pick">Vybrat složku…</button>` : ""}<button class="b small solid" id="saveRoot">${first ? "Uložit a otevřít" : "Uložit složku"}</button></div>
      <div class="note ${st.exifToolOk ? "ok" : "bad"}">${st.exifToolOk
        ? `Zápis popisků do fotek je připravený (ExifTool ${esc(st.exifToolVersion || "")}).`
        : "Chybí program ExifTool, bez něj se popisky nezapíší do fotek (ukládají se jen v aplikaci). Umístěte <b>exiftool.exe</b> do složky <b>exiftool</b> vedle aplikace."}</div>
    </div>
    ${first ? "" : `<div class="sec">${textSizeSection()}</div>`}
    <div class="sec"><h2>Kopírování z karty</h2>
      <label class="toggle"><input type="checkbox" id="autoImp" ${st.autoImport ? "checked" : ""}> <span>Po připojení karty z fotoaparátu automaticky zkopírovat nové fotky a videa do složek „rok + roční období“ (např. „2026 léto“).</span></label>
    </div>
    ${first ? "" : `<div class="sec">${aiSection()}</div>`}
    ${first ? "" : `<div class="sec">${updatesSection()}</div>`}
    ${first ? "" : `<div class="sec"><h2>Data aplikace</h2><p style="font-size:16px">Databáze a náhledy jsou v profilu uživatele. Záloha databáze se ukládá do složky <b>Fotokronika – databáze (nemazat)</b> ve složce s fotkami.</p></div>`}
  </div>`;
  if (!first) $("autoImp").onchange = async e => {
    try { S.state = await api("POST", "/api/settings", { autoImport: e.target.checked }); toast("Uloženo."); } catch (err) { fail(err); }
  };
  if (S.canPick) $("pick").onclick = async () => {
    try { const r = await api("POST", "/api/pick-folder"); if (r.folder) $("rootIn").value = r.folder; } catch (e) { fail(e); }
  };
  $("saveRoot").onclick = async () => {
    try {
      S.state = await api("POST", "/api/settings", { photoRoot: $("rootIn").value, autoImport: $("autoImp").checked });
      S.tree = null; S.folder = null; S.cur = null; S.photos = []; S.view = "work"; resetLibrary();
      el.hidden = true; await boot();
    } catch (e) { fail(e); }
  };
  if (!first) $("back").onclick = () => { el.hidden = true; $("work").hidden = false; updateView(); };
  if (!first) wireAiSection();
  if (!first) wireTextSize();
  if (!first && S.app && S.app.installed) {
    document.querySelectorAll("[name=chan]").forEach(r => r.onchange = async () => {
      setUpdMsg("Přepínám kanál a hledám verzi…");
      try { renderApp(await api("POST", "/api/update/channel", { channel: r.value })); setUpdMsg(updText(S.app)); } catch (e) { fail(e); }
    });
    $("updCheck").onclick = async () => {
      setUpdMsg("Hledám novou verzi…");
      try { renderApp(await api("POST", "/api/update/check")); setUpdMsg(updText(S.app)); } catch (e) { fail(e); }
    };
  }
}

/* ---------- verze a aktualizace (specifikace 2.8) ---------- */
function updText(a) {
  return ({
    ready: `Nová verze ${a.newVersion} je stažená – nainstaluje se po zavření okna.`,
    downloading: "Stahuji novou verzi…", checking: "Hledám novou verzi…",
    uptodate: "Máte nejnovější verzi.", error: a.message || "Kontrola aktualizací se nepovedla.",
    unavailable: "Aktualizace fungují jen u verze nainstalované instalátorem.",
  })[a.status] || "";
}
function setUpdMsg(t) { const el = $("updMsg"); if (el) el.textContent = t; }
function updatesSection() {
  const a = S.app || {};
  if (!a.installed) return `<h2>Aktualizace</h2><p>Verze ${esc(a.version || "")}. Automatické aktualizace fungují jen u verze nainstalované instalátorem (Setup.exe).</p>`;
  return `<h2>Aktualizace</h2>
    <p>Nainstalovaná verze <b>${esc(a.version)}</b>. Nové verze se stahují samy a instalují se po zavření okna nebo při příštím startu.</p>
    <div class="channels">
      <label class="toggle" style="font-weight:400"><input type="radio" name="chan" value="stable" ${a.channel !== "test" ? "checked" : ""}> Stabilní verze (schválené – pro běžné používání)</label>
      <label class="toggle" style="font-weight:400"><input type="radio" name="chan" value="test" ${a.channel === "test" ? "checked" : ""}> Testovací verze (jen pro správce – zkušební novinky)</label>
    </div>
    <div class="row"><button class="b small" id="updCheck">Zkontrolovat aktualizace teď</button></div>
    <p id="updMsg">${esc(updText(a))}</p>`;
}
let shownUpdatedTo = false;
function renderApp(a) {
  if (!a) return;
  S.app = a;
  $("ver").textContent = "verze " + a.version + (a.channel === "test" ? " · test" : "");
  const u = $("upd");
  const msg = a.status === "ready" ? `Nová verze ${a.newVersion} – nainstaluje se po zavření okna`
    : a.status === "downloading" ? "Stahuji novou verzi…" : "";
  u.hidden = !msg; u.textContent = msg;
  if (a.updatedTo && !shownUpdatedTo) { shownUpdatedTo = true; toast(`Fotokronika byla aktualizována na verzi ${a.updatedTo}.`, null, 6000); }
}

/* ---------- strom složek ---------- */
const ico = {
  folder: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 6h6l2 2h10v11H3z"/></svg>',
  open: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M6 9l6 6 6-6"/></svg>',
  shut: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M9 6l6 6-6 6"/></svg>',
};
async function loadTree() { S.tree = await api("GET", "/api/tree"); renderTree(); }
function firstFolderWithPhotos(n) {
  if (!n) return null;
  if (n.total > 0 && n.children.length === 0) return n.path;
  for (const c of n.children) { const f = firstFolderWithPhotos(c); if (f != null) return f; }
  return n.total > 0 ? n.path : null;
}
function findNode(n, path) { if (!n) return null; if (n.path === path) return n; for (const c of n.children) { const f = findNode(c, path); if (f) return f; } return null; }
function ownCount(f) { return f.total - f.children.reduce((a, c) => a + c.total, 0); }
/** Řazení složek ve stromu i dlaždic ve Čtení: podle názvu, od nejnovějších, od nejstarších. */
function sortFolders(list, nameOf = f => f.name, dateOf = f => f.first) {
  const byName = (a, b) => (nameOf(a) || "").localeCompare(nameOf(b) || "", "cs", { numeric: true });
  const byDate = (a, b) => (dateOf(a) || "9999").localeCompare(dateOf(b) || "9999") || byName(a, b);
  return [...list].sort(S.sort === "old" ? byDate : S.sort === "new" ? (a, b) => (dateOf(b) || "").localeCompare(dateOf(a) || "") || byName(a, b) : byName);
}
const ICON_PALM = '<svg class="palm" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 21c0-5 .5-8 1.5-11"/><path d="M13.5 10c-1.5-3-4.5-4-8-3 2.5.2 4.5 1.3 5.5 3"/><path d="M13.5 10c1-3 4-5 7.5-4.5-2.5.6-4.5 2-5.5 4"/><path d="M13.5 10c3-.5 6 .8 7.5 3.5-2.5-1.2-5-1.5-7.5-1"/><path d="M13.5 10c-3 .2-5.5 2-6.5 5 1.5-1.8 3.8-3 6.5-3.5"/><path d="M7 21h10"/></svg>';
function expandTo(path, expanded, includeFolder = false) {
  const parts = path.split("/");
  expanded.clear();
  expanded.add("");
  for (let i = 1; i < parts.length + Number(includeFolder); i++) expanded.add(parts.slice(0, i).join("/"));
}
function renderTree() {
  const el = $("tree");
  const crumbs = $("crumbs");
  const scrollTop = el.scrollTop;
  el.replaceChildren(); crumbs.replaceChildren();
  if (!S.tree) return;
  // strom je ve Čtení stejný jako v Pracovně; klepnutí ve Čtení rovnou otevře čtení složky
  const browse = S.view !== "work";
  const selected = browse ? (B.reader ? B.reader.album.path : null) : S.folder;
  const expanded = S.open;
  const shown = () => true;
  const choose = path => {
    if (browse) { const f = findNode(S.tree, path); if (f && f.total) openReader({ path, name: f.name || S.tree.name, intro: "" }); }
    else openFolder(path).catch(fail);
  };
  const nodes = ownCount(S.tree) > 0 || !S.tree.children.length ? [S.tree] : S.tree.children;
  const mk = (f, host) => {
    if (!shown(f)) return;
    const li = document.createElement("li");
    const row = document.createElement("div");
    row.className = "tree-line";
    const kids = sortFolders(f.children.filter(shown));
    if (kids.length) {
      const toggle = document.createElement("button");
      toggle.type = "button";
      toggle.className = "tree-expand";
      toggle.dataset.path = f.path;
      toggle.innerHTML = expanded.has(f.path) ? ico.open : ico.shut;
      toggle.setAttribute("aria-expanded", expanded.has(f.path));
      toggle.setAttribute("aria-label", (expanded.has(f.path) ? "Sbalit " : "Rozbalit ") + f.name);
      toggle.onclick = () => {
        if (expanded.has(f.path)) expanded.delete(f.path);
        else expandTo(f.path, expanded, true);
        renderTree();
        [...el.querySelectorAll(".tree-expand")].find(button => button.dataset.path === f.path)?.focus();
      };
      row.append(toggle);
    }
    const b = document.createElement("button");
    const vacation = f.kind === "dovolena";
    b.className = "node" + (vacation ? " vacation" : "");
    b.title = (f.name || "Všechny fotky") + (vacation ? " – dovolená" : "") + (f.ownTotal ? "" : " (fotky jsou jen v podsložkách)");
    b.setAttribute("aria-current", selected === f.path);
    // počet = jen fotky přímo v této složce (podsložky mají vlastní)
    b.innerHTML = (vacation ? ICON_PALM : ico.folder) + `<span class="nm">${esc(f.name || "Všechny fotky")}</span>`
      + (f.complete ? '<span class="folder-complete" title="Složka hotová" aria-label="Složka hotová">✓</span>' : "")
      + (f.ownTotal ? `<span class="cnt" title="Vyřízené fotky přímo v této složce">${f.ownHandled}/${f.ownTotal}</span>` : "");
    b.oncontextmenu = event => {
      event.preventDefault();
      if (!S.busy && !$("modalHost").firstChild) showFolderMenu(event, f, b);
    };
    b.onclick = () => choose(f.path);
    row.append(b); li.append(row);
    if (kids.length && expanded.has(f.path)) {
      const ul = document.createElement("ul");
      kids.forEach(c => mk(c, ul));
      li.append(ul);
    }
    host.append(li);
  };
  sortFolders(nodes).forEach(n => mk(n, el));
  if (!el.children.length) el.innerHTML = '<li class="treeempty">Žádná složka se zvoleným filtrem.</li>';
  const parts = selected ? selected.split("/") : [];
  crumbs.hidden = browse || selected == null;
  if (!crumbs.hidden) {
    const names = [S.tree.name || "Všechny fotky", ...parts];
    names.forEach((name, i) => {
      if (i) {
        const separator = document.createElement("span");
        separator.textContent = "›";
        separator.setAttribute("aria-hidden", "true");
        crumbs.append(separator);
      }
      const current = i === names.length - 1;
      const item = document.createElement(current ? "span" : "button");
      item.textContent = name;
      if (current) item.setAttribute("aria-current", "location");
      else {
        item.type = "button";
        item.onclick = () => choose(parts.slice(0, i).join("/"));
      }
      crumbs.append(item);
    });
  }
  el.scrollTop = scrollTop;
}

/** Nabídka pravého tlačítka na složce: dovolená ano/ne, v Pracovně i „Označit složku jako hotovou“. */
function showFolderMenu(event, folder, anchor) {
  const menu = $("folderMenu");
  const vacation = folder.kind === "dovolena";
  const items = [[vacation ? "Zrušit označení dovolené" : "🌴 Označit jako dovolenou", () => setFolderKind(folder.path, vacation ? "bezne" : "dovolena")]];
  if (S.view === "work" && folder.ownTotal > 0 && !folder.complete)
    items.push(["✓ Označit složku jako hotovou", () => changeFolderCompletion(folder.path)]);
  menu.replaceChildren(...items.map(([label, fn]) => {
    const button = document.createElement("button");
    button.type = "button"; button.className = "b"; button.textContent = label;
    button.onclick = () => { menu.hidden = true; anchor.focus({ preventScroll: true }); Promise.resolve(fn()).catch(fail); };
    return button;
  }));
  const note = document.createElement("small");
  note.textContent = "Platí jen pro tuto složku, ne pro podsložky.";
  menu.append(note);
  menu.hidden = false;
  const rect = anchor.getBoundingClientRect();
  const x = event.clientX || rect.left, y = event.clientY || rect.bottom;
  menu.style.left = Math.max(0, Math.min(x, window.innerWidth - menu.offsetWidth)) + "px";
  menu.style.top = Math.max(0, Math.min(y, window.innerHeight - menu.offsetHeight)) + "px";
  menu.firstElementChild.focus({ preventScroll: true });
}
/** Označí / odznačí složku jako dovolenou (jen tuto složku). */
async function setFolderKind(path, kind) {
  await api("POST", "/api/folder/kind", { path, kind });
  await loadTree();
  if (S.view === "work" && S.folder != null) await refreshFolderMeta();
  else if (S.view !== "work") loadAlbums();
  toast(kind === "dovolena" ? "Složka je označená jako dovolená." : "Označení dovolené zrušeno.");
}

async function changeFolderCompletion(path, undoBatch = null) {
  if (S.busy || S.view !== "work") return;
  if (path === S.folder && !(await guardLeave())) return;
  if (S.busy) return;
  S.busy = true;
  renderButtons();
  let result;
  const textarea = $("ta");
  textarea.readOnly = true;
  try {
    if (path === S.folder) await commit();
    toast(undoBatch ? "Vracím označení fotek…" : "Označuji složku jako hotovou…");
    result = await api("POST", "/api/folder/complete", { path, undoBatch });
    if (path === S.folder) {
      const changed = new Set(result.paths);
      const updated = new Map((result.photos || []).map(photo => [photo.path, photo]));
      const before = S.cur, previousCaption = S.saved;
      for (const photo of S.photos) {
        if (updated.has(photo.path)) Object.assign(photo, updated.get(photo.path));
        if (changed.has(photo.path)) {
          if (undoBatch) photo.status = "none";
          S.keep.delete(photo.path);
        }
      }
      S.saved = cur()?.caption || "";
      renderAll(before !== S.cur || previousCaption !== S.saved);
    }
  } finally {
    textarea.readOnly = false;
    S.busy = false;
    renderButtons();
  }
  loadTree().catch(fail);
  if (undoBatch) {
    toast(`Vráceno u ${result.paths.length} fotek.`);
  } else if (result.paths.length) {
    toast(`Bez popisku označeno ${result.paths.length} fotek.`,
      { label: "Vrátit označení", fn: () => changeFolderCompletion(path, result.batch).catch(fail) }, 15000);
  } else {
    toast("Ve složce nejsou fotky k označení.");
  }
}
async function refreshFolderMeta() {
  await loadTree();
  const r = await api("GET", "/api/folder?path=" + q(S.folder));
  const byPath = new Map(r.photos.map(p => [p.path, p]));
  S.photos = S.photos.map(p => Object.assign(p, byPath.get(p.path) || {}));
  renderAll(false);
}

/* ---------- složka a fotky ---------- */
async function openFolder(path, initial) {
  if (S.busy) return;
  if (!initial) { if (!(await guardLeave())) return; await commit(); closeMenu(); }
  if (S.cur) api("POST", "/api/photo/leave", { folder: S.folder, path: S.cur.path }).catch(() => {});
  S.folder = path; expandTo(path, S.open, true); renderTree();
  S.hist = []; S.hi = 0; S.keep.clear();
  $("loading").hidden = false;
  try {
    const r = await api("GET", "/api/folder?path=" + q(path));
    S.photos = r.photos; stripKey = "";
    S.cur = (r.lastPhoto && S.photos.find(p => p.path === r.lastPhoto)) || null;
    S.saved = cur() ? S.cur.caption : "";
    renderAll(true);
    // obnova textu rozepsaného před nečekaným zavřením
    const d = draftGet();
    if (initial && d && S.cur && d.path === S.cur.path && d.text !== S.saved) {
      $("ta").value = d.text; renderCaption(false); renderButtons();
      toast("Obnoven rozepsaný text, který se minule nestihl uložit.", null, 5000);
    } else if (initial && S.cur) toast("Pokračujete tam, kde jste minule skončili.", null, 3000);
    loadTree().catch(() => {});
  } catch (e) { fail(e); } finally { $("loading").hidden = true; }
}
function visible() {
  const hide = p => S.photoFilter === "undone" ? handled(p) : S.photoFilter === "done" ? !handled(p) : false;
  return S.photos.filter(p => !hide(p) || S.keep.has(p.path));
}
function cur() { const v = visible(); if (!S.cur || !v.includes(S.cur)) { S.cur = v[0] || null; S.saved = S.cur ? S.cur.caption : ""; } return S.cur; }
const byPath = path => S.photos.find(p => p.path === path);

/* ---------- historie akcí (Zpět / Znovu) ---------- */
function record(a) { S.hist = S.hist.slice(0, S.hi); S.hist.push(a); S.hi = S.hist.length; }

/** Provede akci (dopředu, nebo pozpátku) bez zápisu do historie. */
async function applyAction(a, backwards) {
  switch (a.t) {
    case "nav": await showPhoto(byPath(backwards ? a.from : a.to)); break;
    case "caption": {
      const p = byPath(a.path); if (!p) break;
      if (S.cur !== p) await showPhoto(p);
      const v = backwards ? a.before : a.after;
      const st = typeof v === "string" ? { c: v, s: "manual" } : v;
      await saveCaption(p, st.c, st.s, st.t);
      $("ta").value = S.saved; break;
    }
    case "flags": {
      const p = byPath(a.path); if (!p) break;
      if (S.cur !== p) await showPhoto(p);
      Object.assign(p, await api("POST", "/api/photo/flags", { path: p.path, ...(backwards ? a.before : a.after) }));
      break;
    }
    case "multi": for (const x of (backwards ? [...a.list].reverse() : a.list)) await applyAction(x, backwards); break;
  }
}
async function undo() {
  if (S.busy || !S.cur) return;
  if (isDirty()) { // neuložené psaní se vrátí celé najednou
    $("ta").value = S.saved; draftClear(); renderCaption(false); renderButtons(); return;
  }
  if (S.hi === 0) return;
  await run(async () => { S.hi--; await applyAction(S.hist[S.hi], true); });
}
async function redo() {
  if (S.busy || isDirty() || S.hi >= S.hist.length) return;
  await run(async () => { await applyAction(S.hist[S.hi], false); S.hi++; });
}
/** Spustí akci, zamkne tlačítka a překreslí vše. */
async function run(fn) {
  S.busy = true; renderButtons();
  try { await fn(); } catch (e) { fail(e); }
  finally { S.busy = false; renderAll(false); loadTree().catch(() => {}); }
}

/* ---------- ukládání popisku ---------- */
async function saveCaption(p, text, source, title, raw) {
  const d = await api("PUT", "/api/photo/caption", { path: p.path, caption: text, isDraft: false, source: source || "manual", title: title || null, raw: raw ?? null });
  Object.assign(p, d);
  if (S.cur === p) S.saved = d.caption;
  if (S.photoFilter !== "all") S.keep.add(p.path); // změněná fotka nezmizí hned pod rukama
  pollWrite(p);
  return d;
}
/** Uloží rozepsaný text aktuální fotky (volá se před každou akcí) a zapíše to do historie jako jeden krok. */
async function commit() {
  const p = S.cur; if (!p || !isDirty()) return;
  const before = snap(p), text = $("ta").value;
  await saveCaption(p, text, "manual");
  draftClear();
  record({ t: "caption", path: p.path, before, after: { c: S.saved, s: "manual" } });
}
let pollT;
function pollWrite(p) {
  clearTimeout(pollT);
  if (!p.writePending) return;
  pollT = setTimeout(async () => {
    try { const d = await api("GET", "/api/photo?path=" + q(p.path)); Object.assign(p, d); if (S.cur === p) renderCaption(false); updatePending(); pollWrite(p); } catch { }
  }, 2500);
}
async function updatePending() {
  if (S.closed) return;
  try { const r = await api("GET", "/api/state"); S.state = r.state; renderApp(r.app); const n = r.state.pendingWrites; const el = $("pending"); el.hidden = !n; el.textContent = n ? `Zapisuji popisky do fotek (${n})…` : ""; } catch { }
}

/* ---------- navigace ---------- */
/** Zobrazí fotku (bez ukládání a historie). */
async function showPhoto(p) {
  if (!p || p === S.cur) { renderAll(false); return; }
  const prev = S.cur;
  S.cur = p; S.saved = p.caption; S.fixText = "";
  if (prev) api("POST", "/api/photo/leave", { folder: S.folder, path: prev.path }).catch(() => {});
  api("POST", "/api/photo/position", { folder: S.folder, path: p.path }).catch(() => {});
  renderAll(true);
}
/** Uživatel přešel na jinou fotku: nejdřív uložit, pak zapsat přechod do historie. */
async function go(p) {
  if (!p || p === S.cur || S.busy) return;
  closeMenu();
  if (!(await guardLeave())) return;
  await run(async () => {
    await commit();
    const from = S.cur;
    await showPhoto(p);
    if (from) record({ t: "nav", from: from.path, to: p.path });
  });
}
function neighbour(d) { const v = visible(), i = v.indexOf(S.cur); return v[i + d] || null; }
function step(d) { const n = neighbour(d); if (n) go(n); }
function nextUndone() {
  const v = visible(), i = v.indexOf(S.cur);
  const n = v.slice(i + 1).find(p => p.status === "none") || v.find(p => p.status === "none");
  if (n) go(n); else toast("V této složce už nic nepopsaného není.");
}

/* ---------- vykreslení ---------- */
function renderAll(newPhoto) { renderStrip(newPhoto); renderStage(newPhoto); renderCaption(newPhoto); renderButtons(); }

let stripKey = "", stripMap = new Map(), io = null;
const railVisible = new Set(), railPending = new Map();
const railLoadLimit = 4;

function loadVisibleThumbnails() {
  for (const button of railVisible) {
    if (railPending.size >= railLoadLimit) break;
    const img = button.firstElementChild;
    if (img.hasAttribute("src") || img.dataset.failed || railPending.has(button)) continue;
    const controller = new AbortController();
    railPending.set(button, controller);
    fetch(img.dataset.src, { signal: controller.signal }).then(response => {
      if (!response.ok) throw new Error("Náhled se nepodařilo načíst.");
      return response.blob();
    }).then(blob => {
      if (controller.signal.aborted) return;
      const url = URL.createObjectURL(blob);
      img.addEventListener("load", () => URL.revokeObjectURL(url), { once: true });
      img.addEventListener("error", () => URL.revokeObjectURL(url), { once: true });
      img.src = url;
    }).catch(() => {
      if (!controller.signal.aborted) img.dataset.failed = "true";
    }).finally(() => {
      if (railPending.get(button) !== controller) return;
      railPending.delete(button);
      loadVisibleThumbnails();
    });
  }
}
function badgeFor(st) { return { done: ["done", "✓"], manual: ["manual", "✍"], draft: ["draft", "✎"], skip: ["skip", "–"], del: ["del", "✕"] }[st] || ["", ""]; }
function renderStrip(newPhoto) {
  const el = $("strip"), v = visible(), p = cur();
  const key = JSON.stringify([S.folder, v.map(photo => photo.path)]);
  if (key !== stripKey) {
    stripKey = key;
    if (io) io.disconnect();
    for (const controller of railPending.values()) controller.abort();
    railPending.clear();
    railVisible.clear();
    el.replaceChildren();
    stripMap.clear();
    io = new IntersectionObserver((entries, observer) => {
      if (observer !== io) return;
      for (const entry of entries) {
        const button = entry.target;
        if (entry.isIntersecting && entry.intersectionRatio >= 0.01) {
          railVisible.add(button);
        } else {
          railVisible.delete(button);
          railPending.get(button)?.abort();
          railPending.delete(button);
          delete button.firstElementChild.dataset.failed;
        }
      }
      loadVisibleThumbnails();
    }, { root: el, threshold: 0.01 });
    v.forEach(ph => {
      const b = document.createElement("button"); b.className = "thumb"; b.setAttribute("role", "listitem"); b.title = ph.title || ph.name;
      b.innerHTML = `<img alt="${esc(ph.name)}" data-src="/api/thumb?size=640&path=${q(ph.path)}"><span class="badge"></span>`;
      b.onclick = () => go(ph); stripMap.set(b, ph); el.append(b); io.observe(b);
    });
  }
  for (const [b, ph] of stripMap) {
    const [c, t] = badgeFor(ph.status); const bd = b.lastChild;
    if (bd.className !== "badge " + c) { bd.className = "badge " + c; bd.textContent = t; }
    b.classList.toggle("del", ph.status === "del"); b.classList.toggle("skip", ph.status === "skip");
    b.setAttribute("aria-current", ph === p);
    b.firstChild.style.transform = ph.rotation ? `rotate(${ph.rotation}deg)` : "";
  }
  const all = S.photos.filter(x => x.status !== "del");
  $("progress").innerHTML = p ? `<b>Fotka ${v.indexOf(p) + 1} z ${v.length}</b>vyřízeno ${all.filter(handled).length} z ${all.length}` : "";
  const folder = findNode(S.tree, S.folder);
  $("railFolder").textContent = folder?.name || "Všechny fotky";
  drawTrack();
  if (newPhoto) {
    const selected = [...stripMap.keys()].find(button => stripMap.get(button) === p);
    if (selected) selected.scrollIntoView({ block: "nearest", inline: "nearest", behavior: "instant" });
  }
}
function drawTrack() {
  const cv = $("track"), v = visible(), p = S.cur, w = cv.clientWidth; if (!w) return;
  const dpr = window.devicePixelRatio || 1; cv.width = w * dpr; cv.height = 14 * dpr;
  const c = cv.getContext("2d"); c.scale(dpr, dpr);
  const cs = getComputedStyle(document.documentElement);
  const col = { done: "--accent", manual: "--manual", draft: "--warn", skip: "--skip", none: "--line", del: "--rec" };
  const n = v.length; if (!n) return; const sw = w / n;
  v.forEach((ph, i) => { c.fillStyle = cs.getPropertyValue(col[ph.status]).trim(); c.fillRect(i * sw, 2, Math.max(sw - (sw > 4 ? 1 : 0), 1), 10); });
  const i = v.indexOf(p); if (i >= 0) { c.fillStyle = cs.getPropertyValue("--ink").trim(); c.fillRect(i * sw + sw / 2 - 1.5, 0, 3, 14); }
}
$("track").onclick = e => {
  const v = visible(); if (!v.length) return; const r = e.currentTarget.getBoundingClientRect();
  go(v[Math.min(v.length - 1, Math.max(0, Math.floor((e.clientX - r.left) / r.width * v.length)))]);
};

function renderStage(newPhoto) {
  const p = cur(), st = $("stage"), img = $("photo");
  st.querySelectorAll(".delband,.empty").forEach(n => n.remove());
  const stEl = $("state");
  const stInfo = p ? ({ done: ["done", "✓ Popsáno"], manual: ["manual", "✍ Jen ruční text"], draft: ["draft", "✎ Rozepsáno"], skip: ["skip", "– Bez popisku"], del: ["del", "✕ Ke smazání"] })[p.status] : null;
  stEl.className = "state " + (stInfo ? stInfo[0] : ""); stEl.textContent = stInfo ? stInfo[1] : "";
  if (!p) {
    img.hidden = true; $("meta").innerHTML = "";
    const e = document.createElement("div"); e.className = "empty";
    e.innerHTML = S.photos.length ? `Ve složce nejsou žádné ${S.photoFilter === "done" ? "popsané" : "nepopsané"} fotky.<button class="b small solid" id="showAll">Ukázat všechny fotky</button>`
      : "V této složce nejsou žádné fotky. Vyberte jinou složku vlevo.";
    st.append(e);
    const b = $("showAll"); if (b) b.onclick = () => { setPhotoFilter("all"); stripKey = ""; renderAll(true); };
    return;
  }
  img.hidden = false;
  if (newPhoto) { img.style.visibility = "hidden"; img.onload = () => { fitImage(); img.style.visibility = ""; }; img.src = "/api/image?path=" + q(p.path); }
  else fitImage();
  // záhlaví: nadpis z popisku (jakmile existuje), jinak název souboru
  $("meta").innerHTML = (p.title ? `<span class="chip title" title="${esc(p.name)}">${esc(p.title)}</span>` : `<span class="chip">${esc(p.name)}</span>`)
    + (p.date ? `<span class="chip">${fmtDate(p.date)}</span>` : "");
  if (p.status === "del") {
    // jen nápis přes fotku – nečeká na žádné kliknutí
    const m = document.createElement("div"); m.className = "delband";
    m.innerHTML = 'Bude smazána při zavření programu <button class="linkbtn light" id="undel">vrátit</button>';
    st.append(m); $("undel").onclick = () => setFlags({ markedDelete: false }, { markedDelete: true });
  }
}
function fitImage() {
  const p = S.cur, img = $("photo"), st = $("stage"); if (!p || !img.naturalWidth) return;
  const odd = p.rotation % 180 !== 0, W = st.clientWidth - 16, H = st.clientHeight - 16; if (W <= 0 || H <= 0) return;
  const ar = odd ? img.naturalHeight / img.naturalWidth : img.naturalWidth / img.naturalHeight;
  let w = W, h = W / ar; if (h > H) { h = H; w = H * ar; }
  img.style.width = (odd ? h : w) + "px"; img.style.height = (odd ? w : h) + "px";
  img.style.transform = p.rotation ? `rotate(${p.rotation}deg)` : "";
}

function renderCaption(newPhoto) {
  const p = S.cur, ta = $("ta");
  ta.disabled = !p || p.status === "del";
  if (newPhoto) ta.value = p ? p.caption : "";
  const dirty = isDirty();
  const st = !p ? "none" : dirty ? (ta.value.trim() ? "edit" : "none") : p.status;
  const m = $("mode");
  m.className = "mode " + ({ done: "done", manual: "manual", edit: "draft", draft: "draft" }[st] || "");
  m.textContent = { done: p && p.source === "ai" ? "Popisek (po korektuře)" : "Popisek", manual: "Jen ruční text", edit: "Píšete", draft: "Rozepsáno", skip: "Bez popisku", del: "Ke smazání" }[st] || "Prázdný popisek";
  $("fix").hidden = !S.fixText; $("fixT").textContent = S.fixText || "";
  const rb = $("recbar");
  rb.hidden = !(R.on || R.pending);
  rb.textContent = R.on ? `mluvte, text se objeví po větách${R.pending ? " (přepisuji…)" : ""}` : R.pending ? "Přepisuji poslední větu…" : "";
  $("busyBar").hidden = !S.aiWorking;
  // původní diktát u popisku po korektuře
  const hasRaw = !!(p && p.source === "ai" && p.raw && p.raw !== ta.value);
  $("rawBtn").hidden = !hasRaw;
  if (!hasRaw) S.showRaw = false;
  $("rawBtn").textContent = S.showRaw ? "Skrýt původní text" : "Původní text";
  $("rawBox").hidden = !S.showRaw;
  if (S.showRaw) $("rawTxt").textContent = p.raw;
  $("cap").classList.toggle("rec", R.on);
  if (R.on) { m.className = "mode rec"; m.textContent = "● " + (R.mode === "fix" ? "Diktujete úpravu " : "Diktujete ") + fmtTime(performance.now() - R.started); }
  ta.placeholder = p && p.status === "skip" ? "Fotka je označená „bez popisku“. Když začnete psát, značka zmizí." : "Napište, co je na fotce.";
  const s = $("save");
  const ok = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3"><path d="M5 12l5 5 9-10"/></svg>';
  if (!p) s.textContent = "";
  else if (dirty) { s.className = "save"; s.textContent = "Uloží se při přechodu dál"; }
  else if (!p.caption) { s.className = "save"; s.textContent = ""; }
  else if (p.writePending) { s.className = "save ok"; s.innerHTML = ok + "Uloženo"; }
  else { s.className = "save ok"; s.innerHTML = ok + "Uloženo do fotky"; }
  $("clear").disabled = !p || p.status === "del" || !ta.value;
  const err = $("writeErr");
  err.hidden = !(p && p.writeError);
  if (p && p.writeError) err.textContent = "Do fotky se nepodařilo zapsat: " + p.writeError + " Popisek je uložený v aplikaci a zápis se zkusí znovu.";
}
function renderButtons() {
  const p = S.cur, lock = !p || S.busy;
  $("prev").disabled = $("pL").disabled = lock || !neighbour(-1);
  $("next").disabled = $("pR").disabled = lock || !neighbour(1);
  $("skip").disabled = lock || p.status === "del";
  $("undo").disabled = lock || !(isDirty() || S.hi > 0);
  $("redo").disabled = lock || isDirty() || S.hi >= S.hist.length;
  $("more").disabled = lock;
  $("clear").disabled = lock || p.status === "del" || !$("ta").value || R.on;
  // diktování a korektura (jen se zadaným klíčem AI)
  $("aiRow").hidden = !(S.ai && S.ai.hasKey);
  const fixMode = p && p.source === "ai" && !isDirty() && S.saved.trim();
  $("mic").classList.toggle("on", R.on);
  $("mic").disabled = !p || p.status === "del" || (S.busy && !R.on);
  $("micT").textContent = R.on ? "Zastavit " + fmtTime(performance.now() - R.started) : fixMode ? "Diktovat úpravu" : "Diktovat";
  $("ai").disabled = (lock && !R.on) || S.aiWorking || !($("ta").value.trim() || S.fixText || R.on || R.pending > 0);
  const redoMode = p && p.source === "ai" && !isDirty() && !S.fixText;
  $("aiT").textContent = S.aiWorking ? "Upravuji…" : S.fixText ? "Použít úpravu" : redoMode ? "Upravit znovu" : "Korektura";
  $("ok").disabled = lock || p.status === "del" || S.aiWorking;
  $("okT").textContent = p && !neighbour(1) ? "Schválit (poslední fotka)" : "Schválit a další fotka";
  $("hint").textContent = R.on ? "Až domluvíte, stiskněte Zastavit nebo rovnou Korekturu." : "Text můžete kdykoli psát nebo opravovat přímo v poli.";
  if (R.on) { $("prev").disabled = $("next").disabled = $("skip").disabled = $("undo").disabled = $("redo").disabled = true; }
}

/* ---------- akce na fotce ---------- */
/** Změní příznaky aktuální fotky a zapíše to do historie (before = jak to bylo). */
async function setFlags(after, before, then) {
  const p = S.cur; if (!p || S.busy) return;
  closeMenu();
  await run(async () => {
    await commit();
    Object.assign(p, await api("POST", "/api/photo/flags", { path: p.path, ...after }));
    if (S.photoFilter !== "all") S.keep.add(p.path);
    record({ t: "flags", path: p.path, before, after });
    if (then) await then();
  });
}
/** Přechod na další fotku jako samostatný krok historie (Zpět nejdřív vrátí na tuto fotku). */
async function advance() {
  const n = neighbour(1); if (!n) return;
  const from = S.cur; await showPhoto(n); record({ t: "nav", from: from.path, to: n.path });
}
function skipPhoto() {
  const p = S.cur; if (!p || S.busy) return;
  if (p.status === "skip") { step(1); return; }
  const text = $("ta").value.trim();
  if (!text) { setFlags({ skip: true }, { skip: false }, advance); return; }
  // ochrana před omylem: fotka už má popisek
  modal(`<h3>Fotka má popisek</h3><p>Opravdu ho smazat a označit fotku jako „bez popisku“?</p>
    <div class="quote">${esc(text)}</div>
    <div class="col"><button class="b" data-a="no">Ne, popisek ponechat</button><button class="b danger" data-a="yes">Smazat popisek a pokračovat</button></div>`,
    async a => {
      if (a !== "yes") return;
      await run(async () => {
        const before = snap(p);
        await saveCaption(p, "");
        $("ta").value = ""; draftClear(); S.fixText = "";
        Object.assign(p, await api("POST", "/api/photo/flags", { path: p.path, skip: true }));
        record({ t: "multi", list: [{ t: "caption", path: p.path, before, after: "" }, { t: "flags", path: p.path, before: { skip: false }, after: { skip: true } }] });
        await advance();
      });
    });
}
function markDelete() {
  const p = S.cur; if (!p) return;
  if (p.status === "del") { setFlags({ markedDelete: false }, { markedDelete: true }); return; }
  setFlags({ markedDelete: true }, { markedDelete: false }, async () => {
    await advance();
    toast("Fotka je označená ke smazání. Smaže se až při zavření programu.", null, 4000);
  });
}
function rotate() { const p = S.cur; if (p) setFlags({ rotation: (p.rotation + 90) % 360 }, { rotation: p.rotation }); }
async function clearCaption() {
  const p = S.cur; if (!p || !$("ta").value || S.busy) return;
  await run(async () => {
    // i neuložený (právě napsaný) text jde tlačítkem Zpět vrátit
    const before = isDirty() ? { c: $("ta").value, s: "manual" } : snap(p);
    await saveCaption(p, "");
    $("ta").value = ""; draftClear();
    record({ t: "caption", path: p.path, before, after: "" });
  });
  toast("Popisek je smazaný. Vrátíte ho tlačítkem Zpět.", null, 3500);
}
function openMenu() {
  const p = S.cur, m = $("menu"); S.menu = true; $("more").setAttribute("aria-expanded", "true");
  m.innerHTML = `<button data-a="rot"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 12a8 8 0 11-3-6.3"/><path d="M20 4v5h-5"/></svg>Otočit fotku</button>
  <button data-a="next"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M5 5l7 7-7 7M13 5l7 7-7 7"/></svg>Přejít na další nepopsanou</button>
  <button data-a="del" class="danger"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M4 7h16M6 7l1 13h10l1-13M9 7V4h6v3"/></svg>${p.status === "del" ? "Vrátit, nemazat fotku" : "Smazat fotku"}</button>`;
  m.hidden = false; m.querySelector("button").focus();
  m.onclick = e => { const b = e.target.closest("button"); if (b) ({ rot: rotate, next: () => { closeMenu(); nextUndone(); }, del: markDelete })[b.dataset.a](); };
}
function closeMenu() { if (!S.menu) return; S.menu = false; $("menu").hidden = true; $("more").setAttribute("aria-expanded", "false"); }

/* ---------- diktování a korektura (specifikace 3) ---------- */
const R = { on: false, stream: null, ctx: null, proc: null, buf: [], len: 0, voicedSec: 0, lastVoice: 0, started: 0, timer: null, queue: Promise.resolve(), pending: 0, mode: "append", target: null, levels: [] };
function fmtTime(ms) { const s = Math.floor(ms / 1000); return Math.floor(s / 60) + ":" + String(s % 60).padStart(2, "0"); }

async function loadAi() { try { S.ai = await api("GET", "/api/ai"); } catch { S.ai = null; } renderButtons(); }

async function recStart(receive = null) {
  const p = S.cur;
  if ((!p && !receive) || R.on || R.starting || R.pending || S.busy) return false;
  R.starting = true;
  try { R.stream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true, channelCount: 1 } }); }
  catch { toast("Mikrofon není dostupný. Je připojený a povolený?", null, 6000); return false; }
  finally { R.starting = false; }
  if ((receive && ($("work").hidden || S.view === "work")) || (!receive && (p !== S.cur || S.view !== "work" || $("work").hidden))) {
    R.stream.getTracks().forEach(track => track.stop());
    return false;
  }
  R.receive = receive;
  R.mode = !receive && p.source === "ai" && !isDirty() && S.saved.trim() ? "fix" : "append";
  if (R.mode === "fix") S.fixText = "";
  R.target = p; R.on = true; R.buf = []; R.len = 0; R.voicedSec = 0; R.levels = []; R.started = R.lastVoice = performance.now();
  R.ctx = new AudioContext();
  const src = R.ctx.createMediaStreamSource(R.stream);
  R.proc = R.ctx.createScriptProcessor(4096, 1, 1);
  R.proc.onaudioprocess = e => onAudio(e.inputBuffer.getChannelData(0));
  src.connect(R.proc); R.proc.connect(R.ctx.destination);
  // pojistka: po minutě ticha (nebo po 10 minutách) se nahrávání zastaví samo
  R.timer = setInterval(() => {
    const now = performance.now();
    if (now - R.lastVoice > 60000 || now - R.started > (receive ? 60000 : 600000)) recStop(); else if (!receive) { renderCaption(false); renderButtons(); }
  }, 500);
  renderAll(false);
  return true;
}
function onAudio(d) {
  if (!R.on) return;
  const copy = new Float32Array(d);
  let sum = 0; for (let i = 0; i < copy.length; i++) sum += copy[i] * copy[i];
  const now = performance.now(), rms = Math.sqrt(sum / copy.length);
  // hladina šumu místnosti = spodní pětina hlasitosti za posledních ~5 s; řeč musí být výrazně nad ní
  R.levels.push(rms); if (R.levels.length > 60) R.levels.shift();
  const floor = [...R.levels].sort((a, b) => a - b)[Math.floor(R.levels.length * 0.2)] || 0;
  if (rms > Math.max(0.015, floor * 3)) { R.lastVoice = now; R.voicedSec += copy.length / R.ctx.sampleRate; }
  R.buf.push(copy); R.len += copy.length;
  const sec = R.len / R.ctx.sampleRate;
  // úsek se odešle v pauze řeči (nebo nejpozději po 12 s), aby se nedělila slova
  if ((sec >= 3 && now - R.lastVoice > 700) || sec >= 12) flushSegment();
}
function flushSegment() {
  if (!R.len) return;
  const chunks = R.buf, len = R.len, voiced = R.voicedSec >= 0.5, rate = R.ctx.sampleRate, target = R.target, mode = R.mode, receive = R.receive;
  R.buf = []; R.len = 0; R.voicedSec = 0;
  if (!voiced) return; // ticho a šum se neposílají
  const wav = encodeWav(chunks, len, rate);
  R.pending++; renderCaption(false); renderButtons();
  R.queue = R.queue.then(async () => {
    try {
      const prev = receive ? receive.value : mode === "fix" ? (S.fixText || "") : $("ta").value;
      const r = await api("POST", "/api/ai/transcribe", { audio: wav, previous: prev });
      if (receive) {
        if (r.text && !isRepeat(r.text, receive.value)) {
          receive.value = [receive.value, r.text].filter(Boolean).join(" ");
          receive.update(receive.value);
        }
      } else if (r.text && S.cur === target && !isRepeat(r.text, mode === "fix" ? (S.fixText || "") : $("ta").value)) {
        appendDictation(r.text, mode);
      }
    } catch (e) { fail(e); }
    finally { R.pending--; renderCaption(false); renderButtons(); }
  });
}
/* ---------- schválení ---------- */
/** Schválit text (případně použít nadiktovaný pokyn) a přejít na další fotku – bez dotazu na korekturu. */
async function approveAndNext() {
  if (!S.cur || S.busy) return;
  if (R.on) await recStop();
  if (R.pending) await R.queue;
  if (S.fixText) await improveCaption();
  S.approving = true;
  try {
    const n = neighbour(1);
    if (n) await go(n);
    else { await run(commit); toast("Toto byla poslední fotka ve složce."); }
  } finally { S.approving = false; }
  renderAll(false);
}
/** Pojistka: přepis, který jen opakuje konec už napsaného textu, se nepřidá. */
function isRepeat(text, prev) {
  const norm = s => s.toLowerCase().replace(/[^\p{L}\p{N}]+/gu, " ").trim();
  const t = norm(text), p = norm(prev);
  return !t || (p.length > 0 && t.length >= 8 && p.endsWith(t));
}
function appendDictation(text, mode) {
  if (mode === "fix") S.fixText = (S.fixText ? S.fixText + " " : "") + text;
  else {
    const ta = $("ta");
    ta.value = (ta.value.trim() ? ta.value.replace(/\s+$/, "") + " " : "") + text;
    ta.scrollTop = ta.scrollHeight; // vždy vidět poslední napsané
    draftSave(S.cur.path, ta.value);
  }
  renderCaption(false); renderButtons();
}
async function recStop() {
  if (!R.on) return;
  R.on = false; clearInterval(R.timer);
  flushSegment();
  try { R.proc.disconnect(); } catch { }
  R.stream.getTracks().forEach(t => t.stop());
  try { await R.ctx.close(); } catch { }
  if (R.receive) R.receive.stopping?.();
  else renderAll(false);
  await R.queue;
  const receive = R.receive;
  R.receive = null;
  if (receive) receive.finish(receive.value);
  if (R.mode === "fix" && S.fixText) toast("Pokyn k úpravě je připravený. Stiskněte Použít úpravu.", null, 4000);
}
/** Zvuk → WAV 16 kHz mono 16 bit → base64. */
function encodeWav(chunks, len, rate) {
  const target = 16000, ratio = rate / target, outLen = Math.floor(len / ratio);
  const data = new Float32Array(len); let o = 0; for (const c of chunks) { data.set(c, o); o += c.length; }
  const buf = new ArrayBuffer(44 + outLen * 2), v = new DataView(buf);
  const w = (p, s) => { for (let i = 0; i < s.length; i++) v.setUint8(p + i, s.charCodeAt(i)); };
  w(0, "RIFF"); v.setUint32(4, 36 + outLen * 2, true); w(8, "WAVE"); w(12, "fmt "); v.setUint32(16, 16, true);
  v.setUint16(20, 1, true); v.setUint16(22, 1, true); v.setUint32(24, target, true); v.setUint32(28, target * 2, true);
  v.setUint16(32, 2, true); v.setUint16(34, 16, true); w(36, "data"); v.setUint32(40, outLen * 2, true);
  for (let i = 0; i < outLen; i++) {
    const a = Math.floor(i * ratio), b = Math.min(len, Math.floor((i + 1) * ratio));
    let s = 0; for (let j = a; j < b; j++) s += data[j]; s /= Math.max(1, b - a);
    v.setInt16(44 + i * 2, Math.max(-1, Math.min(1, s)) * 0x7fff, true);
  }
  const bytes = new Uint8Array(buf); let bin = "";
  for (let i = 0; i < bytes.length; i += 0x8000) bin += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
  return btoa(bin);
}
/** ✨ Korektura: z diktátu udělá pěkný popisek, nebo použije nadiktovaný pokyn k úpravě. Zpět vrátí původní text. */
async function improveCaption() {
  const p = S.cur; if (!p || S.busy || R.on) return;
  const typed = $("ta").value, fix = !!S.fixText;
  // opakovaná korektura (např. po změně délky v nastavení) vychází z původního diktátu
  const redo = !fix && p.source === "ai" && !isDirty();
  const dictation = redo ? (p.raw || typed) : typed;
  if (!fix && !dictation.trim()) { toast("Nejdřív nadiktujte nebo napište, co je na fotce."); return; }
  S.aiWorking = true; renderCaption(false);
  await run(async () => {
    try {
      const before = isDirty() ? { c: typed, s: "manual" } : snap(p);
      const r = fix ? await api("POST", "/api/ai/fix", { path: p.path, caption: typed, instruction: S.fixText })
        : await api("POST", "/api/ai/improve", { path: p.path, dictation });
      await saveCaption(p, r.caption, "ai", r.title, fix ? (p.raw || null) : dictation);
      $("ta").value = S.saved; draftClear(); S.fixText = "";
      record({ t: "caption", path: p.path, before, after: { c: r.caption, s: "ai", t: r.title } });
    } finally { S.aiWorking = false; }
  });
}
/** Před odchodem z fotky: text bez korektury → zeptat se (specifikace 2.7). Vrací true = lze pokračovat. */
function guardLeave() {
  return new Promise(async resolve => {
    if (R.on) await recStop();
    if (R.pending) await R.queue;
    if (S.approving) return resolve(true); // „Schválit a další“ = vědomé rozhodnutí, neptat se
    const p = S.cur;
    if (!p || !S.ai || !S.ai.hasKey) return resolve(true);
    const text = $("ta").value.trim();
    if (!S.fixText && !(isDirty() && text)) return resolve(true);
    const fix = !!S.fixText;
    modal(`<h3>${fix ? "Úprava ještě nebyla použita" : "Text ještě neprošel korekturou"}</h3>
      <p>${fix ? "Nadiktovaný pokyn k úpravě se zatím nepoužil." : "Chcete z něj nejdřív nechat udělat pěkný popisek?"}</p>
      <div class="col"><button class="b solid" data-a="ai">${fix ? "Použít úpravu" : "Udělat korekturu"}</button>
      <button class="b" data-a="leave">${fix ? "Nechat popisek beze změny" : "Ponechat jen ruční text"} a pokračovat</button>
      <button class="b" data-a="stay">Zůstat u této fotky</button></div>`,
      async a => {
        if (a === "ai") { await improveCaption(); resolve(false); }
        else if (a === "leave") { S.fixText = ""; resolve(true); }
        else resolve(false);
      });
  });
}

/* ---------- nastavení AI (správce) ---------- */
function aiSection() {
  const a = S.ai || {};
  return `<h2>Diktování a korektura (AI přes OpenRouter)</h2>
    <p>Klíč se ukládá šifrovaně jen v tomto počítači. Do cloudu jde jen zvuk diktátu a text, nikdy fotky.</p>
    <label>Klíč OpenRouter <input type="text" id="aiKey" autocomplete="off" placeholder="${a.hasKey ? "zadán (" + esc(a.keyHint || "") + ") – nový klíč přepíše starý" : "sk-or-…"}"></label>
    <div class="row"><button class="b small solid" id="aiKeySave">Uložit klíč</button>${a.hasKey ? `<button class="b small" id="aiKeyDel">Odebrat klíč</button>` : ""}</div>
    <label>Model pro korekturu <select id="aiText"><option value="">Výchozí (${esc(a.defaultTextModel || "")})</option></select></label>
    <label>Model pro přepis řeči <select id="aiAudio"><option value="">Automaticky (nejvhodnější dostupný)</option></select></label>
    <label>Slovníček jmen a míst <textarea id="aiGloss" rows="3" placeholder="např. Jarka, strýc Pepa, Ngorongoro, Masai Mara, Kilimandžáro">${esc(a.glossary || "")}</textarea></label>
    <div class="channels"><b>Délka popisku</b>
      ${[["kratky", "Krátký (jedna věta)"], ["stredni", "Střední (1–3 věty)"], ["delsi", "Delší (3–5 vět)"]].map(([v, l]) =>
        `<label class="toggle" style="font-weight:400"><input type="radio" name="aiLen" value="${v}" ${a.captionLength === v ? "checked" : ""}> ${l}</label>`).join("")}</div>
    <div class="row"><button class="b small solid" id="aiSave">Uložit nastavení AI</button></div>
    <p>Zatím utraceno za AI: <b>${(a.costUsd || 0).toFixed(3)} $</b></p>`;
}
async function wireAiSection() {
  const a = S.ai || {};
  const saveAi = async body => { S.ai = await api("POST", "/api/ai/settings", body); renderButtons(); };
  $("aiKeySave").onclick = async () => {
    const k = $("aiKey").value.trim(); if (!k) { toast("Vložte klíč OpenRouter."); return; }
    try { await saveAi({ key: k }); toast("Klíč je uložený."); openSettings(); } catch (e) { fail(e); }
  };
  if ($("aiKeyDel")) $("aiKeyDel").onclick = async () => { try { await saveAi({ key: "" }); toast("Klíč je odebraný."); openSettings(); } catch (e) { fail(e); } };
  $("aiSave").onclick = async () => {
    const len = document.querySelector("[name=aiLen]:checked");
    try {
      await saveAi({ textModel: $("aiText").value, audioModel: $("aiAudio").value, glossary: $("aiGloss").value, captionLength: len ? len.value : null });
      toast("Nastavení AI je uložené.");
    } catch (e) { fail(e); }
  };
  try {
    const m = await api("GET", "/api/ai/models");
    const fill = (sel, list, chosen) => {
      for (const x of list) {
        const o = document.createElement("option"); o.value = x.id;
        o.textContent = `${x.name}${x.completionPricePerM != null ? ` – ${x.promptPricePerM.toFixed(2)} / ${x.completionPricePerM.toFixed(2)} $ za 1M tokenů` : ""}`;
        sel.append(o);
      }
      if (chosen && ![...sel.options].some(o => o.value === chosen)) { const o = document.createElement("option"); o.value = o.textContent = chosen; sel.append(o); }
      sel.value = chosen || "";
    };
    fill($("aiText"), m.text, a.textModel); fill($("aiAudio"), m.audio, a.audioModel);
  } catch { toast("Seznam modelů se nepodařilo načíst (není připojení k internetu?).", null, 5000); }
}

/* ---------- zavření programu ---------- */
async function requestClose() {
  try {
    if (!(await guardLeave())) return;
    if (S.view !== "work" && !confirmIntroLeave()) return;
    await commit();
    const marked = S.state && S.state.configured ? await api("GET", "/api/trash") : [];
    if (!marked.length) return quit(null);
    const n = marked.length, word = n === 1 ? "fotka" : n < 5 ? "fotky" : "fotek";
    const albums = new Map();
    marked.forEach(m => { if (!albums.has(m.album)) albums.set(m.album, []); albums.get(m.album).push(m); });
    const groups = [...albums].map(([album, items]) => `<div class="tgroup"><div class="talbum">${esc(album)}</div><div class="tthumbs">${
      items.map(m => `<img alt="${esc(m.name)}" title="${esc(m.name)}" src="/api/thumb?size=200&path=${q(m.path)}">`).join("")}</div></div>`).join("");
    modal(`<h3>${n === 1 ? "Označena" : "Označeno"} ${n} ${word} ke smazání</h3>
      <div class="trash">${groups}</div>
      <p>Fotky se přesunou do koše Windows. Odtud je jde ještě obnovit.</p>
      <div class="col"><button class="b danger" data-a="bin">Přesunout do koše a zavřít</button>
      <button class="b" data-a="keep">Ponechat fotky a zavřít</button>
      <button class="b" data-a="back">Zpět do programu</button></div>`,
      a => { if (a === "bin") quit(true); else if (a === "keep") quit(false); });
  } catch (e) { fail(e); }
}
async function quit(moveToBin) {
  modal(`<h3><span class="spin dark"></span>Ukládám a zavírám…</h3>`, () => {});
  try {
    await api("POST", "/api/quit", { moveMarkedToBin: moveToBin });
    S.closed = true; clearTimeout(pollT);
    if (S.host !== "pc") modal(`<h3>Fotokronika je zavřená</h3><p>Vše je uložené. Okno prohlížeče můžete zavřít.</p>`, () => {});
  } catch (e) { $("modalHost").innerHTML = ""; fail(e); }
}
/** Skryté nastavení pro správce (Ctrl+Alt+S nebo menu ikony u hodin). */
async function openSettings() {
  if (S.view !== "work" && !confirmIntroLeave()) return;
  if (R.on && R.receive) await recStop();
  stopAutoplay();
  try { const r = await api("GET", "/api/state"); S.state = r.state; renderApp(r.app); } catch { }
  try { S.ai = await api("GET", "/api/ai"); } catch { }
  closeMenu(); $("modalHost").innerHTML = "";
  showSetup(false);
}
window.fotoalbum = {
  requestClose,
  showSettings: openSettings,
  // volá Windows aplikace po zkopírování fotek z karty – strom složek se obnoví
  refreshFolders: () => { if (S.state && S.state.configured && !$("work").hidden) loadTree().then(() => { if (S.view !== "work") loadAlbums(); }).then(() => toast("Z karty přibyly nové fotky – najdete je ve stromu složek.")).catch(() => {}); },
};

/* ---------- události ---------- */
$("prev").onclick = $("pL").onclick = () => step(-1);
$("next").onclick = $("pR").onclick = () => step(1);
$("skip").onclick = skipPhoto;
$("mic").onclick = () => R.on ? recStop() : recStart();
$("ok").onclick = approveAndNext;
$("ai").onclick = async () => { if (R.on) await recStop(); if (R.pending) await R.queue; if ($("ta").value.trim() || S.fixText) improveCaption(); };
$("rawBtn").onclick = () => { S.showRaw = !S.showRaw; renderCaption(false); };
$("useRaw").onclick = () => { const p = S.cur; if (!p || !p.raw) return; $("ta").value = p.raw; S.showRaw = false; draftSave(p.path, p.raw); renderCaption(false); renderButtons(); toast("Původní text je v poli. Můžete ho upravit a znovu dát Korekturu."); };
$("nextUndone").onclick = nextUndone;
$("undo").onclick = undo;
$("redo").onclick = redo;
$("clear").onclick = clearCaption;
$("more").onclick = e => { e.stopPropagation(); S.menu ? closeMenu() : openMenu(); };
document.addEventListener("click", e => {
  if (S.menu && !e.target.closest(".morewrap")) closeMenu();
  if (!e.target.closest("#folderMenu")) $("folderMenu").hidden = true;
});
$("tree").addEventListener("scroll", () => { $("folderMenu").hidden = true; });
$("closeBtn").onclick = requestClose;
$("gearBtn").onclick = () => { if (S.state && S.state.configured) openSettings(); };
$("sideBtn").onclick = () => { S.side = !S.side; $("work").classList.toggle("nosidebar", !S.side); requestAnimationFrame(() => { fitImage(); drawTrack(); }); };
function setPhotoFilter(value) {
  S.photoFilter = value; S.keep.clear(); stripKey = "";
  document.querySelectorAll("#photoFilter button").forEach(b => b.setAttribute("aria-pressed", b.dataset.f === value));
  renderAll(true);
}
document.querySelectorAll("#photoFilter button").forEach(b => b.onclick = () => setPhotoFilter(b.dataset.f));
$("folderSort").value = S.sort;
$("folderSort").onchange = e => {
  S.sort = e.target.value;
  try { localStorage.setItem("fotoalbum-razeni", S.sort); } catch { }
  renderTree();
  if (S.view !== "work" && !B.reader) renderLibrary();
};
$("ta").addEventListener("input", () => {
  if (!S.cur) return;
  draftSave(S.cur.path, $("ta").value); // jen pojistka pro nečekané zavření, neukládá se do fotky
  renderCaption(false); renderButtons();
});
document.addEventListener("keydown", e => {
  if (e.defaultPrevented || $("modalHost").innerHTML) return;
  if (e.key === "Escape") {
    if (!$("folderMenu").hidden) { $("folderMenu").hidden = true; return; }
    closeMenu();
    // Esc zavře nastavení a vrátí do Pracovny
    if (!$("setup").hidden && S.state && S.state.configured && $("back")) $("back").click();
    // Esc ve čtení (mimo celou obrazovku) = zpět na alba
    else if (S.view !== "work" && typeof B !== "undefined" && B.reader && !B.intro && !document.fullscreenElement) $("readerBack")?.click();
    return;
  }
  // Ctrl+Alt+S – na české klávesnici je Ctrl+Alt = AltGr a e.key je „đ“, proto podle fyzické klávesy
  if (e.ctrlKey && e.altKey && e.code === "KeyS") { e.preventDefault(); openSettings(); return; }
  if ($("work").hidden) return;
  if (S.view !== "work") { readerKey(e); return; }
  if (e.key === "F2") { e.preventDefault(); if (S.ai && S.ai.hasKey) (R.on ? recStop() : recStart()); return; }
  if (e.key === "PageUp") { e.preventDefault(); step(-1); return; }
  if (e.key === "PageDown") { e.preventDefault(); step(1); return; }
  if (/TEXTAREA|INPUT|SELECT/.test(e.target.tagName)) return;
  if (e.key === "ArrowLeft") { e.preventDefault(); step(-1); }
  else if (e.key === "ArrowRight") { e.preventDefault(); step(1); }
  else if (e.key === "b" || e.key === "B") skipPhoto();
  else if (e.ctrlKey && (e.key === "z" || e.key === "Z")) { e.preventDefault(); undo(); }
  else if (e.ctrlKey && (e.key === "y" || e.key === "Y")) { e.preventDefault(); redo(); }
});
window.addEventListener("beforeunload", () => {
  const p = S.cur;
  if (p && isDirty()) api("PUT", "/api/photo/caption", { path: p.path, caption: $("ta").value, isDraft: false }, true).catch(() => {});
});
new ResizeObserver(() => { fitImage(); drawTrack(); }).observe($("stage"));
setInterval(updatePending, 15000);


/* ---------- měnitelná šířka panelů ---------- */
function applyPanelWidths() {
  for (const [property, value] of [["--folders", S.state.folderPanelWidth], ["--thumbnails", S.state.thumbnailPanelWidth]]) {
    if (value == null) $("work").style.removeProperty(property);
    else $("work").style.setProperty(property, value + "px");
  }
}
function wireSplitter(gripId, panelId, panel, property, stateKey, direction) {
  const grip = $(gripId), target = $(panelId), work = $("work");
  let drag = null;
  function limit(width) {
    return Math.round(Math.max(140, Math.min(640, work.clientWidth * (direction === 1 ? 0.28 : 0.27), width)));
  }
  function resize(width) {
    work.style.setProperty(property, width + "px");
    grip.setAttribute("aria-valuenow", target.clientWidth);
  }
  async function save(width) {
    S.state[stateKey] = width;
    applyPanelWidths();
    grip.setAttribute("aria-valuenow", target.clientWidth);
    try { await api("PUT", "/api/settings/panel", { panel, width }); }
    catch (e) { fail(e); }
  }
  grip.setAttribute("aria-valuemin", 140);
  grip.setAttribute("aria-valuemax", 640);
  new ResizeObserver(() => grip.setAttribute("aria-valuenow", target.clientWidth)).observe(target);
  grip.addEventListener("pointerdown", e => {
    if (e.button !== 0 || drag) return;
    drag = { id: e.pointerId, x: e.clientX, width: target.getBoundingClientRect().width,
      zoom: target.getBoundingClientRect().width / target.offsetWidth, next: null };
    grip.setPointerCapture(e.pointerId);
    grip.classList.add("dragging");
    document.body.classList.add("resizing");
  });
  grip.addEventListener("pointermove", e => {
    if (drag?.id !== e.pointerId || Math.abs(e.clientX - drag.x) < 2 && drag.next === null) return;
    drag.next = limit((drag.width + direction * (e.clientX - drag.x)) / drag.zoom);
    resize(drag.next);
  });
  function finish(e, cancelled) {
    if (drag?.id !== e.pointerId) return;
    const width = drag.next;
    drag = null;
    grip.classList.remove("dragging");
    document.body.classList.remove("resizing");
    if (cancelled) applyPanelWidths();
    else if (width !== null) save(width);
  }
  grip.addEventListener("pointerup", e => finish(e, false));
  grip.addEventListener("pointercancel", e => finish(e, true));
  grip.addEventListener("lostpointercapture", e => finish(e, true));
  grip.addEventListener("dblclick", () => save(null));
  grip.addEventListener("keydown", e => {
    if (!["ArrowLeft", "ArrowRight", "Home"].includes(e.key)) return;
    e.preventDefault();
    save(e.key === "Home" ? null : limit(target.offsetWidth + direction * (e.key === "ArrowRight" ? 16 : -16)));
  });
}
wireSplitter("folderGrip", "folderPanel", "folders", "--folders", "folderPanelWidth", 1);
wireSplitter("thumbGrip", "rail", "thumbnails", "--thumbnails", "thumbnailPanelWidth", -1);
