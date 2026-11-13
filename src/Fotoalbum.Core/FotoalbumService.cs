using Fotoalbum.Core.Ai;

namespace Fotoalbum.Core;

public sealed record StateDto(bool Configured, string? Root, string? RootName, bool ExifToolOk, string? ExifToolVersion,
    string? LastFolder, int PendingWrites, string? Problem, bool AutoImport, int TextSize, int? FolderPanelWidth = null, int? ThumbnailPanelWidth = null, string? ReadingLayout = null);

/// <param name="OwnTotal">Fotky přímo ve složce (bez podsložek).</param>
/// <param name="First">Nejstarší známé datum fotky ve složce i podsložkách (pro řazení podle data).</param>
public sealed record FolderDto(string Path, string Name, string Kind, bool KindInherited, int Total, int Handled, int Done,
    List<FolderDto> Children, bool Complete, int OwnTotal = 0, int OwnHandled = 0, string? First = null);

public sealed record FolderCompletionDto(string Batch, List<string> Paths, List<PhotoDto>? Photos);

public sealed record PhotoDto(string Path, string Name, string? Date, string Status, string Caption, bool IsDraft,
    int Rotation, bool Vacation, string? Title, bool WritePending, string? WriteError, string Source, string? Raw);

public sealed record FolderPhotosDto(string Folder, string? LastPhoto, List<PhotoDto> Photos);

public sealed record ReadingPhotoDto(string Path, string Name, string? Date, string? Title, string Caption, int Rotation);
/// <summary>Album (dlaždice) na hlavní stránce Čtení – jen údaje z databáze, bez čtení souborů.</summary>
public sealed record ReadingAlbumSummaryDto(string Path, string Name, string Intro, int Count, string? Cover, int CoverRotation, string? From, string? To);
/// <summary>Otevřené album: fotky složky a pak fotky jejích podsložek.</summary>
public sealed record ReadingAlbumDto(string Path, string Name, string Intro, List<ReadingPhotoDto> Photos);
/// <summary>Podklady pro vyhledávání: všechny už prošlé fotky a složky.</summary>
public sealed record SearchIndexDto(List<SearchFolderDto> Folders, List<SearchPhotoDto> Photos, int Indexed, int Total);
public sealed record SearchFolderDto(string Path, string Name, string Kind, string Intro);
public sealed record SearchPhotoDto(string Path, string? Date, string? Title, string Caption, int Rotation);
public sealed record IndexStatusDto(int Indexed, int Total, bool Running);

public sealed record TrashItemDto(string Path, string Name, string Album);

/// <summary>
/// Druh složky: běžné fotky (výchozí, bez štítku) nebo dovolená (štítek „Dovolená“).
/// Nastavuje se jen té jedné složce; podsložky ho nedědí (spec. 2.14).
/// </summary>
public static class FolderKind
{
    public const string Vacation = "dovolena";
    public const string Everyday = "bezne";
    /// <summary>Starší hodnota z první verze – znamená dovolenou.</summary>
    private const string LegacyTravel = "cestovni";

    public static string? Normalize(string? k) => k switch
    {
        Vacation or LegacyTravel => Vacation,
        Everyday => Everyday,
        _ => null,
    };
}

/// <summary>Jediný vstupní bod pro rozhraní (PC i později tablet). Drží knihovnu, databázi, ExifTool a zapisovač.</summary>
public sealed class FotoalbumService : IDisposable
{
    private readonly AppPaths _paths;
    private readonly SettingsStore _settings;
    private readonly Func<string, IRecycleBin> _recycleFactory;
    private readonly object _lock = new();
    private PhotoLibrary? _library;
    private ExifTool? _exif;
    private string? _problem;

    public FotoalbumService(AppPaths paths, Func<string, IRecycleBin>? recycleFactory = null, TimeSpan? writeDelay = null,
        ISecretStore? secrets = null, HttpMessageHandler? aiHandler = null)
    {
        _paths = paths;
        _settings = new SettingsStore(paths);
        Ai = new AiService(aiHandler is null ? new HttpClient() : new HttpClient(aiHandler), secrets ?? new FileSecretStore(paths.DataDir),
            () => _settings.Current, cost => _settings.Update(s => s.AiCostUsd += cost));
        _recycleFactory = recycleFactory ?? (root => new FolderRecycleBin(root));
        Db = new CatalogDb(paths.CatalogFile);
        Thumbs = new Thumbnails(paths.ThumbDir);
        Writer = new MetadataWriter(Db, () => _library, () => _exif, writeDelay);
        Reload();
    }

    public CatalogDb Db { get; }
    public AiService Ai { get; }
    public Thumbnails Thumbs { get; }
    public MetadataWriter Writer { get; }
    public AppSettings Settings => _settings.Current;

    public PhotoLibrary Library => _library ?? throw new InvalidOperationException("Není nastavená složka s fotkami.");

    private void Reload()
    {
        lock (_lock)
        {
            _problem = null;
            _exif = ExifTool.Locate(_settings.Current.ExifToolPath);
            var root = _settings.Current.PhotoRoot;
            _library = null;
            if (string.IsNullOrWhiteSpace(root)) return;
            try { _library = new PhotoLibrary(root); _library.MigrateServiceFolders(); }
            catch (DirectoryNotFoundException) { _problem = $"Složka s fotkami „{root}“ není dostupná. Je připojený disk?"; }
        }
    }

    public StateDto GetState()
    {
        var lib = _library;
        return new StateDto(lib is not null, lib?.Root, lib is null ? null : Path.GetFileName(lib.Root.TrimEnd(Path.DirectorySeparatorChar)),
            _exif is not null, _exif?.TryVersion(), Db.GetState("last_folder"), Writer.PendingCount + Db.GetPendingWrites().Count, _problem,
            _settings.Current.AutoImport, _settings.Current.TextSize, _settings.Current.FolderPanelWidth, _settings.Current.ThumbnailPanelWidth, _settings.Current.ReadingLayout);
    }

    /// <summary>Nastaví kanál aktualizací (stable / test).</summary>
    public void SetUpdateChannel(string channel)
    {
        if (channel is not ("stable" or "test")) throw new ArgumentException("Neznámý kanál aktualizací.");
        _settings.Update(s => { s.UpdateChannel = channel; s.UpdateChannelChosen = true; });
    }

    public void Configure(string? photoRoot, string? exifToolPath, bool? autoImport = null, int? textSize = null)
    {
        if (autoImport is { } ai) _settings.Update(s => s.AutoImport = ai);
        if (textSize is { } ts) _settings.Update(s => s.TextSize = Math.Clamp(ts, 80, 140));
        if (photoRoot is not null)
        {
            var full = Path.GetFullPath(photoRoot.Trim().Trim('"'));
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"Složka „{full}“ neexistuje.");
            Writer.FlushAll();
            _settings.Update(s => s.PhotoRoot = full);
        }
        if (exifToolPath is not null) _settings.Update(s => s.ExifToolPath = exifToolPath.Trim().Length == 0 ? null : exifToolPath.Trim());
        Reload();
    }

    public void SetReadingLayout(string layout)
    {
        if (layout is not ("book" or "overlay")) throw new ArgumentException("Neznámé rozložení.");
        _settings.Update(settings => settings.ReadingLayout = layout);
    }

    public void SetPanelWidth(string panel, int? width)
    {
        if (panel is not ("folders" or "thumbnails")) throw new ArgumentException("Neznámý panel.");
        int? size = width is int value ? Math.Clamp(value, 140, 640) : null;
        _settings.Update(settings =>
        {
            if (panel == "folders") settings.FolderPanelWidth = size;
            else settings.ThumbnailPanelWidth = size;
        });
    }

    // ---------- složky ----------

    public FolderDto GetTree()
    {
        var lib = Library;
        var node = lib.ScanTree();
        var recs = Db.GetPhotos(node.AllPhotoPaths());
        var kinds = Db.GetFolderKinds();
        return Map(node, FolderKind.Everyday);

        FolderDto Map(FolderNode n, string parentKind)
        {
            // druh platí jen pro tu jednu složku – podsložky ho nedědí
            var kind = FolderKind.Normalize(kinds.GetValueOrDefault(n.Path)) ?? FolderKind.Everyday;
            var children = n.Children.Select(c => Map(c, kind)).ToList();
            int done = 0, handled = 0, total = 0;
            string? first = null;
            foreach (var p in n.PhotoPaths)
            {
                var r = recs.GetValueOrDefault(p);
                var st = r?.Status ?? PhotoStatus.None;
                if (st == PhotoStatus.MarkedDelete) continue;
                total++;
                if (st is PhotoStatus.Done or PhotoStatus.Manual) { done++; handled++; }
                else if (st == PhotoStatus.Skip) handled++;
                if (r?.DateTaken is { } d && (first is null || string.CompareOrdinal(d, first) < 0)) first = d;
            }
            foreach (var c in children)
                if (c.First is { } d && (first is null || string.CompareOrdinal(d, first) < 0)) first = d;
            return new FolderDto(n.Path, n.Name, kind, false, total + children.Sum(c => c.Total),
                handled + children.Sum(c => c.Handled), done + children.Sum(c => c.Done), children, total > 0 && handled == total,
                total, handled, first);
        }
    }

    /// <summary>Druh složky – jen její vlastní nastavení, podsložky nedědí.</summary>
    public string KindOf(string folder) =>
        FolderKind.Normalize(Db.GetFolderKinds().GetValueOrDefault(folder)) ?? FolderKind.Everyday;

    public void SetFolderKind(string folder, string? kind)
    {
        Library.Resolve(folder);
        Db.SetFolderKind(folder, FolderKind.Normalize(kind));
    }

    public FolderCompletionDto CompleteFolder(string folder, string? undoBatch)
    {
        // Před označením načti i starší popisky uložené jen v souborech.
        var photos = undoBatch is null ? OpenFolder(folder).Photos : null;
        var paths = photos?.Select(photo => photo.Path).ToList() ?? Library.ListPhotos(folder);
        var (batch, changed) = Db.SetFolderSkipped(paths, undoBatch);
        if (photos is not null)
        {
            var skipped = new HashSet<string>(changed, StringComparer.OrdinalIgnoreCase);
            photos = photos.Select(photo => skipped.Contains(photo.Path) ? photo with { Status = "skip" } : photo).ToList();
        }
        return new FolderCompletionDto(batch, changed, photos);
    }

    /// <summary>Otevře složku: načte popisky a data ze souborů, které ještě neznáme nebo se změnily.</summary>
    public FolderPhotosDto OpenFolder(string folder)
    {
        var lib = Library;
        var paths = lib.ListPhotos(folder);
        var recs = Db.GetPhotos(paths);
        var toImport = new List<string>();
        foreach (var p in paths)
        {
            var fi = new FileInfo(lib.Resolve(p));
            if (!recs.TryGetValue(p, out var r) || !r.Imported || r.FileSize != fi.Length || r.FileMtime != fi.LastWriteTimeUtc.Ticks)
                toImport.Add(p);
        }
        if (toImport.Count > 0 && _exif is not null)
        {
            var byFull = toImport.ToDictionary(lib.Resolve, p => p, StringComparer.OrdinalIgnoreCase);
            foreach (var m in _exif.Read(byFull.Keys))
            {
                if (!byFull.TryGetValue(m.Path, out var rel)) continue;
                var fi = new FileInfo(m.Path);
                Db.ApplyImport(rel, m.Caption, m.DateTaken, fi.Length, fi.LastWriteTimeUtc.Ticks);
            }
            recs = Db.GetPhotos(paths);
        }
        var kind = KindOf(folder);
        var list = paths.Select(p => ToDto(p, recs.GetValueOrDefault(p), kind))
            .OrderBy(d => d.Date ?? "9999")
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var last = Db.GetFolder(folder).LastPhoto;
        if (last is not null && !list.Any(d => string.Equals(d.Path, last, StringComparison.OrdinalIgnoreCase))) last = null;
        return new FolderPhotosDto(folder, last, list);
    }

    /// <summary>
    /// Dlaždice hlavní stránky Čtení: jen složky označené jako dovolená. Údaje jen z databáze –
    /// soubory se nečtou, takže je to okamžité i u velké knihovny.
    /// </summary>
    public List<ReadingAlbumSummaryDto> GetReadingAlbums()
    {
        var lib = Library;
        var root = lib.ScanTree();
        var kinds = Db.GetFolderKinds();
        var intros = Db.GetAlbumIntros();
        var vacations = new List<FolderNode>();
        Collect(root);
        var recs = Db.GetPhotos(vacations.SelectMany(v => v.AllPhotoPaths()));
        var result = new List<ReadingAlbumSummaryDto>();
        foreach (var f in vacations)
        {
            var photos = f.AllPhotoPaths()
                .Select(p => (Path: p, Rec: recs.GetValueOrDefault(p)))
                .Where(x => x.Rec is not { MarkedDelete: true })
                .ToList();
            if (photos.Count == 0) continue;
            var dated = photos.Where(x => x.Rec?.DateTaken is not null).OrderBy(x => x.Rec!.DateTaken, StringComparer.Ordinal).ToList();
            var cover = dated.Count > 0 ? dated[0] : photos[0];
            result.Add(new ReadingAlbumSummaryDto(f.Path, f.Name, intros.GetValueOrDefault(f.Path) ?? "", photos.Count,
                cover.Path, cover.Rec?.Rotation ?? 0, dated.FirstOrDefault().Rec?.DateTaken, dated.LastOrDefault().Rec?.DateTaken));
        }
        return result;

        void Collect(FolderNode n)
        {
            if (FolderKind.Normalize(kinds.GetValueOrDefault(n.Path)) == FolderKind.Vacation) vacations.Add(n);
            foreach (var c in n.Children) Collect(c);
        }
    }

    /// <summary>Otevře album ke čtení: fotky složky, pak fotky podsložek (při prvním otevření se načtou ze souborů).</summary>
    public ReadingAlbumDto GetReadingAlbum(string folder, CancellationToken ct)
    {
        var lib = Library;
        var dir = lib.Resolve(folder);
        if (!Directory.Exists(dir)) throw new DirectoryNotFoundException("Složka neexistuje.");
        var rel = lib.ToRelative(dir);
        var node = FindNode(lib.ScanTree(), rel) ?? new FolderNode(rel, Path.GetFileName(dir));
        var photos = new List<ReadingPhotoDto>();
        Add(node);
        var name = rel.Length == 0 ? Path.GetFileName(lib.Root.TrimEnd(Path.DirectorySeparatorChar)) : node.Name;
        return new ReadingAlbumDto(rel, name, Db.GetAlbumIntros().GetValueOrDefault(rel) ?? "", photos);

        void Add(FolderNode n)
        {
            ct.ThrowIfCancellationRequested();
            if (n.PhotoPaths.Count > 0)
                photos.AddRange(OpenFolder(n.Path).Photos.Where(p => p.Status != "del")
                    .Select(p => new ReadingPhotoDto(p.Path, p.Name, p.Date, p.Title, p.IsDraft ? "" : p.Caption, p.Rotation)));
            foreach (var c in n.Children) Add(c);
        }

        static FolderNode? FindNode(FolderNode n, string path)
        {
            if (string.Equals(n.Path, path, StringComparison.OrdinalIgnoreCase)) return n;
            foreach (var c in n.Children) if (FindNode(c, path) is { } f) return f;
            return null;
        }
    }

    /// <summary>Podklady pro vyhledávání (Čtení): složky a všechny fotky, které už program prošel.</summary>
    public SearchIndexDto GetSearchIndex()
    {
        var lib = Library;
        var root = lib.ScanTree();
        var kinds = Db.GetFolderKinds();
        var intros = Db.GetAlbumIntros();
        var all = root.AllPhotoPaths().ToList();
        var recs = Db.GetPhotos(all);
        var folders = new List<SearchFolderDto>();
        void Walk(FolderNode n)
        {
            if (n.TotalPhotos > 0)
                folders.Add(new SearchFolderDto(n.Path, n.Path.Length == 0 ? root.Name : n.Name,
                    FolderKind.Normalize(kinds.GetValueOrDefault(n.Path)) ?? FolderKind.Everyday, intros.GetValueOrDefault(n.Path) ?? ""));
            foreach (var c in n.Children) Walk(c);
        }
        Walk(root);
        var photos = new List<SearchPhotoDto>();
        foreach (var p in all)
        {
            if (!recs.TryGetValue(p, out var r) || !r.Imported || r.MarkedDelete) continue;
            photos.Add(new SearchPhotoDto(p, r.DateTaken, r.Title, r.IsDraft ? "" : r.Caption, r.Rotation));
        }
        return new SearchIndexDto(folders, photos, recs.Values.Count(r => r.Imported), all.Count);
    }

    // ---------- procházení knihovny na pozadí (pro vyhledávání) ----------

    private volatile IndexStatusDto _index = new(0, 0, false);
    public IndexStatusDto IndexStatus => _index;

    /// <summary>
    /// Pomalu a potichu projde všechny složky a načte z fotek data a popisky, aby šly najít.
    /// Po celém průchodu počká a projde znovu (nové fotky z karty, změny jinými programy).
    /// </summary>
    public async Task RunIndexerAsync(TimeSpan startDelay, TimeSpan pause, CancellationToken ct)
    {
        try { await Task.Delay(startDelay, ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try { await IndexOnceAsync(pause, ct); }
            catch (OperationCanceledException) { return; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { /* zkusí se příště */ }
            _index = _index with { Running = false };
            try { await Task.Delay(TimeSpan.FromMinutes(30), ct); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Jeden průchod knihovnou (veřejné kvůli testům).</summary>
    public async Task IndexOnceAsync(TimeSpan pause, CancellationToken ct)
    {
        if (_library is not { } lib || _exif is null) return;
        var root = lib.ScanTree();
        var folders = new List<FolderNode>();
        void Walk(FolderNode n) { if (n.PhotoPaths.Count > 0) folders.Add(n); foreach (var c in n.Children) Walk(c); }
        Walk(root);
        var total = folders.Sum(f => f.PhotoPaths.Count);
        var indexed = Db.GetPhotos(root.AllPhotoPaths()).Values.Count(r => r.Imported);
        _index = new IndexStatusDto(indexed, total, true);
        foreach (var f in folders)
        {
            ct.ThrowIfCancellationRequested();
            var before = Db.GetPhotos(f.PhotoPaths).Values.Count(r => r.Imported);
            if (before == f.PhotoPaths.Count) continue;
            OpenFolder(f.Path); // načte jen nové a změněné fotky
            indexed += Db.GetPhotos(f.PhotoPaths).Values.Count(r => r.Imported) - before;
            _index = new IndexStatusDto(indexed, total, true);
            await Task.Delay(pause, ct);
        }
        _index = new IndexStatusDto(indexed, total, false);
    }

    public void SetAlbumIntro(string folder, string intro)
    {
        var lib = Library;
        var path = lib.Resolve(folder);
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException("Složka neexistuje.");
        if (intro.Length > 12000) throw new ArgumentException("Úvod může mít nejvýše 12 000 znaků.");
        Db.SetAlbumIntro(lib.ToRelative(path), intro.Trim());
    }

    // ---------- fotky ----------

    public PhotoDto GetPhoto(string path)
    {
        Library.Resolve(path);
        return ToDto(path, Db.GetPhoto(path), KindOf(ParentOf(path)));
    }

    /// <param name="source">Původ textu: „manual“ (psaní/diktát bez korektury) nebo „ai“ (po korektuře).</param>
    /// <param name="raw">Původní diktát, ze kterého AI popisek vytvořila (pro opakovanou korekturu).</param>
    public PhotoDto SaveCaption(string path, string caption, bool isDraft, string? source = null, string? title = null, string? raw = null)
    {
        EnsurePhoto(path);
        var src = source is CaptionSource.Ai or CaptionSource.File ? source : CaptionSource.Manual;
        Db.SaveCaption(path, caption, isDraft, captionSource: src, title: src == CaptionSource.Ai ? title : null, rawDictation: raw);
        Writer.Schedule(path);
        return GetPhoto(path);
    }

    public PhotoDto SetFlags(string path, bool? skip, bool? markedDelete, int? rotation)
    {
        EnsurePhoto(path);
        Db.SetFlags(path, skip, markedDelete, rotation);
        return GetPhoto(path);
    }

    /// <summary>Uživatel opustil fotku: popisek se zapíše do souboru hned (na pozadí) a uloží se pozice.</summary>
    public void Leave(string folder, string path)
    {
        EnsurePhoto(path);
        Db.SetLastPhoto(folder, path);
        Task.Run(() => Writer.WriteNow(path));
    }

    public void SetPosition(string folder, string path)
    {
        EnsurePhoto(path);
        Db.SetLastPhoto(folder, path);
    }

    public List<HistoryEntry> History(string path)
    {
        Library.Resolve(path);
        return Db.GetHistory(path);
    }

    // ---------- AI (diktování a korektura) ----------

    public sealed record AiStatusDto(bool HasKey, string? KeyHint, string? TextModel, string? AudioModel, string DefaultTextModel,
        string? Glossary, string CaptionLength, double CostUsd);

    public AiStatusDto AiStatus()
    {
        var s = Settings;
        return new AiStatusDto(Ai.HasKey, Ai.KeyHint, s.AiTextModel, s.AiAudioModel, AiService.DefaultTextModel, s.Glossary, s.CaptionLength, s.AiCostUsd);
    }

    public void ConfigureAi(string? key, string? textModel, string? audioModel, string? glossary, string? captionLength)
    {
        if (key is not null) Ai.SetKey(key);
        _settings.Update(s =>
        {
            if (textModel is not null) s.AiTextModel = textModel.Trim().Length == 0 ? null : textModel.Trim();
            if (audioModel is not null) s.AiAudioModel = audioModel.Trim().Length == 0 ? null : audioModel.Trim();
            if (glossary is not null) s.Glossary = glossary.Trim();
            if (captionLength is "kratky" or "stredni" or "delsi") s.CaptionLength = captionLength;
        });
    }

    /// <summary>Kontext pro korekturu: album, datum a popisky sousedních fotek.</summary>
    public CaptionContext ContextFor(string path)
    {
        EnsurePhoto(path);
        var folder = ParentOf(path);
        var paths = Library.ListPhotos(folder);
        var recs = Db.GetPhotos(paths);
        var ordered = paths.OrderBy(p => recs.GetValueOrDefault(p)?.DateTaken ?? "9999").ThenBy(p => p, StringComparer.CurrentCultureIgnoreCase).ToList();
        var i = ordered.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        var neighbours = new List<string>();
        foreach (var j in new[] { i - 2, i - 1, i + 1 })
            if (j >= 0 && j < ordered.Count && j != i && recs.GetValueOrDefault(ordered[j]) is { Caption.Length: > 0, IsDraft: false } r)
                neighbours.Add(r.Caption);
        var date = recs.GetValueOrDefault(path)?.DateTaken;
        if (date is not null && DateTime.TryParse(date, out var d)) date = d.ToString("d. M. yyyy");
        return new CaptionContext(AlbumName(folder), date, neighbours, Settings.Glossary ?? "", Settings.CaptionLength);
    }

    // ---------- mazání ----------

    public List<TrashItemDto> MarkedForDelete() =>
        Db.GetMarkedForDelete().Select(r => new TrashItemDto(r.Path, Path.GetFileName(r.Path), AlbumName(ParentOf(r.Path)))).ToList();

    /// <summary>Název alba pro uživatele: „Afrika 1998 · Keňa“ (poslední dvě úrovně složek).</summary>
    public static string AlbumName(string folder)
    {
        if (folder.Length == 0) return "Všechny fotky";
        var parts = folder.Split('/');
        return parts.Length == 1 ? parts[0] : $"{parts[^2]} · {parts[^1]}";
    }

    /// <summary>Při zavření: přesunout označené fotky do koše, nebo jim zrušit označení.</summary>
    public int ResolveMarked(bool moveToBin)
    {
        var lib = Library;
        var bin = _recycleFactory(lib.Root);
        var count = 0;
        foreach (var rec in Db.GetMarkedForDelete())
        {
            if (!moveToBin) { Db.SetFlags(rec.Path, markedDelete: false); continue; }
            var full = lib.Resolve(rec.Path);
            if (File.Exists(full)) bin.MoveToBin(full);
            Db.RemovePhoto(rec.Path);
            count++;
        }
        return count;
    }

    // ---------- záloha a ukončení ----------

    /// <summary>Konzistentní záloha databáze do „Fotokronika – databáze (nemazat)/zaloha“ ve složce s fotkami (zálohuje ji Synology Drive).</summary>
    public string? BackupCatalog()
    {
        var lib = _library;
        if (lib is null) return null;
        lib.MigrateServiceFolders();
        var dir = Path.Combine(lib.DatabaseDir, "zaloha");
        var file = Path.Combine(dir, $"katalog-{DateTime.Now:yyyy-MM-dd_HHmmss}.db");
        Db.BackupTo(file);
        foreach (var old in Directory.GetFiles(dir, "katalog-*.db").OrderDescending().Skip(Math.Max(1, Settings.BackupsToKeep)))
            try { File.Delete(old); } catch (IOException) { }
        return file;
    }

    public void Shutdown()
    {
        Writer.FlushAll();
        try { BackupCatalog(); } catch (IOException) { /* záloha se zkusí příště */ }
    }

    // ---------- pomocné ----------

    private void EnsurePhoto(string path)
    {
        var full = Library.Resolve(path);
        if (!PhotoLibrary.IsPhotoFile(full) || !File.Exists(full)) throw new FileNotFoundException("Fotka neexistuje.", path);
    }

    private static string ParentOf(string path)
    {
        var i = path.LastIndexOf('/');
        return i < 0 ? "" : path[..i];
    }

    private PhotoDto ToDto(string path, PhotoRecord? r, string folderKind)
    {
        r ??= new PhotoRecord { Path = path };
        var status = r.Status switch
        {
            PhotoStatus.Done => "done", PhotoStatus.Manual => "manual", PhotoStatus.Draft => "draft", PhotoStatus.Skip => "skip",
            PhotoStatus.MarkedDelete => "del", _ => "none",
        };
        return new PhotoDto(path, Path.GetFileName(path), r.DateTaken, status, r.Caption, r.IsDraft, r.Rotation,
            folderKind == FolderKind.Vacation, r.IsDraft ? null : r.Title ?? CaptionTitle.From(r.Caption), r.NeedsFileWrite, r.WriteError,
            r.CaptionSource, r.RawDictation.Length > 0 ? r.RawDictation : null);
    }

    public void Dispose()
    {
        Writer.Dispose();
        Db.Dispose();
    }
}
