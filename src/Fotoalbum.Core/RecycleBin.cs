namespace Fotoalbum.Core;

/// <summary>Bezpečné „smazání“ fotky. Nikdy nemaže natrvalo.</summary>
public interface IRecycleBin
{
    /// <summary>Přesune soubor do koše a vrátí popis, kam se přesunul.</summary>
    string MoveToBin(string fullPath);
}

/// <summary>
/// Náhradní koš pro prostředí bez koše Windows (vývoj, testy): soubor se přesune
/// do složky <c>_fotoalbum/kos</c> uvnitř složky s fotkami, se zachovanou cestou.
/// Ve Windows aplikaci se používá skutečný koš Windows.
/// </summary>
public sealed class FolderRecycleBin(string photoRoot) : IRecycleBin
{
    public string MoveToBin(string fullPath)
    {
        var rel = Path.GetRelativePath(photoRoot, fullPath);
        var target = Path.Combine(photoRoot, "_fotoalbum", "kos", DateTime.Now.ToString("yyyy-MM-dd_HHmmss"), rel);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(fullPath, target);
        return target;
    }
}
