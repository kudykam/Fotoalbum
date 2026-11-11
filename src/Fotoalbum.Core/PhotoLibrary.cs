namespace Fotoalbum.Core;

/// <summary>
/// Práce se složkou fotek. Všechny cesty navenek jsou relativní ke kořeni a používají '/'.
/// Každá cesta z venku prochází <see cref="Resolve"/>, aby nešlo vystoupit mimo kořen.
/// </summary>
public sealed class PhotoLibrary
{
    /// <summary>Složky, které aplikace nezobrazuje (zálohy, systémové složky Synology, koš).</summary>
    private static readonly HashSet<string> IgnoredDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "@eaDir", "#recycle", "#snapshot", "$RECYCLE.BIN", "System Volume Information",
    };

    public PhotoLibrary(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Chybí složka s fotkami.");
        Root = Path.GetFullPath(root);
        if (!Directory.Exists(Root)) throw new DirectoryNotFoundException($"Složka {Root} neexistuje.");
    }

    public string Root { get; }

    public static bool IsPhotoFile(string name)
    {
        var ext = Path.GetExtension(name);
        return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsIgnoredDir(string name) =>
        name.StartsWith('.') || name.StartsWith('_') || IgnoredDirs.Contains(name);

    /// <summary>Převede relativní cestu na absolutní a ověří, že leží uvnitř kořene.</summary>
    public string Resolve(string relative)
    {
        relative ??= "";
        if (relative.Contains('\0')) throw new UnauthorizedAccessException("Neplatná cesta.");
        var combined = Path.GetFullPath(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSep = Root.EndsWith(Path.DirectorySeparatorChar) ? Root : Root + Path.DirectorySeparatorChar;
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!combined.Equals(Root, cmp) && !combined.StartsWith(rootWithSep, cmp))
            throw new UnauthorizedAccessException("Cesta mimo složku s fotkami.");
        return combined;
    }

    public string ToRelative(string full)
    {
        var r = Path.GetRelativePath(Root, full).Replace(Path.DirectorySeparatorChar, '/');
        return r == "." ? "" : r;
    }

    public FolderNode ScanTree()
    {
        var root = new FolderNode("", Path.GetFileName(Root.TrimEnd(Path.DirectorySeparatorChar)));
        Fill(root, Root);
        return root;
    }

    private void Fill(FolderNode node, string dir)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir))
                if (IsPhotoFile(f)) node.PhotoPaths.Add(ToRelative(f));
            foreach (var d in Directory.EnumerateDirectories(dir).Order(StringComparer.CurrentCultureIgnoreCase))
            {
                var name = Path.GetFileName(d);
                if (IsIgnoredDir(name)) continue;
                var child = new FolderNode(ToRelative(d), name);
                Fill(child, d);
                if (child.TotalPhotos > 0) node.Children.Add(child);
            }
        }
        catch (UnauthorizedAccessException) { /* nepřístupná složka se přeskočí */ }
        catch (IOException) { }
    }

    public List<string> ListPhotos(string folderRelative)
    {
        var dir = Resolve(folderRelative);
        return Directory.EnumerateFiles(dir)
            .Where(IsPhotoFile)
            .Select(ToRelative)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}

public sealed class FolderNode(string path, string name)
{
    public string Path { get; } = path;
    public string Name { get; } = name;
    public List<string> PhotoPaths { get; } = [];
    public List<FolderNode> Children { get; } = [];
    public int TotalPhotos => PhotoPaths.Count + Children.Sum(c => c.TotalPhotos);
    public IEnumerable<string> AllPhotoPaths() => PhotoPaths.Concat(Children.SelectMany(c => c.AllPhotoPaths()));
}
