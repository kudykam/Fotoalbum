namespace Fotoalbum.Core;

public sealed record StateDto(bool Configured, string? Root, string? RootName, bool ExifToolOk, string? ExifToolVersion,
    string? LastFolder, int PendingWrites, string? Problem);

public sealed record FolderDto(string Path, string Name, string Kind, bool KindInherited, int Total, int Handled, int Done,
    List<FolderDto> Children);

public sealed record PhotoDto(string Path, string Name, string? Date, string Status, string Caption, bool IsDraft,
    int Rotation, bool Everyday, bool? EverydayOverride, bool WritePending, string? WriteError);

public sealed record FolderPhotosDto(string Folder, string? LastPhoto, List<PhotoDto> Photos);

public sealed record TrashItemDto(string Path, string Name);

/// <summary>Druh složky: cestovní (výchozí) nebo běžné fotky.</summary>
public static class FolderKind
{
    public const string Travel = "cestovni";
    public const string Everyday = "bezne";
    public static bool IsValid(string? k) => k is Travel or Everyday;
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

    public FotoalbumService(AppPaths paths, Func<string, IRecycleBin>? recycleFactory = null, TimeSpan? writeDelay = null)
    {
        _paths = paths;
        _settings = new SettingsStore(paths);
        _recycleFactory = recycleFactory ?? (root => new FolderRecycleBin(root));
        Db = new CatalogDb(paths.CatalogFile);
        Thumbs = new Thumbnails(paths.ThumbDir);
        Writer = new MetadataWriter(Db, () => _library, () => _exif, writeDelay);
        Reload();
    }

    public CatalogDb Db { get; }
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
            try { _library = new PhotoLibrary(root); }
            catch (DirectoryNotFoundException) { _problem = $"Složka s fotkami „{root}“ není dostupná. Je připojený disk?"; }
        }
    }

    public StateDto GetState()
    {
        var lib = _library;
        return new StateDto(lib is not null, lib?.Root, lib is null ? null : Path.GetFileName(lib.Root.TrimEnd(Path.DirectorySeparatorChar)),
            _exif is not null, _exif?.TryVersion(), Db.GetState("last_folder"), Writer.PendingCount + Db.GetPendingWrites().Count, _problem);
    }

    public void Configure(string? photoRoot, string? exifToolPath)
    {
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

    // ---------- složky ----------

    public FolderDto GetTree()
    {
        var lib = Library;
        var node = lib.ScanTree();
        var recs = Db.GetPhotos(node.AllPhotoPaths());
        var kinds = Db.GetFolderKinds();
        return Map(node, FolderKind.Travel);

        FolderDto Map(FolderNode n, string parentKind)
        {
            var own = kinds.GetValueOrDefault(n.Path);
            var kind = FolderKind.IsValid(own) ? own! : parentKind;
            var children = n.Children.Select(c => Map(c, kind)).ToList();
            int done = 0, handled = 0, total = 0;
            foreach (var p in n.PhotoPaths)
            {
                var st = recs.TryGetValue(p, out var r) ? r.Status : PhotoStatus.None;
                if (st == PhotoStatus.MarkedDelete) continue;
                total++;
                if (st == PhotoStatus.Done) { done++; handled++; }
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
            if (kinds.TryGetValue(f, out var k) && FolderKind.IsValid(k)) return k;
            if (f.Length == 0) return FolderKind.Travel;
            var i = f.LastIndexOf('/');
            f = i < 0 ? "" : f[..i];
        }
    }

    public void SetFolderKind(string folder, string? kind)
    {
        Library.Resolve(folder);
        Db.SetFolderKind(folder, FolderKind.IsValid(kind) ? kind : null);
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

    // ---------- fotky ----------

    public PhotoDto GetPhoto(string path)
    {
        Library.Resolve(path);
        return ToDto(path, Db.GetPhoto(path), KindOf(ParentOf(path)));
    }

    public PhotoDto SaveCaption(string path, string caption, bool isDraft)
    {
        EnsurePhoto(path);
        Db.SaveCaption(path, caption, isDraft);
        Writer.Schedule(path);
        return GetPhoto(path);
    }

    public PhotoDto SetFlags(string path, bool? skip, bool? markedDelete, int? rotation, bool? everyday, bool clearEveryday)
    {
        EnsurePhoto(path);
        Db.SetFlags(path, skip, markedDelete, rotation, everyday, clearEveryday);
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

    // ---------- mazání ----------

    public List<TrashItemDto> MarkedForDelete() =>
        Db.GetMarkedForDelete().Select(r => new TrashItemDto(r.Path, Path.GetFileName(r.Path))).ToList();

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

    /// <summary>Konzistentní záloha databáze do <c>_fotoalbum/zaloha</c> ve složce s fotkami (zálohuje ji Synology Drive).</summary>
    public string? BackupCatalog()
    {
        var lib = _library;
        if (lib is null) return null;
        var dir = Path.Combine(lib.Root, "_fotoalbum", "zaloha");
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
            PhotoStatus.Done => "done", PhotoStatus.Draft => "draft", PhotoStatus.Skip => "skip",
            PhotoStatus.MarkedDelete => "del", _ => "none",
        };
        return new PhotoDto(path, Path.GetFileName(path), r.DateTaken, status, r.Caption, r.IsDraft, r.Rotation,
            r.Everyday ?? folderKind == FolderKind.Everyday, r.Everyday, r.NeedsFileWrite, r.WriteError);
    }

    public void Dispose()
    {
        Writer.Dispose();
        Db.Dispose();
    }
}
