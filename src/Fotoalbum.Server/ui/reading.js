"use strict";

const B = {
  albums: null, loadId: 0, folder: null, everyday: false, sort: "date",
  reader: null, captioned: true, captionVisible: true, autoplay: null,
  intent: null, queryId: 0, searchTimer: null, intro: null, loading: false,
};

function updateView() {
  const active = !$("work").hidden, work = S.view === "work";
  $("workActions").hidden = !active || !work;
  $("viewTabs").hidden = !active;
  $("albumSearch").hidden = !active || work || !!B.reader;
  $("workspace").hidden = $("rail").hidden = $("thumbGrip").hidden = !work;
  $("library").hidden = work;
  $("work").classList.toggle("browse", !work);
  for (const [view, id] of [["work", "workView"], ["library", "libraryView"], ["reading", "readingView"]]) {
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
  B.albums = null;
  B.loading = false;
  B.folder = B.reader = B.intro = null;
  S.browseOpen = new Set([""]);
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
  if (view === "work") renderAll(false);
  else await loadAlbums();
}

async function loadAlbums() {
  const id = ++B.loadId;
  B.loading = true;
  if (!B.reader && !B.intro) renderLibrary();
  try {
    const albums = await api("GET", "/api/reading/albums");
    if (id !== B.loadId) return;
    B.albums = albums;
    B.loading = false;
    if (!B.reader && !B.intro) renderLibrary();
  } catch (error) {
    if (id !== B.loadId) return;
    B.loading = false;
    if (!B.reader && !B.intro) renderLibrary();
    fail(error);
  }
}

function chooseBrowseFolder(path) {
  if (!confirmIntroLeave()) return;
  if (document.fullscreenElement) document.exitFullscreen().catch(fail);
  stopAutoplay();
  B.reader = B.intro = null;
  B.folder = path;
  if (path === null) S.browseOpen = new Set([""]);
  else expandTo(path, S.browseOpen, true);
  updateView();
  renderLibrary();
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

const photoTitle = photo => photo.title || photo.name;
const albumTitle = album => album.name || "Všechny fotky";
function albumPeriod(album) {
  const dates = album.photos.map(photo => photo.date).filter(Boolean);
  if (!dates.length) return "";
  return dates[0].slice(0, 10) === dates.at(-1).slice(0, 10)
    ? fmtDate(dates[0]) : fmtDate(dates[0]) + " – " + fmtDate(dates.at(-1));
}
function albumCard(album) {
  const cover = album.photos[0];
  const period = albumPeriod(album);
  return `<button class="album-card" data-album="${esc(album.path)}">
    <figure><img loading="lazy" alt="" src="/api/thumb?size=640&amp;path=${q(cover.path)}" style="transform:rotate(${cover.rotation}deg)"></figure>
    <strong>${esc(albumTitle(album))}</strong><small>${esc(period)}${period && S.view === "library" ? " · " : ""}${S.view === "library" ? Math.round(100 * album.handled / album.photos.length) + " % vyřízeno" : ""}</small>
    </button>`;
}
function photoCard(photo, album) {
  return `<button class="album-card" data-album="${esc(album.path)}" data-photo="${esc(photo.path)}">
    <figure><img loading="lazy" alt="" src="/api/thumb?size=320&amp;path=${q(photo.path)}" style="transform:rotate(${photo.rotation}deg)"></figure>
    <strong>${esc(photoTitle(photo))}</strong></button>`;
}

function renderLibrary() {
  if (S.view === "work" || B.reader || B.intro) return;
  const el = $("library"), reading = S.view === "reading", query = $("searchQuery").value.trim();
  const selected = findNode(S.tree, B.folder);
  const header = `<div class="library-head"><h1>${esc(selected?.name || (reading ? "Čtení" : "Knihovna alb"))}</h1>
    ${B.folder !== null ? '<button class="b small" id="allAlbums">Všechna alba</button>' : ""}
    ${reading ? `<label class="toggle"><input id="includeEveryday" type="checkbox" ${B.everyday ? "checked" : ""}> Ukázat i běžné fotky</label>`
      : `<button class="b small solid" id="resumeWork">Pokračovat${S.cur ? ": " + esc(findNode(S.tree, S.folder)?.name || "") + " · fotka " + (S.photos.indexOf(S.cur) + 1) + " z " + S.photos.length : " v Pracovně"}</button>`}
    <label>Řadit <select id="albumSort"><option value="date">Podle data</option><option value="name">Podle názvu</option></select></label></div>`;
  const albums = (B.albums || []).filter(album => (!reading || B.everyday || album.kind === "dovolena")
    && (B.folder === null || B.folder === "" || album.path === B.folder || album.path.startsWith(B.folder + "/")));
  const byName = (a, b) => albumTitle(a).localeCompare(albumTitle(b), "cs");
  albums.sort(B.sort === "name" ? byName : (a, b) => (b.photos[0].date || "").localeCompare(a.photos[0].date || "") || byName(a, b));
  const photos = [];
  let matches = albums;
  if (query) {
    const match = makeSearch(query, B.intent);
    matches = albums.filter(album => {
      const text = [album.path, album.name, album.intro].join(" ");
      for (const photo of album.photos) {
        if (match([text, photo.title, photo.caption].join(" "), photo.date)) photos.push(photoCard(photo, album));
      }
      return match(text, null) || album.photos.some(photo => match(text, photo.date));
    });
  } else if (B.folder !== null) {
    const album = albums.find(item => item.path === B.folder);
    if (album) photos.push(...album.photos.map(photo => photoCard(photo, album)));
  }
  const albumGrid = matches.length ? `<div class="album-grid">${matches.map(albumCard).join("")}</div>` : "";
  el.innerHTML = header + (B.loading && !B.albums ? '<p role="status">Načítám alba a popisky…</p>'
    : !B.albums ? '<p>Alba se nepodařilo načíst.</p><button class="b small" id="retryAlbums">Zkusit znovu</button>'
    : !matches.length && !photos.length ? `<p role="status">${query ? "Hledání nic nenašlo." : reading && !B.everyday ? "Ve výběru nejsou žádné dovolené." : "Ve výběru nejsou žádné fotky."}</p>`
    : `${query && matches.length ? "<h2>Nalezená alba</h2>" : ""}${albumGrid}
       ${photos.length ? `<h2>${query ? "Nalezené fotky" : "Fotky ve složce"}</h2><div class="result-grid">${photos.join("")}</div>` : ""}`);
  $("albumSort").value = B.sort;
  $("albumSort").onchange = event => { B.sort = event.target.value; renderLibrary(); $("albumSort").focus(); };
  if ($("allAlbums")) $("allAlbums").onclick = () => chooseBrowseFolder(null);
  if ($("retryAlbums")) $("retryAlbums").onclick = loadAlbums;
  if ($("resumeWork")) $("resumeWork").onclick = () => showView("work").catch(fail);
  if ($("includeEveryday")) $("includeEveryday").onchange = event => {
    B.everyday = event.target.checked;
    updateView(); renderLibrary(); $("includeEveryday").focus();
  };
  el.querySelectorAll("[data-album]").forEach(button => button.onclick = () => {
    const album = B.albums.find(item => item.path === button.dataset.album);
    openReader(album, button.dataset.photo);
  });
}

function openReader(album, photoPath) {
  stopAutoplay();
  const photo = album.photos.find(item => item.path === photoPath);
  if (photo && !photo.caption) B.captioned = false;
  B.reader = { album, photoPath: photo?.path ?? null };
  B.returnFocus = document.activeElement?.dataset.album === album.path ? { path: album.path, photoPath } : null;
  updateView();
  renderReader();
  $("reader").tabIndex = -1;
  $("reader").focus();
}

function readerPhotos() {
  return B.reader.album.photos.filter(photo => !B.captioned || photo.caption.trim());
}
function readerStep(delta, wrap = false) {
  const photos = readerPhotos();
  if (!photos.length) return;
  let index = photos.findIndex(photo => photo.path === B.reader.photoPath) + delta;
  if (wrap && index >= photos.length) index = -1;
  if (index < -1 || index >= photos.length) return;
  B.reader.photoPath = photos[index]?.path ?? null;
  renderReader();
}
function stopAutoplay() { clearInterval(B.autoplay); B.autoplay = null; }

function renderReader() {
  const { album, photoPath } = B.reader;
  const photos = readerPhotos();
  const photo = photos.find(item => item.path === photoPath);
  const image = photo || album.photos[0];
  const overlay = S.state.readingLayout === "overlay";
  const position = photo ? "Fotka " + (photos.indexOf(photo) + 1) + " z " + photos.length : "Úvod alba";
  const el = $("library");
  let reader = $("reader");
  if (!reader) {
    el.innerHTML = '<article class="reader" id="reader"></article>';
    reader = $("reader");
  }
  const focused = reader.contains(document.activeElement) ? document.activeElement.id : "";
  reader.innerHTML = `<div class="reader-tools">
    <button class="b small" id="readerBack">← Zpět na alba</button>
    <label>Vzhled <select id="readerLayout"><option value="book">Kniha</option><option value="overlay">Fotka přes celou plochu</option></select></label>
    <label class="toggle"><input id="captionOnly" type="checkbox" ${B.captioned ? "checked" : ""}> Jen fotky s popiskem</label>
    <button class="b small" id="autoPlay" ${photos.length ? "" : "disabled"}>${B.autoplay ? "Zastavit přehrávání" : "Přehrávat samo"}</button>
    <button class="b small" id="fullScreen">${document.fullscreenElement === reader ? "Ukončit celou obrazovku" : "Celá obrazovka"}</button>
    ${overlay ? `<button class="b small" id="toggleCaption">${B.captionVisible ? "Skrýt popisek" : "Ukázat popisek"}</button>` : ""}
    ${!photo && S.view === "library" ? '<button class="b small" id="editIntro">Upravit úvod alba</button>' : ""}
  </div>
  <div class="reader-page ${overlay ? "overlay" : ""}" id="readerPage">
    <div class="reader-image ${image.rotation % 180 ? "rotated" : ""}"><img alt="${esc(photoTitle(image))}" src="/api/image?path=${q(image.path)}" style="transform:rotate(${image.rotation}deg)"></div>
    <div class="reader-caption" ${overlay && !B.captionVisible && photo ? "hidden" : ""}>
      <div>${esc(photo ? [albumTitle(album), fmtDate(photo.date), position].filter(Boolean).join(" · ") : albumPeriod(album))}</div>
      <h2>${esc(photo ? photoTitle(photo) : albumTitle(album))}</h2>
      ${photo?.caption || (!photo && album.intro) ? `<p>${esc(photo ? photo.caption : album.intro)}</p>` : ""}
    </div>
  </div>
  <div class="reader-nav"><button class="b" id="readerPrev" ${photo ? "" : "disabled"}>← Předchozí</button>
    <span class="reader-status">${esc(!photo && !photos.length ? "Album nemá fotky s popiskem." : position)}</span>
    <button class="b" id="readerNext" ${photos.length && photo !== photos.at(-1) ? "" : "disabled"}>Další →</button></div>`;
  if (focused) $(focused)?.focus({ preventScroll: true });
  $("readerBack").onclick = () => {
    if (document.fullscreenElement) document.exitFullscreen().catch(fail);
    stopAutoplay(); B.reader = null; updateView(); renderLibrary();
    const focus = B.returnFocus;
    if (focus) [...$("library").querySelectorAll("[data-album]")]
      .find(button => button.dataset.album === focus.path && button.dataset.photo === focus.photoPath)?.focus();
  };
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

function readerKey(event) {
  if (!B.reader || B.intro || event.target.closest("input,textarea,select,[role=separator]")
    || (event.key === " " && event.target.closest("button"))) return;
  if (["ArrowLeft", "ArrowRight", " "].includes(event.key)) {
    event.preventDefault();
    readerStep(event.key === "ArrowLeft" ? -1 : 1);
  }
}

for (const [view, id] of [["work", "workView"], ["library", "libraryView"], ["reading", "readingView"]])
  $(id).onclick = () => showView(view).catch(fail);
$("searchQuery").addEventListener("input", queueSearch);
$("searchQuery").addEventListener("keydown", event => {
  if (event.key === "Enter" && !R.on) interpretSearch(event.target.value, ++B.queryId);
});
$("searchClear").onclick = async () => {
  if (R.on && R.receive) await recStop();
  clearSearch(); B.folder = null; S.browseOpen = new Set([""]);
  updateView(); renderLibrary(); $("searchQuery").focus();
};
$("searchMic").onclick = () => dictateSearch().catch(fail);
window.addEventListener("online", updateView);
window.addEventListener("offline", updateView);
document.addEventListener("fullscreenchange", () => { if (B.reader && !B.intro) renderReader(); });
boot().catch(fail);
