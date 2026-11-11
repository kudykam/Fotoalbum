"use strict";
/* Fotoalbum – Pracovna (etapa 1): složky, miniatury, velká fotka, ruční popisek, automatické ukládání. */

const $ = id => document.getElementById(id);
const S = {
  state: null, host: "web", canPick: false,
  tree: null, open: {}, folder: null, photos: [], cur: null,
  fUndone: false, fEvery: false, side: true, menu: false,
  saveTimer: null, saving: false, dirty: false, lastSaved: null,
  undo: new Map(), // path -> {stack:[], i}
};

/* ---------- API ---------- */
async function api(method, url, body) {
  const res = await fetch(url, {
    method, credentials: "same-origin",
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

/* ---------- start a nastavení ---------- */
async function boot() {
  try {
    const r = await api("GET", "/api/state");
    S.state = r.state; S.host = r.host; S.canPick = r.canPickFolder;
  } catch (e) {
    document.body.innerHTML = `<div class="setup"><div class="card"><h1>Fotoalbum</h1><p>${esc(e.message)}</p></div></div>`;
    return;
  }
  if (!S.state.configured) return showSetup(true);
  $("setup").hidden = true; $("work").hidden = false;
  await loadTree();
  const start = S.state.lastFolder ?? firstFolderWithPhotos(S.tree);
  if (start != null) await openFolder(start, true);
  updatePending();
}

function showSetup(first) {
  const st = S.state;
  $("work").hidden = true; const el = $("setup"); el.hidden = false;
  el.innerHTML = `<div class="card">
    <h1>${first ? "Vítejte ve Fotoalbu" : "Nastavení"}</h1>
    ${st.problem ? `<div class="note bad">${esc(st.problem)}</div>` : ""}
    <p>Vyberte složku, ve které jsou fotky. Aplikace v ní nic nemaže; popisky zapisuje přímo do fotek.</p>
    <label>Složka s fotkami
      <input type="text" id="rootIn" value="${esc(st.root || "")}" placeholder="např. D:\\Fotky" autocomplete="off"></label>
    <div class="row">${S.canPick ? `<button class="b small" id="pick">Vybrat složku…</button>` : ""}<button class="b small solid" id="saveRoot">Uložit a otevřít</button>
      ${first ? "" : `<button class="b small" id="back">Zpět</button>`}</div>
    <div class="note ${st.exifToolOk ? "ok" : "bad"}">${st.exifToolOk
      ? `Zápis popisků do fotek je připravený (ExifTool ${esc(st.exifToolVersion || "")}).`
      : "Chybí program ExifTool, bez něj se popisky nezapíší do fotek (ukládají se jen v aplikaci). Umístěte <b>exiftool.exe</b> do složky <b>exiftool</b> vedle aplikace."}</div>
    <label class="toggle" style="font-weight:400"><input type="checkbox" id="autoImp" ${st.autoImport ? "checked" : ""}> Po připojení karty z fotoaparátu automaticky zkopírovat nové fotky (do složek „rok + roční období“, např. „2026 léto“)</label>
    ${first ? "" : `<p style="font-size:15px">Data aplikace (databáze, náhledy) jsou v profilu uživatele. Záloha databáze se ukládá do složky <b>_fotoalbum\\zaloha</b> ve složce s fotkami.</p>`}
  </div>`;
  if (S.canPick) $("pick").onclick = async () => {
    try { const r = await api("POST", "/api/pick-folder"); if (r.folder) $("rootIn").value = r.folder; } catch (e) { fail(e); }
  };
  $("saveRoot").onclick = async () => {
    try {
      S.state = await api("POST", "/api/settings", { photoRoot: $("rootIn").value, autoImport: $("autoImp").checked });
      S.tree = null; S.folder = null; S.cur = null; S.photos = [];
      el.hidden = true; await boot();
    } catch (e) { fail(e); }
  };
  if (!first) $("back").onclick = () => { el.hidden = true; $("work").hidden = false; };
}

/* ---------- strom složek ---------- */
const ico = {
  folder: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 6h6l2 2h10v11H3z"/></svg>',
  open: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M6 9l6 6 6-6"/></svg>',
  shut: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M9 6l6 6-6 6"/></svg>',
  none: '<svg viewBox="0 0 24 24"></svg>',
};
async function loadTree() { S.tree = await api("GET", "/api/tree"); renderTree(); }
function firstFolderWithPhotos(n) {
  if (!n) return null;
  if (n.total > 0 && n.children.length === 0) return n.path;
  for (const c of n.children) { const f = firstFolderWithPhotos(c); if (f != null) return f; }
  return n.total > 0 ? n.path : null;
}
function findNode(n, path) { if (!n) return null; if (n.path === path) return n; for (const c of n.children) { const f = findNode(c, path); if (f) return f; } return null; }
function renderTree() {
  const el = $("tree"); el.innerHTML = "";
  const nodes = ownCount(S.tree) > 0 || !S.tree.children.length ? [S.tree] : S.tree.children;
  if (S.open[""] === undefined) S.open[""] = true;
  const mk = (f, host) => {
    const li = document.createElement("li");
    const has = f.children.length > 0;
    const b = document.createElement("button");
    b.className = "node"; b.setAttribute("aria-current", S.folder === f.path);
    b.innerHTML = (has ? (S.open[f.path] ? ico.open : ico.shut) : ico.none) + ico.folder +
      `<span class="nm">${esc(f.name || "Všechny fotky")}</span>${f.kind === "bezne" ? '<span class="kind">běžné</span>' : ""}<span class="cnt">${f.handled}/${f.total}</span>`;
    b.onclick = () => {
      if (has) S.open[f.path] = !S.open[f.path];
      if (ownCount(f) > 0 || !has) openFolder(f.path); else renderTree();
    };
    li.append(b);
    if (has && S.open[f.path]) { const ul = document.createElement("ul"); f.children.forEach(c => mk(c, ul)); li.append(ul); }
    host.append(li);
  };
  nodes.forEach(n => mk(n, el));
  renderKindRow();
}
function ownCount(f) { return f.total - f.children.reduce((a, c) => a + c.total, 0); }
function expandTo(path) { const parts = path.split("/"); for (let i = 1; i < parts.length; i++) S.open[parts.slice(0, i).join("/")] = true; }
function renderKindRow() {
  const n = S.folder != null ? findNode(S.tree, S.folder) : null; const el = $("kindRow");
  if (!n) { el.innerHTML = ""; return; }
  el.innerHTML = `<span>Druh této složky:</span><button data-k="cestovni" aria-pressed="${n.kind === "cestovni"}">cestovní</button><button data-k="bezne" aria-pressed="${n.kind === "bezne"}">běžné fotky</button>`;
  el.querySelectorAll("button").forEach(b => b.onclick = async () => {
    try { await api("POST", "/api/folder/kind", { path: S.folder, kind: b.dataset.k }); await refreshFolderMeta(); } catch (e) { fail(e); }
  });
}
async function refreshFolderMeta() {
  await loadTree();
  const r = await api("GET", "/api/folder?path=" + q(S.folder));
  const byPath = new Map(r.photos.map(p => [p.path, p]));
  S.photos = S.photos.map(p => byPath.get(p.path) || p);
  S.cur = S.cur && byPath.get(S.cur.path) || S.cur;
  stripKey = ""; renderAll(false);
}

/* ---------- složka a fotky ---------- */
async function openFolder(path, initial) {
  await flushSave(); closeMenu();
  if (S.cur) api("POST", "/api/photo/leave", { folder: S.folder, path: S.cur.path }).catch(() => {});
  S.folder = path; expandTo(path); renderTree();
  $("loading").hidden = false;
  try {
    const r = await api("GET", "/api/folder?path=" + q(path));
    S.photos = r.photos; stripKey = "";
    const last = r.lastPhoto && S.photos.find(p => p.path === r.lastPhoto);
    S.cur = last || null;
    renderAll(true);
    if (initial && last) toast("Pokračujete tam, kde jste minule skončili.", null, 3000);
    loadTree().catch(() => {});
  } catch (e) { fail(e); } finally { $("loading").hidden = true; }
}
function visible() {
  return S.photos.filter(p =>
    !(S.fUndone && (p.status === "done" || p.status === "skip") && p !== S.cur) &&
    !(S.fEvery && p.everyday && p !== S.cur));
}
function cur() { const v = visible(); if (!S.cur || !v.includes(S.cur)) S.cur = v[0] || null; return S.cur; }

async function go(p) {
  if (!p || p === S.cur) return;
  await flushSave(); closeMenu();
  const prev = S.cur;
  S.cur = p;
  if (prev) api("POST", "/api/photo/leave", { folder: S.folder, path: prev.path }).catch(() => {});
  api("POST", "/api/photo/position", { folder: S.folder, path: p.path }).catch(() => {});
  renderAll(true);
}
function step(d) { const v = visible(), i = v.indexOf(S.cur); if (v[i + d]) go(v[i + d]); }
function nextUndone() {
  const v = visible(), i = v.indexOf(S.cur);
  const n = v.slice(i + 1).find(p => p.status === "none") || v.find(p => p.status === "none");
  if (n) go(n); else toast("V této složce už nic nepopsaného není.");
}

/* ---------- vykreslení ---------- */
function renderAll(newPhoto) { renderStrip(); renderStage(newPhoto); renderCaption(newPhoto); renderButtons(); }

let stripKey = "", stripMap = new Map(), io = null;
function badgeFor(st) { return { done: ["done", "✓"], draft: ["draft", "✎"], skip: ["skip", "–"], del: ["del", "✕"] }[st] || ["", ""]; }
function renderStrip() {
  const el = $("strip"), v = visible(), p = cur();
  const key = S.folder + "|" + S.fUndone + "|" + S.fEvery + "|" + v.length;
  if (key !== stripKey) {
    stripKey = key; el.innerHTML = ""; stripMap.clear(); if (io) io.disconnect();
    io = new IntersectionObserver(es => es.forEach(e => {
      if (!e.isIntersecting) return;
      const img = e.target.querySelector("img"); const ph = stripMap.get(e.target);
      if (img.dataset.src) { img.src = img.dataset.src; delete img.dataset.src; }
      io.unobserve(e.target); void ph;
    }), { root: el, rootMargin: "0px 800px" });
    v.forEach(ph => {
      const b = document.createElement("button"); b.className = "thumb"; b.setAttribute("role", "listitem"); b.title = ph.name;
      b.innerHTML = `<img alt="${esc(ph.name)}" data-src="/api/thumb?size=240&path=${q(ph.path)}"><span class="badge"></span>`;
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
  const handled = all.filter(x => x.status === "done" || x.status === "skip").length;
  $("progress").innerHTML = p ? `<b>Fotka ${v.indexOf(p) + 1} z ${v.length}</b> · vyřízeno ${handled} z ${all.length}` : "";
  drawTrack();
  const a = [...stripMap.keys()].find(b => stripMap.get(b) === p); if (a) a.scrollIntoView({ block: "nearest", inline: "center" });
}
function drawTrack() {
  const cv = $("track"), v = visible(), p = S.cur, w = cv.clientWidth; if (!w) return;
  const dpr = window.devicePixelRatio || 1; cv.width = w * dpr; cv.height = 22 * dpr;
  const c = cv.getContext("2d"); c.scale(dpr, dpr);
  const cs = getComputedStyle(document.documentElement);
  const col = { done: "--accent", draft: "--warn", skip: "--skip", none: "--line", del: "--rec" };
  const n = v.length; if (!n) return; const sw = w / n;
  v.forEach((ph, i) => { c.fillStyle = cs.getPropertyValue(col[ph.status]).trim(); c.fillRect(i * sw, 4, Math.max(sw - (sw > 4 ? 1 : 0), 1), 14); });
  const i = v.indexOf(p); if (i >= 0) { c.fillStyle = cs.getPropertyValue("--ink").trim(); c.fillRect(i * sw + sw / 2 - 1.5, 0, 3, 22); }
}
$("track").onclick = e => {
  const v = visible(); if (!v.length) return; const r = e.currentTarget.getBoundingClientRect();
  go(v[Math.min(v.length - 1, Math.max(0, Math.floor((e.clientX - r.left) / r.width * v.length)))]);
};

function renderStage(newPhoto) {
  const p = cur(), st = $("stage"), img = $("photo");
  st.querySelectorAll(".mask,.empty").forEach(n => n.remove());
  if (!p) {
    img.hidden = true; $("meta").innerHTML = "";
    const e = document.createElement("div"); e.className = "empty";
    e.innerHTML = S.photos.length ? 'Filtr skryl všechny fotky v této složce.<button class="b small solid" id="showAll">Ukázat všechny fotky</button>'
      : "V této složce nejsou žádné fotky. Vyberte jinou složku vlevo.";
    st.append(e);
    const b = $("showAll"); if (b) b.onclick = () => { S.fUndone = S.fEvery = false; $("fUndone").checked = $("fEvery").checked = false; stripKey = ""; renderAll(true); };
    return;
  }
  img.hidden = false;
  if (newPhoto) { img.style.visibility = "hidden"; img.onload = () => { fitImage(); img.style.visibility = ""; }; img.src = "/api/image?path=" + q(p.path); }
  else fitImage();
  const n = findNode(S.tree, S.folder);
  $("meta").innerHTML = `<span class="chip">${esc(p.name)}</span>${p.date ? `<span class="chip">${fmtDate(p.date)}</span>` : ""}<span class="chip">${esc(n ? (n.name || "Všechny fotky") : "")}</span>`
    + (p.everyday ? '<span class="chip every">běžná fotka</span>' : "") + (p.status === "skip" ? '<span class="chip skipc">bez popisku</span>' : "");
  if (p.status === "del") {
    const m = document.createElement("div"); m.className = "mask";
    m.innerHTML = '<div class="box"><strong>Bude smazána</strong><span>Fotka se přesune do koše až při zavření programu.</span><button class="b small solid" id="undel">Vrátit, nemazat</button></div>';
    st.append(m); $("undel").onclick = () => setFlags({ markedDelete: false });
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

function undoState(p) { if (!S.undo.has(p.path)) S.undo.set(p.path, { stack: [p.caption], i: 0 }); return S.undo.get(p.path); }
function renderCaption(newPhoto) {
  const p = S.cur, ta = $("ta");
  ta.disabled = !p || p.status === "del";
  if (newPhoto || document.activeElement !== ta) ta.value = p ? p.caption : "";
  if (p) undoState(p);
  const m = $("mode");
  const st = p ? (S.dirty ? (ta.value.trim() ? "done" : "none") : p.status) : "none";
  m.className = "mode " + (st === "done" ? "done" : st === "draft" ? "draft" : "");
  m.textContent = { done: "Popisek", draft: "Rozepsáno", skip: "Bez popisku", del: "Ke smazání" }[st] || "Prázdný popisek";
  ta.placeholder = p && p.status === "skip" ? "Fotka je označená „bez popisku“. Když začnete psát, značka zmizí." : "Napište, co je na fotce. Ukládá se samo.";
  renderSave();
  const err = $("writeErr");
  err.hidden = !(p && p.writeError);
  if (p && p.writeError) err.textContent = "Do fotky se nepodařilo zapsat: " + p.writeError + " Popisek je uložený v aplikaci a zápis se zkusí znovu.";
}
function renderSave() {
  const s = $("save"), p = S.cur;
  if (!p) { s.textContent = ""; return; }
  const ok = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3"><path d="M5 12l5 5 9-10"/></svg>';
  if (S.dirty || S.saving) { s.className = "save"; s.textContent = "Ukládám…"; }
  else if (!p.caption && !p.writePending && p.status !== "done") { s.className = "save"; s.textContent = ""; }
  else if (p.writePending) { s.className = "save ok"; s.innerHTML = ok + "Uloženo"; }
  else { s.className = "save ok"; s.innerHTML = ok + "Uloženo do fotky"; }
}
function renderButtons() {
  const p = S.cur, v = visible(), i = v.indexOf(p);
  $("prev").disabled = $("pL").disabled = !p || i <= 0;
  $("next").disabled = $("pR").disabled = !p || i >= v.length - 1;
  const u = p ? undoState(p) : null, lock = !p || p.status === "del";
  $("undo").disabled = lock || !(u && u.i > 0);
  $("redo").disabled = lock || !(u && u.i < u.stack.length - 1);
  $("clear").disabled = lock || !$("ta").value;
  $("more").disabled = !p;
}

/* ---------- ukládání popisku ---------- */
$("ta").addEventListener("input", () => {
  if (!S.cur) return;
  S.dirty = true; renderCaption(false); renderButtons();
  clearTimeout(S.saveTimer); S.saveTimer = setTimeout(() => flushSave(), 700);
});
async function flushSave() {
  clearTimeout(S.saveTimer);
  const p = S.cur; if (!p || !S.dirty) return;
  const text = $("ta").value;
  S.dirty = false; S.saving = true; renderSave();
  try {
    const d = await api("PUT", "/api/photo/caption", { path: p.path, caption: text, isDraft: false });
    Object.assign(p, d);
    const u = undoState(p);
    if (u.stack[u.i] !== d.caption) { u.stack = u.stack.slice(0, u.i + 1); u.stack.push(d.caption); u.i = u.stack.length - 1; }
    pollWrite(p);
  } catch (e) { S.dirty = true; fail(e); }
  finally { S.saving = false; if (S.cur === p) { renderCaption(false); renderButtons(); } renderStrip(); }
}
let pollT;
function pollWrite(p) {
  clearTimeout(pollT);
  if (!p.writePending) return;
  pollT = setTimeout(async () => {
    try { const d = await api("GET", "/api/photo?path=" + q(p.path)); if (!S.dirty) Object.assign(p, d); if (S.cur === p) renderCaption(false); updatePending(); pollWrite(p); } catch { /* další pokus později */ }
  }, 2500);
}
async function setCaption(text, msg) {
  $("ta").value = text; S.dirty = true; await flushSave(); if (msg) toast(msg.text, msg.act);
}
async function undoRedo(d) {
  const p = S.cur; if (!p) return; const u = undoState(p);
  const ni = u.i + d; if (ni < 0 || ni >= u.stack.length) return;
  u.i = ni; $("ta").value = u.stack[ni]; S.dirty = true;
  clearTimeout(S.saveTimer); S.saving = true; S.dirty = false; renderSave();
  try { Object.assign(p, await api("PUT", "/api/photo/caption", { path: p.path, caption: u.stack[ni], isDraft: false })); pollWrite(p); }
  catch (e) { fail(e); } finally { S.saving = false; renderCaption(false); renderButtons(); renderStrip(); }
}
async function updatePending() {
  try { const r = await api("GET", "/api/state"); S.state = r.state; const n = r.state.pendingWrites; const el = $("pending"); el.hidden = !n; el.textContent = n ? `Zapisuji popisky do fotek (${n})…` : ""; } catch { }
}

/* ---------- příznaky a menu ---------- */
async function setFlags(f, msg) {
  const p = S.cur; if (!p) return;
  await flushSave(); closeMenu();
  try {
    Object.assign(p, await api("POST", "/api/photo/flags", { path: p.path, ...f }));
    renderAll(false); loadTree().catch(() => {});
    if (msg) toast(msg.text, msg.act, msg.ms);
  } catch (e) { fail(e); }
}
function toggleSkip() {
  const p = S.cur; if (!p) return;
  if (p.status !== "skip" && $("ta").value.trim()) { toast("Fotka už má popisek. Pro označení „bez popisku“ ho nejdřív smažte."); return; }
  const on = p.status !== "skip";
  setFlags({ skip: on }, on ? { text: "Fotka nepotřebuje popisek a počítá se jako vyřízená.", act: { label: "Vrátit", fn: () => setFlags({ skip: false }) }, ms: 3500 } : null);
}
function toggleDel() {
  const p = S.cur; if (!p) return;
  const on = p.status !== "del";
  setFlags({ markedDelete: on }, on ? { text: "Fotka je označená ke smazání. Do zavření programu jde vrátit.", act: { label: "Vrátit", fn: () => setFlags({ markedDelete: false }) } } : null);
}
function rotate() { const p = S.cur; if (p) setFlags({ rotation: (p.rotation + 90) % 360 }); }
function toggleEvery() {
  const p = S.cur; if (!p) return;
  const n = findNode(S.tree, S.folder); const folderEveryday = n && n.kind === "bezne";
  const want = !p.everyday;
  setFlags(want === folderEveryday ? { clearEveryday: true } : { everyday: want },
    { text: want ? "Fotka je označená jako běžná." : "Fotka je označená jako cestovní." });
}
function openMenu() {
  const p = S.cur, m = $("menu"); S.menu = true; $("more").setAttribute("aria-expanded", "true");
  m.innerHTML = `<button data-a="skip"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="9"/><path d="M8 12h8"/></svg>${p.status === "skip" ? "Přece jen popsat" : "Bez popisku"}<small><kbd>B</kbd></small></button>
  <button data-a="rot"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 12a8 8 0 11-3-6.3"/><path d="M20 4v5h-5"/></svg>Otočit fotku</button>
  <button data-a="ev"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M4 12h16M12 4v16"/></svg>${p.everyday ? "Označit jako cestovní" : "Označit jako běžnou fotku"}</button>
  <button data-a="del" class="danger"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M4 7h16M6 7l1 13h10l1-13M9 7V4h6v3"/></svg>${p.status === "del" ? "Vrátit, nemazat fotku" : "Smazat fotku"}</button>`;
  m.hidden = false; m.querySelector("button").focus();
  m.onclick = e => { const b = e.target.closest("button"); if (b) ({ skip: toggleSkip, rot: rotate, ev: toggleEvery, del: toggleDel })[b.dataset.a](); };
}
function closeMenu() { if (!S.menu) return; S.menu = false; $("menu").hidden = true; $("more").setAttribute("aria-expanded", "false"); }

/* ---------- zavření programu ---------- */
async function requestClose() {
  try {
    await flushSave();
    const marked = S.state && S.state.configured ? await api("GET", "/api/trash") : [];
    if (!marked.length) return quit(null);
    const n = marked.length, word = n === 1 ? "fotka" : n < 5 ? "fotky" : "fotek";
    modal(`<h3>Označeno ${n} ${word} ke smazání</h3>
      <div class="files">${marked.map(m => esc(m.path)).join("<br>")}</div>
      <p>Fotky se přesunou do koše Windows. Odtud je jde ještě obnovit.</p>
      <div class="col"><button class="b danger" data-a="bin">Přesunout do koše a zavřít</button>
      <button class="b" data-a="keep">Ponechat fotky a zavřít</button>
      <button class="b" data-a="back">Zpět do programu</button></div>`,
      a => { if (a === "bin") quit(true); else if (a === "keep") quit(false); });
  } catch (e) { fail(e); }
}
async function quit(moveToBin) {
  modal(`<h3><span class="spin" style="border-top-color:var(--accent);border-color:var(--line)"></span>Ukládám a zavírám…</h3>`, () => {});
  try {
    await api("POST", "/api/quit", { moveMarkedToBin: moveToBin });
    S.closed = true; clearInterval(pendingTimer); clearTimeout(pollT);
    if (S.host !== "pc") modal(`<h3>Fotoalbum je zavřené</h3><p>Vše je uložené. Okno prohlížeče můžete zavřít.</p>`, () => {});
  } catch (e) { $("modalHost").innerHTML = ""; fail(e); }
}
window.fotoalbum = {
  requestClose,
  // volá Windows aplikace po zkopírování fotek z karty – strom složek se obnoví
  refreshFolders: () => { if (S.state && S.state.configured && !$("work").hidden) loadTree().then(() => toast("Z karty přibyly nové fotky – najdete je ve stromu složek.")).catch(() => {}); },
};

/* ---------- události ---------- */
$("prev").onclick = $("pL").onclick = () => step(-1);
$("next").onclick = $("pR").onclick = () => step(1);
$("nextUndone").onclick = nextUndone;
$("undo").onclick = () => undoRedo(-1);
$("redo").onclick = () => undoRedo(1);
$("clear").onclick = () => {
  const p = S.cur; if (!p || !$("ta").value) return;
  setCaption("", { text: "Popisek je smazaný.", act: { label: "Vrátit", fn: () => undoRedo(-1) } });
};
$("more").onclick = e => { e.stopPropagation(); S.menu ? closeMenu() : openMenu(); };
document.addEventListener("click", e => { if (S.menu && !e.target.closest(".morewrap")) closeMenu(); });
$("closeBtn").onclick = requestClose;
$("sideBtn").onclick = () => { S.side = !S.side; $("work").classList.toggle("nosidebar", !S.side); requestAnimationFrame(() => { fitImage(); drawTrack(); }); };
$("fUndone").onchange = e => { S.fUndone = e.target.checked; stripKey = ""; renderAll(true); };
$("fEvery").onchange = e => { S.fEvery = e.target.checked; stripKey = ""; renderAll(true); };
$("stripL").onclick = () => $("strip").scrollBy({ left: -500 });
$("stripR").onclick = () => $("strip").scrollBy({ left: 500 });
document.addEventListener("keydown", e => {
  if ($("modalHost").innerHTML) return;
  if (e.key === "Escape") { closeMenu(); return; }
  if (e.ctrlKey && e.altKey && (e.key === "s" || e.key === "S")) { e.preventDefault(); showSetup(false); return; }
  if ($("work").hidden) return;
  if (e.key === "PageUp") { e.preventDefault(); step(-1); return; }
  if (e.key === "PageDown") { e.preventDefault(); step(1); return; }
  if (/TEXTAREA|INPUT|SELECT/.test(e.target.tagName)) return;
  if (e.key === "ArrowLeft") { e.preventDefault(); step(-1); }
  else if (e.key === "ArrowRight") { e.preventDefault(); step(1); }
  else if (e.key === "b" || e.key === "B") toggleSkip();
});
window.addEventListener("beforeunload", () => { if (S.dirty) flushSave(); });
new ResizeObserver(() => { fitImage(); drawTrack(); }).observe($("stage"));
const pendingTimer = setInterval(() => { if (!S.closed) updatePending(); }, 15000);
boot();
