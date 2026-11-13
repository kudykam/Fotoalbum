using Fotoalbum.Core.Ai;

namespace Fotoalbum.Core;

public sealed record StateDto(bool Configured, string? Root, string? RootName, bool ExifToolOk, string? ExifToolVersion,
    string? LastFolder, int PendingWrites, string? Problem, bool AutoImport, int TextSize, int? FolderPanelWidth = null, int? ThumbnailPanelWidth = null, string? ReadingLayout = null);

public sealed record FolderDto(string Path, string Name, string Kind, bool KindInherited, int Total, int Handled, int Done,
    List<FolderDto> Children);

public sealed record PhotoDto(string Path, string Name, string? Date, string Status, string Caption, bool IsDraft,
    int Rotation, bool Vacation, string? Title, bool WritePending, string? WriteError, string Source, string? Raw);

public sealed record FolderPhotosDto(string Folder, string? LastPhoto, List<PhotoDto> Photos);

public sealed record ReadingPhotoDto(string Path, string Name, string? Date, string? Title, string Caption, int Rotation);
public sealed record ReadingAlbumDto(string Path, string Name, string Kind, string Intro, int Handled, List<ReadingPhotoDto> Photos);

public sealed record TrashItemDto(string Path, string Name, string Album);

/// <summary>
/// Druh složky: běžné fotky (výchozí, bez štítku) nebo dovolená (štítek „Dovolená“).
/// Nastavuje se celé složce; podsložky ho dědí.
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
            var own = FolderKind.Normalize(kinds.GetValueOrDefault(n.Path));
            var kind = own ?? parentKind;
            var children = n.Children.Select(c => Map(c, kind)).ToList();
            int done = 0, handled = 0, total = 0;
            foreach (var p in n.PhotoPaths)
            {
                var st = recs.TryGetValue(p, out var r) ? r.Status : PhotoStatus.None;
                if (st == PhotoStatus.MarkedDelete) continue;
                total++;
                if (st is PhotoStatus.Done or PhotoStatus.Manual) { done++; handled++; }
                else if (st == PhotoStatus.Skip) handled++;
            }
            return new FolderDto(n.Path, n.Name, kind, own is null, total + children.Sum(c => c.Total),
                handled + children.Sum(c => c.Handled), done + children.Sum(c => c.Done), children);
        }
    }

    public string KindOf(string folder)
    {
        var kinds = Db.GetFolderKinds();
        var f = folder;
        while (true)
        {
            if (kinds.TryGetValue(f, out var k) && FolderKind.Normalize(k) is { } kind) return kind;
            if (f.Length == 0) return FolderKind.Everyday;
            var i = f.LastIndexOf('/');
            f = i < 0 ? "" : f[..i];
        }
    }

    public void SetFolderKind(string folder, string? kind)
    {
        Library.Resolve(folder);
        Db.SetFolderKind(folder, FolderKind.Normalize(kind));
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

    public List<ReadingAlbumDto> GetReadingAlbums(CancellationToken ct)
    {
        var albums = new List<ReadingAlbumDto>();
        var intros = Db.GetAlbumIntros();
        AddAlbum(GetTree());
        return albums;

        void AddAlbum(FolderDto folder)
        {
            ct.ThrowIfCancellationRequested();
            if (folder.Total > folder.Children.Sum(child => child.Total))
            {
                var photos = OpenFolder(folder.Path).Photos.Where(photo => photo.Status != "del").ToList();
                if (photos.Count > 0)
                    albums.Add(new ReadingAlbumDto(folder.Path, folder.Name, folder.Kind, intros.GetValueOrDefault(folder.Path) ?? "",
                        photos.Count(photo => photo.Status is "done" or "manual" or "skip"),
                        photos.Select(photo => new ReadingPhotoDto(photo.Path, photo.Name, photo.Date, photo.Title,
                            photo.IsDraft ? "" : photo.Caption, photo.Rotation)).ToList()));
            }
            foreach (var child in folder.Children) AddAlbum(child);
        }
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
