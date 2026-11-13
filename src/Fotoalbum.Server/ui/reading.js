"use strict";
/* Fotokronika – Čtení (spec. 2.11, 2.13, 2.14): dovolené jako dlaždice, album hned na celé okno, vyhledávání i hlasem. */

const B = {
  albums: null, loadId: 0, reader: null, captioned: true, captionVisible: true, autoplay: null,
  intent: null, queryId: 0, searchTimer: null, intro: null, loading: false,
  index: null, indexAt: 0, indexLoading: null, status: null, statusTimer: null,
};

function updateView() {
  const active = !$("work").hidden, work = S.view === "work";
  $("workActions").hidden = !active || !work;
  $("viewTabs").hidden = !active;
  $("albumSearch").hidden = !active || work || !!B.reader;
  $("workspace").hidden = $("rail").hidden = $("thumbGrip").hidden = !work;
  $("library").hidden = work;
  $("work").classList.toggle("browse", !work);
  // otevřené album zabere celé okno programu (bez záhlaví a stromu složek)
  $("app").classList.toggle("reading-full", !work && !!(B.reader || B.intro));
  for (const [view, id] of [["work", "workView"], ["reading", "readingView"]]) {
    const tab = $(id);
    if (view === S.view) tab.setAttribute("aria-current", "page");
    else tab.removeAttribute("aria-current");
  }
  $("searchMic").disabled = !(R.on && R.receive) && (!S.ai?.hasKey || !navigator.onLine || R.pending > 0);
  $("searchMic").title = $("searchMic").disabled ? "Diktování vyžaduje připojení a nastavený klíč AI" : "Diktovat hledání";
  renderTree();
}

function resetLibrary() {
  stopAutoplay();
  ++B.loadId;
  B.loading = false;
  B.reader = B.intro = null;
  clearSearch();
}

async function showView(view) {
  if (S.busy || S.aiWorking || R.starting || !confirmIntroLeave()) return;
  if (S.view === view && !B.reader) return;
  if (document.fullscreenElement) await document.exitFullscreen();
  if (R.on) await recStop();
  if (S.view === "work") {
    if (!(await guardLeave())) return;
    await commit();
    closeMenu();
  }
  stopAutoplay();
  S.view = view;
  resetLibrary();
  updateView();
  if (view === "work") { renderAll(false); stopStatus(); }
  else { await loadAlbums(); watchStatus(); }
}

/** Dlaždice dovolených – jen údaje z databáze, je to okamžité. */
async function loadAlbums() {
  const id = ++B.loadId;
  B.loading = true;
  if (!B.reader && !B.intro) renderLibrary();
  try {
    const albums = await api("GET", "/api/reading/albums");
    if (id !== B.loadId) return;
    B.albums = albums;
  } catch (error) {
    if (id === B.loadId) fail(error);
  } finally {
    if (id === B.loadId) { B.loading = false; if (!B.reader && !B.intro) renderLibrary(); }
  }
}

/* ---------- stav procházení knihovny pro vyhledávání ---------- */
function watchStatus() {
  stopStatus();
  const tick = async () => {
    try {
      const before = B.status;
      B.status = await api("GET", "/api/index/status");
      if (before && before.indexed !== B.status.indexed) { B.index = null; if (searchActive()) renderLibrary(); }
      else if (!before && !B.reader && !B.intro) renderLibrary();
    } catch { }
  };
  tick();
  B.statusTimer = setInterval(tick, 10000);
}
function stopStatus() { clearInterval(B.statusTimer); B.statusTimer = null; }
function statusNote() {
  const st = B.status;
  if (!st || !st.total || st.indexed >= st.total) return "";
  return `<p class="index-note" role="status">Připravuji vyhledávání: prošlo ${st.indexed.toLocaleString("cs")} z ${st.total.toLocaleString("cs")} fotek. Hledání zatím najde jen v prošlých fotkách.</p>`;
}
async function loadIndex() {
  if (B.index) return B.index;
  if (!B.indexLoading) B.indexLoading = api("GET", "/api/search/index").then(r => { B.index = r; return r; }).finally(() => { B.indexLoading = null; });
  return B.indexLoading;
}

function normalizeSearch(text) {
  return String(text ?? "").toLocaleLowerCase("cs").normalize("NFD").replace(/\p{M}/gu, "");
}

const searchStopWords = new Set("fotka fotky fotek fotografie ze z na v ve kde ukaz mi jsem a i u o jak se s zkus".split(" "));
function searchWords(text) { return normalizeSearch(text).match(/[a-z0-9]+/g) || []; }

function searchStem(word) {
  if (word === "kena" || word === "keni" || word === "kenu" || word === "kenou") return "ken";
  if (word.length < 4) return word;
  return word.replace(/(ami|emi|ovi|ove|ech|ich|ach|em|um|ou|y|a|e|i|u|o)$/u, "");
}

function seasonOf(month) {
  return month >= 3 && month <= 5 ? "jaro" : month >= 6 && month <= 8 ? "leto" : month >= 9 && month <= 11 ? "podzim" : "zima";
}
function makeSearch(query, intent) {
  const words = searchWords(query).filter(word => !searchStopWords.has(word));
  const years = words.filter(word => /^\d{4}$/.test(word));
  const seasonNames = { jaro: "jaro", jare: "jaro", leto: "leto", lete: "leto", leta: "leto", podzim: "podzim", podzimu: "podzim", zima: "zima", zime: "zima", zimy: "zima" };
  const seasons = words.map(word => seasonNames[word]).filter(Boolean);
  const terms = (intent ? (intent.terms || []).flatMap(searchWords) : words)
    .filter(word => !searchStopWords.has(word) && !/^\d{4}$/.test(word) && !seasonNames[word]);
  const from = intent?.from, to = intent?.to;
  return (text, date) => {
    const normalized = normalizeSearch(text), tokens = searchWords(normalized);
    const stems = new Set(tokens.map(searchStem));
    if (!terms.every(word => tokens.includes(word) || stems.has(searchStem(word)))) return false;
    const [year, month] = (date || "").slice(0, 10).split("-").map(Number);
    const season = month ? seasonOf(month) : null;
    const seasonYear = season === "zima" && month < 3 ? year - 1 : year;
    if (from || to) {
      if (!date || (from && date.slice(0, 10) < from) || (to && date.slice(0, 10) > to)) return false;
    } else {
      if (years.length && !years.some(value => (seasons.length ? seasonYear : year) === Number(value) || (!date && tokens.includes(value)))) return false;
      if (seasons.length && !seasons.some(value => season === value || (!date && tokens.includes(value)))) return false;
    }
    return terms.length > 0 || years.length > 0 || seasons.length > 0 || !!from || !!to;
  };
}

function searchActive() { return !!$("searchQuery").value.trim(); }
function clearSearch() {
  $("searchQuery").value = "";
  B.intent = null;
  clearTimeout(B.searchTimer);
  ++B.queryId;
}
function queueSearch() {
  B.intent = null;
  ++B.queryId;
  clearTimeout(B.searchTimer);
  B.searchTimer = setTimeout(renderLibrary, 130);
}
async function interpretSearch(query, id) {
  if (!S.ai?.hasKey || !navigator.onLine || query.trim().split(/\s+/).length < 2) return;
  try {
    const intent = await api("POST", "/api/ai/search", { query });
    if (B.queryId !== id || query !== $("searchQuery").value || !intent
      || !(intent.terms?.length || intent.from || intent.to)) return;
    B.intent = intent;
    renderLibrary();
  } catch (error) { if (B.queryId === id) fail(error); }
}

async function dictateSearch() {
  if (R.on && R.receive) return recStop();
  const input = $("searchQuery"), button = $("searchMic");
  if (!(await recStart({
    value: "",
    update(text) {
      input.value = text.slice(0, input.maxLength);
      queueSearch();
    },
    stopping() { button.textContent = "Přepisuji…"; button.disabled = true; },
    finish() {
      button.classList.remove("recording");
      button.textContent = "🎤";
      button.setAttribute("aria-label", "Diktovat hledání");
      updateView();
      if (input.value.trim()) interpretSearch(input.value, B.queryId);
    },
  }))) return;
  clearSearch();
  renderLibrary();
  button.classList.add("recording");
  button.textContent = "■ Zastavit";
  button.setAttribute("aria-label", "Zastavit diktování hledání");
}

const photoTitle = photo => photo.title || photo.name || photo.path.split("/").pop();
const albumTitle = album => album.name || "Všechny fotky";
const folderOf = path => path.includes("/") ? path.slice(0, path.lastIndexOf("/")) : "";
function periodText(from, to) {
  if (!from) return "";
  return from.slice(0, 10) === (to || from).slice(0, 10) ? fmtDate(from) : fmtDate(from) + " – " + fmtDate(to);
}
function cardHtml({ path, photo, cover, rotation, title, sub }) {
  return `<button class="album-card" data-album="${esc(path)}"${photo ? ` data-photo="${esc(photo)}"` : ""}>
    <figure>${cover ? `<img loading="lazy" alt="" src="/api/thumb?size=640&amp;path=${q(cover)}" style="transform:rotate(${rotation || 0}deg)">` : '<span class="nocover">🌴</span>'}</figure>
    <strong>${esc(title)}</strong>${sub ? `<small>${esc(sub)}</small>` : ""}</button>`;
}

function renderLibrary() {
  if (S.view === "work" || B.reader || B.intro) return;
  const el = $("library"), query = $("searchQuery").value.trim();
  const head = `<div class="library-head"><h1>${query ? "Hledání" : "Dovolené"}</h1></div>`;
  if (!query) {
    const albums = sortFolders(B.albums || [], a => a.name, a => a.from);
    el.innerHTML = head + (B.loading && !B.albums ? '<p role="status">Načítám dovolené…</p>'
      : !B.albums ? '<p>Dovolené se nepodařilo načíst.</p><button class="b small" id="retryAlbums">Zkusit znovu</button>'
      : !albums.length ? '<p role="status">Zatím není žádná složka označená jako dovolená. Ve stromu vlevo klepněte na složku <b>pravým tlačítkem</b> a zvolte <b>Označit jako dovolenou</b>. Jakoukoli složku můžete také rovnou otevřít klepnutím ve stromu.</p>'
      : `<div class="album-grid">${albums.map(a => cardHtml({ path: a.path, cover: a.cover, rotation: a.coverRotation, title: a.name,
          sub: [periodText(a.from, a.to), a.count + " fotek"].filter(Boolean).join(" · ") })).join("")}</div>`);
    if ($("retryAlbums")) $("retryAlbums").onclick = loadAlbums;
    wireCards(el);
    return;
  }
  if (!B.index) {
    el.innerHTML = head + '<p role="status">Hledám…</p>';
    const id = B.queryId;
    loadIndex().then(() => { if (id === B.queryId || searchActive()) renderLibrary(); }).catch(fail);
    return;
  }
  const match = makeSearch(query, B.intent);
  const folders = new Map(B.index.folders.map(f => [f.path, f]));
  const byFolder = new Map();
  for (const p of B.index.photos) { const f = folderOf(p.path); if (!byFolder.has(f)) byFolder.set(f, []); byFolder.get(f).push(p); }
  const albumHits = [], photoHits = [];
  for (const f of folders.values()) {
    const text = [f.path, f.name, f.intro].join(" ");
    const photos = byFolder.get(f.path) || [];
    const albumByText = match(text, null);
    if (albumByText || photos.some(p => match(text, p.date))) albumHits.push(f);
    // fotky z alba nalezeného podle názvu zastupuje samo album; zvlášť se ukážou jen ty, které sedí i vlastním textem
    for (const p of photos)
      if (albumByText ? match([p.title, p.caption].join(" "), p.date) : match([text, p.title, p.caption].join(" "), p.date)) photoHits.push(p);
  }
  const shownPhotos = photoHits.slice(0, 300);
  el.innerHTML = head + statusNote() + (!albumHits.length && !photoHits.length ? '<p role="status">Hledání nic nenašlo.</p>'
    : (albumHits.length ? `<h2>Nalezená alba</h2><div class="album-grid">${sortFolders(albumHits, f => f.name).map(f => {
        const first = (byFolder.get(f.path) || [])[0];
        return cardHtml({ path: f.path, cover: first?.path, rotation: first?.rotation, title: f.name, sub: f.kind === "dovolena" ? "Dovolená" : "" });
      }).join("")}</div>` : "")
    + (photoHits.length ? `<h2>Nalezené fotky (${photoHits.length})</h2><div class="result-grid">${shownPhotos.map(p =>
        cardHtml({ path: folderOf(p.path), photo: p.path, cover: p.path, rotation: p.rotation, title: p.title || p.caption.slice(0, 60) || p.path.split("/").pop(),
          sub: [folders.get(folderOf(p.path))?.name, p.date ? fmtDate(p.date) : ""].filter(Boolean).join(" · ") })).join("")}</div>`
        + (photoHits.length > shownPhotos.length ? `<p>Zobrazeno prvních ${shownPhotos.length}. Upřesněte hledání.</p>` : "") : ""));
  wireCards(el);
}
function wireCards(el) {
  el.querySelectorAll("[data-album]").forEach(button => button.onclick = () => {
    const path = button.dataset.album;
    const meta = (B.albums || []).find(a => a.path === path) || B.index?.folders.find(f => f.path === path) || findNode(S.tree, path) || { path };
    openReader({ path, name: meta.name, intro: meta.intro || "" }, button.dataset.photo || undefined);
  });
}

/* ---------- čtení alba ---------- */
/** Otevře album na celé okno. Fotky se načtou ze serveru (u nového alba to může chvíli trvat). */
async function openReader(meta, photoPath) {
  stopAutoplay();
  if (document.fullscreenElement) await document.exitFullscreen().catch(() => {});
  const album = meta.photos ? meta : { path: meta.path, name: meta.name, intro: meta.intro || "", photos: null };
  B.reader = { album, photoPath: null, want: photoPath };
  B.returnFocus = document.activeElement?.dataset?.album ? { path: meta.path, photoPath } : null;
  updateView();
  renderReader();
  if (!album.photos) {
    const reader = B.reader;
    try {
      const r = await api("GET", "/api/reading/album?path=" + q(meta.path));
      if (B.reader !== reader) return;
      Object.assign(album, r);
    } catch (error) {
      if (B.reader === reader) { B.reader = null; updateView(); renderLibrary(); }
      fail(error); return;
    }
  }
  startReader();
}
function startReader() {
  const { album, want } = B.reader;
  if (!album.photos.some(item => item.caption.trim())) B.captioned = false;
  let photoPath = want;
  if (photoPath === undefined && !(album.intro || "").trim())
    photoPath = album.photos.find(item => !B.captioned || item.caption.trim())?.path ?? null;
  const photo = album.photos.find(item => item.path === photoPath);
  if (photo && !photo.caption.trim()) B.captioned = false;
  B.reader.photoPath = photo?.path ?? null;
  renderReader();
  $("reader").tabIndex = -1;
  $("reader").focus();
}

function readerPhotos() {
  return (B.reader.album.photos || []).filter(photo => !B.captioned || photo.caption.trim());
}
function readerStep(delta, wrap = false) {
  if (!B.reader.album.photos) return;
  const photos = readerPhotos();
  if (!photos.length) return;
  let index = photos.findIndex(photo => photo.path === B.reader.photoPath) + delta;
  if (wrap && index >= photos.length) index = -1;
  if (index < -1 || index >= photos.length) return;
  B.reader.photoPath = photos[index]?.path ?? null;
  renderReader();
}
function stopAutoplay() { clearInterval(B.autoplay); B.autoplay = null; }
function closeReader() {
  if (document.fullscreenElement) document.exitFullscreen().catch(fail);
  stopAutoplay(); B.reader = null; updateView(); renderLibrary();
  const focus = B.returnFocus;
  if (focus) [...$("library").querySelectorAll("[data-album]")]
    .find(button => button.dataset.album === focus.path && (button.dataset.photo || undefined) === focus.photoPath)?.focus();
}

function renderReader() {
  const { album, photoPath } = B.reader;
  const el = $("library");
  let reader = $("reader");
  if (!reader) {
    el.innerHTML = '<article class="reader" id="reader"></article>';
    reader = $("reader");
  }
  if (!album.photos) {
    reader.innerHTML = `<div class="reader-tools"><button class="b small solid" id="readerBack">← Zpět na alba</button>
      <strong class="reader-album">${esc(albumTitle(album))}</strong></div>
      <div class="reader-loading" role="status"><span class="spin dark"></span>Načítám fotky alba…<small>Při prvním otevření velkého alba to může chvíli trvat.</small></div>`;
    $("readerBack").onclick = closeReader;
    return;
  }
  if (!album.photos.length) {
    reader.innerHTML = `<div class="reader-tools"><button class="b small solid" id="readerBack">← Zpět na alba</button>
      <strong class="reader-album">${esc(albumTitle(album))}</strong></div><div class="reader-loading">V této složce nejsou žádné fotky.</div>`;
    $("readerBack").onclick = closeReader;
    return;
  }
  const photos = readerPhotos();
  const photo = photos.find(item => item.path === photoPath);
  const image = photo || album.photos[0];
  const overlay = S.state.readingLayout === "overlay";
  const position = photo ? "Fotka " + (photos.indexOf(photo) + 1) + " z " + photos.length : "Úvod alba";
  const focused = reader.contains(document.activeElement) ? document.activeElement.id : "";
  const sub = photo && folderOf(photo.path) !== album.path ? folderOf(photo.path).split("/").pop() : "";
  reader.innerHTML = `<div class="reader-tools">
    <button class="b small solid" id="readerBack">← Zpět na alba</button>
    <strong class="reader-album">${esc(albumTitle(album))}</strong>
    <label>Vzhled <select id="readerLayout"><option value="book">Kniha</option><option value="overlay">Fotka přes celou plochu</option></select></label>
    <label class="toggle"><input id="captionOnly" type="checkbox" ${B.captioned ? "checked" : ""}> Jen fotky s popiskem</label>
    <button class="b small" id="autoPlay" ${photos.length ? "" : "disabled"}>${B.autoplay ? "Zastavit přehrávání" : "Přehrávat samo"}</button>
    <button class="b small" id="fullScreen">${document.fullscreenElement === reader ? "Ukončit celou obrazovku" : "Celá obrazovka"}</button>
    ${overlay ? `<button class="b small" id="toggleCaption">${B.captionVisible ? "Skrýt popisek" : "Ukázat popisek"}</button>` : ""}
    ${!photo ? '<button class="b small" id="editIntro">Upravit úvod alba</button>' : ""}
  </div>
  <div class="reader-page ${overlay ? "overlay" : ""}" id="readerPage">
    <div class="reader-image ${image.rotation % 180 ? "rotated" : ""}"><img alt="${esc(photoTitle(image))}" src="/api/image?path=${q(image.path)}" style="transform:rotate(${image.rotation}deg)"></div>
    <div class="reader-caption" ${overlay && !B.captionVisible && photo ? "hidden" : ""}>
      <div>${esc(photo ? [albumTitle(album), sub, fmtDate(photo.date), position].filter(Boolean).join(" · ") : periodText(album.photos[0]?.date, album.photos.at(-1)?.date))}</div>
      <h2>${esc(photo ? photoTitle(photo) : albumTitle(album))}</h2>
      ${photo?.caption || (!photo && album.intro) ? `<p>${esc(photo ? photo.caption : album.intro)}</p>` : ""}
    </div>
  </div>
  <div class="reader-nav"><button class="b" id="readerPrev" ${photo ? "" : "disabled"}>← Předchozí</button>
    <span class="reader-status">${esc(!photo && !photos.length ? "Album nemá fotky s popiskem." : position)}</span>
    <button class="b" id="readerNext" ${photos.length && photo !== photos.at(-1) ? "" : "disabled"}>Další →</button></div>`;
  if (focused) $(focused)?.focus({ preventScroll: true });
  $("readerBack").onclick = closeReader;
  $("readerLayout").value = overlay ? "overlay" : "book";
  $("readerLayout").onchange = async event => {
    const layout = event.target.value;
    S.state.readingLayout = layout;
    renderReader();
    try { await api("PUT", "/api/reading/layout", { layout }); }
    catch (error) { fail(error); }
  };
  $("captionOnly").onchange = event => {
    B.captioned = event.target.checked;
    if (photo && !readerPhotos().includes(photo)) B.reader.photoPath = null;
    if (!readerPhotos().length) stopAutoplay();
    renderReader();
  };
  $("autoPlay").onclick = () => {
    if (B.autoplay) stopAutoplay();
    else B.autoplay = setInterval(() => readerStep(1, true), 5000);
    renderReader();
  };
  $("fullScreen").onclick = () => (document.fullscreenElement ? document.exitFullscreen() : reader.requestFullscreen()).catch(fail);
  if ($("toggleCaption")) $("toggleCaption").onclick = () => { B.captionVisible = !B.captionVisible; renderReader(); };
  if ($("editIntro")) $("editIntro").onclick = () => editAlbumIntro(album);
  $("readerPrev").onclick = () => readerStep(-1);
  $("readerNext").onclick = () => readerStep(1);
  $("readerPage").onclick = event => {
    const box = event.currentTarget.getBoundingClientRect();
    if (!window.getSelection().toString()) readerStep(event.clientX - box.left < box.width / 2 ? -1 : 1);
  };
}

function confirmIntroLeave() {
  if (!B.intro) return true;
  if (R.starting || R.on || R.pending || B.intro.busy) return false;
  if ($("introText").value !== B.intro.album.intro && !window.confirm("Zahodit neuložené změny úvodu?")) return false;
  const album = B.intro.album;
  B.intro = null;
  openReader(album);
  return true;
}

function editAlbumIntro(album) {
  stopAutoplay();
  if (document.fullscreenElement) document.exitFullscreen().catch(fail);
  B.intro = { album, busy: false };
  $("library").innerHTML = `<div class="intro-editor"><h1>Úvod alba ${esc(albumTitle(album))}</h1>
    <textarea id="introText" maxlength="12000" aria-label="Úvod alba">${esc(album.intro)}</textarea>
    <div class="row">
      ${S.ai?.hasKey ? '<button class="b small" id="introMic">🎤 Diktovat</button><button class="b small" id="introAi">Korektura</button>' : ""}
      <button class="b small solid" id="introSave">Uložit úvod</button><button class="b small" id="introBack">Zpět</button>
    </div></div>`;
  $("introText").focus();
  $("introBack").onclick = confirmIntroLeave;
  const runIntro = async work => {
    if (B.intro.busy || R.starting || R.on || R.pending) return;
    B.intro.busy = true;
    try { await work(); } catch (error) { fail(error); }
    finally { if (B.intro) B.intro.busy = false; }
  };
  $("introSave").onclick = () => runIntro(async () => {
    const intro = $("introText").value.trim();
    await api("PUT", "/api/reading/intro", { path: album.path, intro });
    album.intro = intro;
    B.intro = null;
    openReader(album);
  });
  if ($("introAi")) $("introAi").onclick = () => runIntro(async () => {
    const field = $("introText"), original = field.value;
    if (!original.trim()) return;
    const result = await api("POST", "/api/ai/improve", { path: album.photos[0].path, dictation: original });
    if (field.value === original) field.value = result.caption.slice(0, field.maxLength);
  });
  if ($("introMic")) $("introMic").onclick = async () => {
    if (B.intro.busy) return;
    if (R.on) return recStop();
    const button = $("introMic"), field = $("introText");
    if (await recStart({
      value: field.value.trim(),
      update(text) { field.value = text.slice(0, field.maxLength); },
      stopping() { button.textContent = "Přepisuji…"; button.disabled = true; },
      finish() { button.textContent = "🎤 Diktovat"; button.disabled = false; },
    })) button.textContent = "■ Zastavit";
  };
}

for (const [view, id] of [["work", "workView"], ["reading", "readingView"]])
  $(id).onclick = () => showView(view).catch(fail);
$("searchQuery").addEventListener("input", queueSearch);
$("searchQuery").addEventListener("keydown", event => {
  if (event.key === "Enter" && !R.on) interpretSearch(event.target.value, ++B.queryId);
});
$("searchClear").onclick = async () => {
  if (R.on && R.receive) await recStop();
  clearSearch();
  updateView(); renderLibrary(); $("searchQuery").focus();
};
$("searchMic").onclick = () => dictateSearch().catch(fail);
window.addEventListener("online", updateView);
window.addEventListener("offline", updateView);
document.addEventListener("fullscreenchange", () => { if (B.reader && !B.intro && B.reader.album.photos) renderReader(); });
boot().catch(fail);
