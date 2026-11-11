using Fotoalbum.Core.Import;

namespace Fotoalbum.Core.Tests;

public class ImportTests
{
    [Theory]
    [InlineData(2026, 3, 1, "2026 jaro")]
    [InlineData(2026, 5, 31, "2026 jaro")]
    [InlineData(2026, 6, 1, "2026 léto")]
    [InlineData(2026, 8, 31, "2026 léto")]
    [InlineData(2026, 9, 15, "2026 podzim")]
    [InlineData(2026, 11, 30, "2026 podzim")]
    [InlineData(2026, 12, 24, "2026 zima")]
    [InlineData(2027, 1, 5, "2027 zima")]
    [InlineData(2027, 2, 28, "2027 zima")]
    public void Slozka_podle_rocniho_obdobi(int y, int m, int d, string expected) =>
        Assert.Equal(expected, Seasons.FolderName(new DateTime(y, m, d)));

    [Theory]
    [InlineData("100_PANA", true)]
    [InlineData("101_PANA", true)]
    [InlineData("100CANON", true)]
    [InlineData("100MSDCF", true)]
    [InlineData("999NIKON", true)]
    [InlineData("000_PANA", false)]
    [InlineData(".thumbnails", false)]
    [InlineData("Camera", false)]
    public void Pozna_slozky_fotoaparatu(string name, bool ok) => Assert.Equal(ok, CardImporter.IsDcfFolderName(name));

    private static string Photo(string path, string? taken)
    {
        TestImages.Create(path);
        if (taken is not null) Exif.Raw("-overwrite_original", "-ExifIFD:DateTimeOriginal=" + taken, path);
        return path;
    }

    private sealed class Card : IDisposable
    {
        public readonly TempDir T = new();
        public string Root => T.Sub("karta");
        public string Library => T.Sub("Fotky");
        public string Index => System.IO.Path.Combine(T.Path, "data", "import.db");
        public CardImporter Importer() => new(Library, Exif.Tool, Index);
        public void Dispose() => T.Dispose();
    }

    [Fact]
    public void Karta_bez_DCIM_neni_karta_z_fotoaparatu()
    {
        using var c = new Card();
        Photo(System.IO.Path.Combine(c.Root, "Dokumenty", "a.jpg"), null);
        Assert.Empty(CardImporter.FindCameraFolders(c.Root));
    }

    [Fact]
    public void Nove_fotky_se_zkopiruji_do_slozek_podle_obdobi_a_karta_zustane_beze_zmeny()
    {
        using var c = new Card();
        Photo(System.IO.Path.Combine(c.Root, "DCIM", "100_PANA", "P1000001.JPG"), "2026:07:14 18:00:00");
        Photo(System.IO.Path.Combine(c.Root, "DCIM", "101_PANA", "P1010001.JPG"), "2026:12:24 10:00:00");
        Photo(System.IO.Path.Combine(c.Root, "DCIM", "101_PANA", "P1010002.JPG"), "2027:01:02 10:00:00");
        TestImages.Create(System.IO.Path.Combine(c.Root, "DCIM", ".thumbnails", "x.jpg"));

        var imp = c.Importer();
        var plan = imp.Plan(c.Root);
        Assert.Equal(3, plan.Items.Count);
        var progress = new List<ImportProgress>();
        var result = imp.Execute(plan, new SyncProgress<ImportProgress>(progress.Add));

        Assert.Equal(3, result.Copied);
        Assert.Empty(result.Errors);
        Assert.Equal(["2026 léto", "2026 zima", "2027 zima"], result.Folders);
        Assert.True(File.Exists(System.IO.Path.Combine(c.Library, "2026 léto", "P1000001.JPG")));
        Assert.True(File.Exists(System.IO.Path.Combine(c.Library, "2027 zima", "P1010002.JPG")));
        Assert.True(File.Exists(System.IO.Path.Combine(c.Root, "DCIM", "100_PANA", "P1000001.JPG")));
        Assert.Equal(3, progress.Last().Done);
        Assert.Empty(Directory.GetFiles(c.Library, "*.kopiruje-se", SearchOption.AllDirectories));
    }

    [Fact]
    public void Podruhe_se_nic_nekopiruje_ani_po_doplneni_popisku_nebo_smazani_v_PC()
    {
        using var c = new Card();
        Photo(System.IO.Path.Combine(c.Root, "DCIM", "100_PANA", "P1000001.JPG"), "2026:07:14 18:00:00");
        Photo(System.IO.Path.Combine(c.Root, "DCIM", "100_PANA", "P1000002.JPG"), "2026:07:14 18:05:00");
        var imp = c.Importer();
        imp.Execute(imp.Plan(c.Root));

        // v PC: k jedné fotce doplněn popisek (soubor se změní), druhá smazána
        var lib1 = System.IO.Path.Combine(c.Library, "2026 léto", "P1000001.JPG");
        Exif.Tool.WriteCaption(lib1, "Západ slunce u chalupy");
        File.Delete(System.IO.Path.Combine(c.Library, "2026 léto", "P1000002.JPG"));

        var again = c.Importer().Plan(c.Root);
        Assert.Empty(again.Items);
        Assert.Equal(2, again.AlreadyPresent);
    }

    [Fact]
    public void Fotka_uz_v_knihovne_z_drivejska_se_nezkopiruje_znovu()
    {
        using var c = new Card();
        // stejná fotka už leží v knihovně jinde (ručně zkopírovaná dřív) a má popisek
        var existing = Photo(System.IO.Path.Combine(c.Library, "Dovolená", "P1000001.JPG"), "2026:07:14 18:00:00");
        Exif.Tool.WriteCaption(existing, "Už popsaná");
        Photo(System.IO.Path.Combine(c.Root, "DCIM", "100_PANA", "P1000001.JPG"), "2026:07:14 18:00:00");
        // jiná fotka se stejným názvem (počítadlo fotoaparátu přetočilo) se zkopírovat má
        Photo(System.IO.Path.Combine(c.Library, "Starší", "P1000002.JPG"), "2019:05:01 09:00:00");
        Photo(System.IO.Path.Combine(c.Root, "DCIM", "100_PANA", "P1000002.JPG"), "2026:07:15 09:00:00");

        var plan = c.Importer().Plan(c.Root);

        var item = Assert.Single(plan.Items);
        Assert.Equal("P1000002.JPG", item.TargetName);
        Assert.Equal(1, plan.AlreadyPresent);
    }

    [Fact]
    public void Jina_fotka_se_stejnym_nazvem_v_cilove_slozce_dostane_novy_nazev()
    {
        using var c = new Card();
        Photo(System.IO.Path.Combine(c.Library, "2026 léto", "P1000001.JPG"), null);
        Photo(System.IO.Path.Combine(c.Root, "DCIM", "100_PANA", "P1000001.JPG"), "2026:07:14 18:00:00");

        var imp = c.Importer();
        var result = imp.Execute(imp.Plan(c.Root));

        Assert.Equal(1, result.Copied);
        Assert.True(File.Exists(System.IO.Path.Combine(c.Library, "2026 léto", "P1000001 (2).JPG")));
    }

    /// <summary>IProgress bez synchronizačního kontextu (Progress&lt;T&gt; volá asynchronně).</summary>
    private sealed class SyncProgress<T>(Action<T> a) : IProgress<T> { public void Report(T v) => a(v); }
}
