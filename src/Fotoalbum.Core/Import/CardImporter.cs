using Microsoft.Data.Sqlite;

namespace Fotoalbum.Core.Import;

/// <summary>
/// Roční období podle měsíců (meteorologicky): jaro 3–5, léto 6–8, podzim 9–11, zima 12–2.
/// Zima nese rok, ve kterém začala: prosinec 2026, leden a únor 2027 → „2026 zima“.
/// Složky „2026 jaro, 2026 léto, 2026 podzim, 2026 zima“ se tak i abecedně řadí podle času.
/// </summary>
public static class Seasons
{
    public static string Of(DateTime d) => d.Month switch
    {
        3 or 4 or 5 => "jaro",
        6 or 7 or 8 => "léto",
        9 or 10 or 11 => "podzim",
        _ => "zima",
    };

    /// <summary>Název cílové složky, např. „2026 léto“; leden a únor patří k zimě předchozího roku.</summary>
    public static string FolderName(DateTime d) => $"{(d.Month <= 2 ? d.Year - 1 : d.Year)} {Of(d)}";
}

/// <summary>Soubory, které se z karty kopírují: fotky JPG a videa.</summary>
public static class MediaFiles
{
    private static readonly HashSet<string> VideoExt = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mov", ".mts", ".m2ts", ".avi", ".m4v", ".3gp" };

    public static bool IsVideo(string path) => VideoExt.Contains(Path.GetExtension(path));
    public static bool IsMedia(string path) => PhotoLibrary.IsPhotoFile(path) || IsVideo(path);
}

public sealed record ImportItem(string SourcePath, string TargetFolder, string TargetName, DateTime Taken, long Size, string Key);

public sealed record ImportPlan(string SourceRoot, List<ImportItem> Items, int AlreadyPresent);

public sealed record ImportProgress(int Done, int Total, string CurrentFile, string CurrentFolder);

public sealed record ImportResult(int Copied, int AlreadyPresent, List<string> Folders, List<string> Errors, long Bytes);

/// <summary>
/// Kopíruje nové fotky z paměťové karty (složka DCIM) do složky s fotkami, rozdělené podle roku a ročního období.
/// Karta se nikdy nemění ani nemaže. Fotky, které už v počítači jsou (i s doplněným popiskem),
/// nebo které se z karty už jednou kopírovaly, se přeskočí.
/// </summary>
public sealed class CardImporter
{
    private readonly string _libraryRoot;
    private readonly ExifTool? _exif;
    private readonly string _indexFile;

    public CardImporter(string libraryRoot, ExifTool? exif, string indexFile)
    {
        _libraryRoot = Path.GetFullPath(libraryRoot);
        _exif = exif;
        _indexFile = indexFile;
    }

    /// <summary>
    /// Pozná kartu z fotoaparátu podle standardu DCF: v kořeni složka DCIM a v ní podsložky
    /// „3 číslice + 5 znaků“, např. 100_PANA, 101_PANA (Panasonic), 100CANON, 100NIKON, 100MSDCF (Sony).
    /// Vrátí tyto podsložky; prázdný seznam = nejde o kartu z fotoaparátu.
    /// </summary>
    public static List<string> FindCameraFolders(string driveRoot)
    {
        try
        {
            var dcim = Path.Combine(driveRoot, "DCIM");
            if (!Directory.Exists(dcim)) return [];
            return Directory.EnumerateDirectories(dcim)
                .Where(d => IsDcfFolderName(Path.GetFileName(d)))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>
    /// Složky s videi mimo DCIM: Panasonic, Sony aj. ukládají videa AVCHD do PRIVATE/AVCHD/BDMV/STREAM (*.MTS),
    /// Sony XAVC do PRIVATE/M4ROOT/CLIP. Hledají se jen na kartě, která má i DCIM.
    /// </summary>
    public static List<string> FindVideoFolders(string driveRoot)
    {
        var list = new List<string>();
        foreach (var rel in new[] { Path.Combine("PRIVATE", "AVCHD", "BDMV", "STREAM"), Path.Combine("PRIVATE", "M4ROOT", "CLIP") })
        {
            try { var d = Path.Combine(driveRoot, rel); if (Directory.Exists(d)) list.Add(d); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return list;
    }

    public static bool IsDcfFolderName(string name) =>
        name.Length == 8 && char.IsAsciiDigit(name[0]) && char.IsAsciiDigit(name[1]) && char.IsAsciiDigit(name[2])
        && name[0] != '0' && name[3..].All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>Klíč fotky, který se nezmění zápisem popisku: název souboru + datum a čas pořízení.</summary>
    public static string KeyOf(string fileName, DateTime? taken, long size) =>
        fileName.ToUpperInvariant() + "|" + (taken is { } t ? t.ToString("yyyy-MM-ddTHH:mm:ss") : "velikost:" + size);

    public ImportPlan Plan(string driveRoot, CancellationToken ct = default)
    {
        var cameraFolders = FindCameraFolders(driveRoot);
        var sources = cameraFolders.Concat(cameraFolders.Count > 0 ? FindVideoFolders(driveRoot) : [])
            .SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.TopDirectoryOnly))
            .Where(MediaFiles.IsMedia)
            .Where(f => !Path.GetFileName(f).StartsWith('.')) // skryté soubory macOS (._IMG…)
            .Select(f => new FileInfo(f))
            .ToList();
        ct.ThrowIfCancellationRequested();

        var dates = ReadDates(sources.Select(s => s.FullName).ToList());
        using var index = OpenIndex();

        // Fotky v knihovně se stejným názvem jako na kartě – jen u nich je potřeba zjistit datum pořízení.
        var cardNames = new HashSet<string>(sources.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
        var libraryByName = new Dictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in EnumerateLibrary())
        {
            if (!cardNames.Contains(f.Name)) continue;
            if (!libraryByName.TryGetValue(f.Name, out var list)) libraryByName[f.Name] = list = [];
            list.Add(f);
        }
        var libraryDates = ReadDates(libraryByName.Values.SelectMany(l => l).Select(f => f.FullName).ToList());
        var libraryKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in libraryByName.Values.SelectMany(l => l))
        {
            var d = libraryDates.GetValueOrDefault(f.FullName);
            libraryKeys.Add(KeyOf(f.Name, d, f.Length));
            // bez data v EXIF porovnáme i podle velikosti původního souboru
            if (d is null) libraryKeys.Add(KeyOf(f.Name, null, f.Length));
        }

        var items = new List<ImportItem>();
        var present = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in sources.OrderBy(s => dates.GetValueOrDefault(s.FullName) ?? s.LastWriteTime).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var taken = dates.GetValueOrDefault(s.FullName);
            var key = KeyOf(s.Name, taken, s.Length);
            if (!seen.Add(key) || libraryKeys.Contains(key) || WasImported(index, key)) { present++; continue; }
            var when = taken ?? s.LastWriteTime;
            items.Add(new ImportItem(s.FullName, Seasons.FolderName(when), s.Name, when, s.Length, key));
        }
        return new ImportPlan(driveRoot, items, present);
    }

    public ImportResult Execute(ImportPlan plan, IProgress<ImportProgress>? progress = null, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var folders = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var copied = 0;
        long bytes = 0;
        using var index = OpenIndex();
        for (var i = 0; i < plan.Items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = plan.Items[i];
            progress?.Report(new ImportProgress(i, plan.Items.Count, item.TargetName, item.TargetFolder));
            try
            {
                var dir = Path.Combine(_libraryRoot, item.TargetFolder);
                Directory.CreateDirectory(dir);
                var target = FreeName(dir, item.TargetName, item.SourcePath);
                if (target is null) { Remember(index, item, Path.Combine(dir, item.TargetName)); continue; } // stejný soubor už tam je
                var tmp = target + ".kopiruje-se";
                File.Copy(item.SourcePath, tmp, overwrite: true);
                var src = new FileInfo(item.SourcePath);
                if (new FileInfo(tmp).Length != src.Length) { File.Delete(tmp); throw new IOException("Kopie má jinou velikost než originál."); }
                File.SetLastWriteTimeUtc(tmp, src.LastWriteTimeUtc);
                File.Move(tmp, target);
                File.SetCreationTimeUtc(target, src.CreationTimeUtc);
                Remember(index, item, target);
                folders.Add(item.TargetFolder);
                copied++;
                bytes += item.Size;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{item.TargetName}: {e.Message}");
            }
        }
        progress?.Report(new ImportProgress(plan.Items.Count, plan.Items.Count, "", ""));
        return new ImportResult(copied, plan.AlreadyPresent, folders.ToList(), errors, bytes);
    }

    // ---------- pomocné ----------

    private IEnumerable<FileInfo> EnumerateLibrary()
    {
        var stack = new Stack<string>([_libraryRoot]);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> files, dirs;
            try { files = Directory.EnumerateFiles(dir).ToList(); dirs = Directory.EnumerateDirectories(dir).ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var f in files) if (MediaFiles.IsMedia(f)) yield return new FileInfo(f);
            foreach (var d in dirs)
            {
                var name = Path.GetFileName(d);
                // originály (před odstraněním vepsaného textu) se počítají jako „už v počítači“
                var originals = name.Equals(PhotoLibrary.OriginalsFolder, StringComparison.OrdinalIgnoreCase)
                    || name.Equals(PhotoLibrary.LegacyOriginalsFolder, StringComparison.OrdinalIgnoreCase);
                if (originals || !PhotoLibrary.IsIgnoredDir(name)) stack.Push(d);
            }
        }
    }

    private Dictionary<string, DateTime?> ReadDates(List<string> files)
    {
        var result = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
        if (files.Count == 0) return result;
        if (_exif is not null)
        {
            foreach (var (path, taken) in _exif.ReadTakenDates(files))
                result[path] = taken;
        }
        foreach (var f in files) result.TryAdd(Path.GetFullPath(f), null);
        return result;
    }

    /// <summary>Vrátí volný název v cílové složce; null, pokud tam stejný soubor (stejná velikost i obsah) už je.</summary>
    private static string? FreeName(string dir, string name, string source)
    {
        var target = Path.Combine(dir, name);
        if (!File.Exists(target)) return target;
        if (SameContent(target, source)) return null;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var n = 2; ; n++)
        {
            target = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!File.Exists(target)) return target;
            if (SameContent(target, source)) return null;
        }
    }

    private static bool SameContent(string a, string b)
    {
        var fa = new FileInfo(a); var fb = new FileInfo(b);
        if (fa.Length != fb.Length) return false;
        using var sa = File.OpenRead(a); using var sb = File.OpenRead(b);
        return System.Security.Cryptography.SHA256.HashData(sa).AsSpan().SequenceEqual(System.Security.Cryptography.SHA256.HashData(sb));
    }

    private SqliteConnection OpenIndex()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_indexFile)!);
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _indexFile, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;
            CREATE TABLE IF NOT EXISTS imported(
              key TEXT PRIMARY KEY, source_name TEXT NOT NULL, target TEXT NOT NULL, imported_at TEXT NOT NULL);
            """;
        cmd.ExecuteNonQuery();
        return c;
    }

    private static bool WasImported(SqliteConnection c, string key)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM imported WHERE key=$k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() is not null;
    }

    private static void Remember(SqliteConnection c, ImportItem item, string target)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO imported(key, source_name, target, imported_at) VALUES($k,$n,$t,$a)";
        cmd.Parameters.AddWithValue("$k", item.Key);
        cmd.Parameters.AddWithValue("$n", item.TargetName);
        cmd.Parameters.AddWithValue("$t", target);
        cmd.Parameters.AddWithValue("$a", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }
}
