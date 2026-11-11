using SkiaSharp;

namespace Fotoalbum.Core.Tests;

public class PhotoLibraryTests
{
    [Fact]
    public void Resolve_odmitne_cestu_mimo_koren()
    {
        using var t = new TempDir();
        var lib = new PhotoLibrary(t.Path);
        Assert.Throws<UnauthorizedAccessException>(() => lib.Resolve("../tajne.jpg"));
        Assert.Throws<UnauthorizedAccessException>(() => lib.Resolve("a/../../x"));
        Assert.Throws<UnauthorizedAccessException>(() => lib.Resolve(System.IO.Path.GetTempPath()));
        Assert.Equal(System.IO.Path.Combine(t.Path, "Keňa", "a.jpg"), lib.Resolve("Keňa/a.jpg"));
    }

    [Fact]
    public void Strom_vynecha_systemove_a_zalozni_slozky()
    {
        using var t = new TempDir();
        TestImages.Create(System.IO.Path.Combine(t.Path, "Afrika 1998", "Keňa", "a.jpg"));
        TestImages.Create(System.IO.Path.Combine(t.Path, "Afrika 1998", "Keňa", "b.JPG"));
        TestImages.Create(System.IO.Path.Combine(t.Path, "_originaly", "c.jpg"));
        TestImages.Create(System.IO.Path.Combine(t.Path, "@eaDir", "d.jpg"));
        TestImages.Create(System.IO.Path.Combine(t.Path, "Prázdná", "neni-fotka.jpg.txt"));

        var tree = new PhotoLibrary(t.Path).ScanTree();

        var afrika = Assert.Single(tree.Children);
        Assert.Equal("Afrika 1998", afrika.Name);
        Assert.Equal(2, afrika.TotalPhotos);
        Assert.Equal("Afrika 1998/Keňa", Assert.Single(afrika.Children).Path);
    }
}

public class ExifToolTests
{
    [Fact]
    public void Popisek_s_diakritikou_a_vice_radky_se_zapise_a_precte()
    {
        using var t = new TempDir();
        var file = TestImages.Create(System.IO.Path.Combine(t.Path, "Západ slunce.jpg"));
        const string caption = "Západ slunce nad Masai Marou v Keni.\nŘidič Joseph zastavil – „ať to vyfotím“.";

        Exif.Tool.WriteCaption(file, caption);

        var read = Assert.Single(Exif.Tool.Read([file]));
        Assert.Equal(caption, read.Caption);
        var raw = Exif.Raw("-charset", "iptc=utf8", "-s", "-s", "-s", "-XMP-dc:Description", "-IPTC:Caption-Abstract", "-IFD0:ImageDescription", file);
        Assert.Equal(3, raw.Split('\n').Count(l => l.StartsWith("Západ slunce nad Masai Marou")));
    }

    [Fact]
    public void Zapis_popisku_nemeni_obrazova_data()
    {
        using var t = new TempDir();
        var file = TestImages.Create(System.IO.Path.Combine(t.Path, "a.jpg"));
        using var before = TestImages.Decode(file);

        Exif.Tool.WriteCaption(file, "Sloni u napajedla");

        using var after = TestImages.Decode(file);
        Assert.Equal(before.Bytes, after.Bytes);
    }

    [Fact]
    public void Prazdny_popisek_odstrani_puvodni()
    {
        using var t = new TempDir();
        var file = TestImages.Create(System.IO.Path.Combine(t.Path, "a.jpg"));
        Exif.Tool.WriteCaption(file, "Něco");
        Exif.Tool.WriteCaption(file, "");
        Assert.Null(Assert.Single(Exif.Tool.Read([file])).Caption);
    }

    [Fact]
    public void Text_od_fotoaparatu_se_nepovazuje_za_popisek()
    {
        using var t = new TempDir();
        var file = TestImages.Create(System.IO.Path.Combine(t.Path, "a.jpg"));
        Exif.Raw("-overwrite_original", "-IFD0:ImageDescription=OLYMPUS DIGITAL CAMERA         ", file);
        Assert.Null(Assert.Single(Exif.Tool.Read([file])).Caption);
    }

    [Fact]
    public void Datum_porizeni_se_nacte()
    {
        using var t = new TempDir();
        var file = TestImages.Create(System.IO.Path.Combine(t.Path, "a.jpg"));
        Exif.Raw("-overwrite_original", "-ExifIFD:DateTimeOriginal=1998:07:14 18:42:10", file);
        Assert.Equal("1998-07-14T18:42:10", Assert.Single(Exif.Tool.Read([file])).DateTaken);
    }
}

public class ThumbnailTests
{
    [Fact]
    public async Task Nahled_je_zmenseny_a_v_mezipameti()
    {
        using var t = new TempDir();
        var file = TestImages.Create(System.IO.Path.Combine(t.Path, "a.jpg"), 1200, 800);
        var thumbs = new Thumbnails(t.Sub("cache"));

        var path = await thumbs.GetAsync(file, 300);
        using var bmp = TestImages.Decode(path);
        Assert.Equal(300, bmp.Width);
        Assert.Equal(200, bmp.Height);
        Assert.Equal(path, await thumbs.GetAsync(file, 300));
    }

    [Fact]
    public void Nahled_respektuje_exif_orientaci()
    {
        using var t = new TempDir();
        var file = TestImages.Create(System.IO.Path.Combine(t.Path, "a.jpg"), 400, 200);
        Exif.Raw("-overwrite_original", "-n", "-IFD0:Orientation=6", file); // otočit o 90° doprava

        using var bmp = TestImages.Decode(Thumbnails.Render(file, 400));

        Assert.Equal(200, bmp.Width);
        Assert.Equal(400, bmp.Height);
        // červený roh je po otočení doprava vpravo nahoře
        Assert.True(TestImages.IsRed(bmp.GetPixel(bmp.Width - 5, 5)));
        Assert.False(TestImages.IsRed(bmp.GetPixel(5, 5)));
    }
}

public class CatalogDbTests
{
    [Fact]
    public void Stavy_fotky()
    {
        Assert.Equal(PhotoStatus.None, new PhotoRecord { Path = "a" }.Status);
        Assert.Equal(PhotoStatus.Done, new PhotoRecord { Path = "a", Caption = "Sloni" }.Status);
        Assert.Equal(PhotoStatus.Draft, new PhotoRecord { Path = "a", Caption = "sloni ehm", IsDraft = true }.Status);
        Assert.Equal(PhotoStatus.Skip, new PhotoRecord { Path = "a", Skip = true }.Status);
        Assert.Equal(PhotoStatus.MarkedDelete, new PhotoRecord { Path = "a", Caption = "x", MarkedDelete = true }.Status);
    }

    [Fact]
    public void Rychle_po_sobe_jdouci_upravy_se_v_historii_slouci()
    {
        using var t = new TempDir();
        using var db = new CatalogDb(System.IO.Path.Combine(t.Path, "k.db"));
        db.SaveCaption("a.jpg", "Sl", false);
        db.SaveCaption("a.jpg", "Sloni", false);
        db.SaveCaption("a.jpg", "Sloni u vody", false);

        var h = db.GetHistory("a.jpg");
        Assert.Equal("Sloni u vody", Assert.Single(h).Caption);
        Assert.Equal("Sloni u vody", db.GetPhoto("a.jpg")!.Caption);
    }

    [Fact]
    public void Popisek_ze_souboru_neprepise_rozpracovanou_upravu()
    {
        using var t = new TempDir();
        using var db = new CatalogDb(System.IO.Path.Combine(t.Path, "k.db"));
        db.ApplyImport("a.jpg", "Původní", null, 1, 1);
        db.SaveCaption("a.jpg", "Nový text", false);
        db.ApplyImport("a.jpg", "Původní", null, 1, 1);
        Assert.Equal("Nový text", db.GetPhoto("a.jpg")!.Caption);
        Assert.True(db.GetPhoto("a.jpg")!.NeedsFileWrite);
    }

    [Fact]
    public void Zaloha_je_citelna_databaze()
    {
        using var t = new TempDir();
        using var db = new CatalogDb(System.IO.Path.Combine(t.Path, "k.db"));
        db.SaveCaption("a.jpg", "Sloni", false);
        var backup = System.IO.Path.Combine(t.Path, "zaloha", "k.db");
        db.BackupTo(backup);
        using var copy = new CatalogDb(backup);
        Assert.Equal("Sloni", copy.GetPhoto("a.jpg")!.Caption);
    }
}

public class FotoalbumServiceTests
{
    private static (TempDir T, FotoalbumService S) Setup()
    {
        var t = new TempDir();
        var photos = t.Sub("Fotky");
        TestImages.Create(System.IO.Path.Combine(photos, "Afrika", "IMG_2.jpg"));
        TestImages.Create(System.IO.Path.Combine(photos, "Afrika", "IMG_1.jpg"));
        TestImages.Create(System.IO.Path.Combine(photos, "Doma", "DSC_1.jpg"));
        Exif.Raw("-overwrite_original", "-XMP-dc:Description=Popisek z jiného programu", System.IO.Path.Combine(photos, "Afrika", "IMG_2.jpg"));
        Exif.Raw("-overwrite_original", "-ExifIFD:DateTimeOriginal=1998:07:14 10:00:00", System.IO.Path.Combine(photos, "Afrika", "IMG_2.jpg"));
        Exif.Raw("-overwrite_original", "-ExifIFD:DateTimeOriginal=1998:07:15 10:00:00", System.IO.Path.Combine(photos, "Afrika", "IMG_1.jpg"));
        var s = new FotoalbumService(new AppPaths(t.Sub("data")), writeDelay: TimeSpan.FromHours(1));
        s.Configure(photos, null);
        return (t, s);
    }

    [Fact]
    public void Otevreni_slozky_nacte_popisky_a_seradi_podle_data()
    {
        var (t, s) = Setup();
        using (t) using (s)
        {
            var f = s.OpenFolder("Afrika");
            Assert.Equal(["Afrika/IMG_2.jpg", "Afrika/IMG_1.jpg"], f.Photos.Select(p => p.Path));
            Assert.Equal("Popisek z jiného programu", f.Photos[0].Caption);
            Assert.Equal("done", f.Photos[0].Status);
            Assert.Equal("none", f.Photos[1].Status);
        }
    }

    [Fact]
    public void Ulozeny_popisek_se_zapise_do_fotky()
    {
        var (t, s) = Setup();
        using (t) using (s)
        {
            s.OpenFolder("Afrika");
            var dto = s.SaveCaption("Afrika/IMG_1.jpg", "Sloni u napajedla", false);
            Assert.True(dto.WritePending);

            s.Writer.FlushAll();

            var file = s.Library.Resolve("Afrika/IMG_1.jpg");
            Assert.Equal("Sloni u napajedla", Assert.Single(Exif.Tool.Read([file])).Caption);
            Assert.False(s.GetPhoto("Afrika/IMG_1.jpg").WritePending);
            // opětovné otevření složky nenačte nic jiného a popisek zůstane
            Assert.Equal("Sloni u napajedla", s.OpenFolder("Afrika").Photos.Single(p => p.Name == "IMG_1.jpg").Caption);
        }
    }

    [Fact]
    public void Rozpracovany_diktat_se_do_fotky_nezapisuje()
    {
        var (t, s) = Setup();
        using (t) using (s)
        {
            s.OpenFolder("Afrika");
            s.SaveCaption("Afrika/IMG_1.jpg", "to bylo v Keni, ne počkej", isDraft: true);
            s.Writer.FlushAll();
            Assert.Null(Assert.Single(Exif.Tool.Read([s.Library.Resolve("Afrika/IMG_1.jpg")])).Caption);
            Assert.Equal("draft", s.GetPhoto("Afrika/IMG_1.jpg").Status);
        }
    }

    [Fact]
    public void Oznacene_fotky_jdou_do_kose_jen_po_potvrzeni()
    {
        var (t, s) = Setup();
        using (t) using (s)
        {
            s.OpenFolder("Afrika");
            s.SetFlags("Afrika/IMG_1.jpg", null, true, null, null, false);
            Assert.Single(s.MarkedForDelete());

            Assert.Equal(0, s.ResolveMarked(moveToBin: false));
            Assert.Empty(s.MarkedForDelete());
            Assert.True(File.Exists(s.Library.Resolve("Afrika/IMG_1.jpg")));

            s.SetFlags("Afrika/IMG_1.jpg", null, true, null, null, false);
            Assert.Equal(1, s.ResolveMarked(moveToBin: true));
            Assert.False(File.Exists(s.Library.Resolve("Afrika/IMG_1.jpg")));
            Assert.Single(Directory.GetFiles(System.IO.Path.Combine(s.Library.Root, "_fotoalbum", "kos"), "IMG_1.jpg", SearchOption.AllDirectories));
            Assert.Single(s.OpenFolder("Afrika").Photos);
        }
    }

    [Fact]
    public void Druh_slozky_se_dedi_a_strom_pocita_vyrizene()
    {
        var (t, s) = Setup();
        using (t) using (s)
        {
            s.SetFolderKind("Doma", FolderKind.Everyday);
            s.OpenFolder("Afrika");
            s.SetFlags("Afrika/IMG_1.jpg", true, null, null, null, false);

            var tree = s.GetTree();
            var afrika = tree.Children.Single(c => c.Name == "Afrika");
            Assert.Equal((2, 2, 1), (afrika.Total, afrika.Handled, afrika.Done));
            Assert.Equal(FolderKind.Everyday, tree.Children.Single(c => c.Name == "Doma").Kind);
            Assert.True(s.OpenFolder("Doma").Photos.Single().Everyday);
        }
    }

    [Fact]
    public void Posledni_pozice_a_zaloha()
    {
        var (t, s) = Setup();
        using (t) using (s)
        {
            s.OpenFolder("Afrika");
            s.SetPosition("Afrika", "Afrika/IMG_1.jpg");
            Assert.Equal("Afrika/IMG_1.jpg", s.OpenFolder("Afrika").LastPhoto);
            Assert.Equal("Afrika", s.GetState().LastFolder);

            var backup = s.BackupCatalog();
            Assert.True(File.Exists(backup));
            Assert.StartsWith(System.IO.Path.Combine(s.Library.Root, "_fotoalbum", "zaloha"), backup);
        }
    }

    [Fact]
    public void Cesta_mimo_slozku_s_fotkami_je_odmitnuta()
    {
        var (t, s) = Setup();
        using (t) using (s)
        {
            Assert.Throws<UnauthorizedAccessException>(() => s.SaveCaption("../data/katalog.db", "x", false));
        }
    }
}
