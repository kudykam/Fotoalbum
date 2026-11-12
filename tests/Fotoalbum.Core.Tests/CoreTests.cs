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
        TestImages.Create(System.IO.Path.Combine(t.Path, PhotoLibrary.DatabaseFolder, "kos", "x.jpg"));
        TestImages.Create(System.IO.Path.Combine(t.Path, PhotoLibrary.OriginalsFolder, "y.jpg"));
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
            s.SetFlags("Afrika/IMG_1.jpg", null, true, null);
            Assert.Single(s.MarkedForDelete());

            Assert.Equal(0, s.ResolveMarked(moveToBin: false));
            Assert.Empty(s.MarkedForDelete());
            Assert.True(File.Exists(s.Library.Resolve("Afrika/IMG_1.jpg")));

            s.SetFlags("Afrika/IMG_1.jpg", null, true, null);
            Assert.Equal(1, s.ResolveMarked(moveToBin: true));
            Assert.False(File.Exists(s.Library.Resolve("Afrika/IMG_1.jpg")));
            Assert.Single(Directory.GetFiles(System.IO.Path.Combine(s.Library.Root, PhotoLibrary.DatabaseFolder, "kos"), "IMG_1.jpg", SearchOption.AllDirectories));
            Assert.Single(s.OpenFolder("Afrika").Photos);
        }
    }

    [Fact]
    public void Druh_slozky_se_dedi_a_strom_pocita_vyrizene()
    {
        var (t, s) = Setup();
        using (t) using (s)
        {
            s.SetFolderKind("Afrika", FolderKind.Vacation);
            s.OpenFolder("Afrika");
            s.SetFlags("Afrika/IMG_1.jpg", true, null, null);

            var tree = s.GetTree();
            var afrika = tree.Children.Single(c => c.Name == "Afrika");
            Assert.Equal((2, 2, 1), (afrika.Total, afrika.Handled, afrika.Done));
            // výchozí druh je běžná složka, dovolenou označí uživatel
            Assert.Equal(FolderKind.Everyday, tree.Children.Single(c => c.Name == "Doma").Kind);
            Assert.Equal(FolderKind.Vacation, afrika.Kind);
            Assert.False(s.OpenFolder("Doma").Photos.Single().Vacation);
            Assert.True(s.OpenFolder("Afrika").Photos.First().Vacation);
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
            Assert.StartsWith(System.IO.Path.Combine(s.Library.Root, PhotoLibrary.DatabaseFolder, "zaloha"), backup);
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

public class Stage1FeedbackTests
{
    [Theory]
    [InlineData("Západ slunce nad Masai Marou v Keni. Řidič zastavil.", "Západ slunce nad Masai Marou v Keni")]
    [InlineData("Sloni u napajedla", "Sloni u napajedla")]
    [InlineData("Ve 3. dnu jsme dojeli. Pak pršelo.", "Ve 3. dnu jsme dojeli")]
    [InlineData("", null)]
    public void Nadpis_je_prvni_veta(string caption, string? expected) => Assert.Equal(expected, CaptionTitle.From(caption));

    [Fact]
    public void Dlouhy_nadpis_se_zkrati_na_cela_slova()
    {
        var t = CaptionTitle.From(string.Join(' ', Enumerable.Repeat("slovo", 40)))!;
        Assert.True(t.Length <= CaptionTitle.MaxLength);
        Assert.EndsWith("slovo…", t);
    }

    [Fact]
    public void Do_fotky_se_zapise_nadpis_a_cely_popisek_do_poli_Windows()
    {
        using var t = new TempDir();
        var file = TestImages.Create(System.IO.Path.Combine(t.Path, "a.jpg"));
        Exif.Tool.WriteCaption(file, "Západ slunce nad Masai Marou. Řidič Joseph zastavil.");
        var raw = Exif.Raw("-s", "-s", "-XPTitle", "-XPSubject", "-XPComment", "-XMP-dc:Title", "-ImageDescription", file);
        Assert.Contains("XPTitle: Západ slunce nad Masai Marou", raw);
        Assert.Contains("XPSubject: Západ slunce nad Masai Marou. Řidič Joseph zastavil.", raw);
        Assert.Contains("XPComment: Západ slunce nad Masai Marou. Řidič Joseph zastavil.", raw);
        Assert.Contains("ImageDescription: Západ slunce nad Masai Marou. Řidič Joseph zastavil.", raw);
        // popisek se čte z plného popisu, ne z nadpisu
        Assert.Equal("Západ slunce nad Masai Marou. Řidič Joseph zastavil.", Exif.Tool.Read([file]).Single().Caption);
    }

    [Fact]
    public void Stara_servisni_slozka_se_prejmenuje_jen_kdyz_je_nase()
    {
        using var t = new TempDir();
        var ours = t.Sub("A", "_fotoalbum", "zaloha");
        File.WriteAllText(System.IO.Path.Combine(ours, "katalog-2026-11-11_120000.db"), "x");
        t.Sub("B", "_fotoalbum", "neco-ciziho");

        new PhotoLibrary(System.IO.Path.Combine(t.Path, "A")).MigrateServiceFolders();
        new PhotoLibrary(System.IO.Path.Combine(t.Path, "B")).MigrateServiceFolders();

        Assert.True(Directory.Exists(System.IO.Path.Combine(t.Path, "A", PhotoLibrary.DatabaseFolder, "zaloha")));
        Assert.False(Directory.Exists(System.IO.Path.Combine(t.Path, "A", "_fotoalbum")));
        Assert.True(Directory.Exists(System.IO.Path.Combine(t.Path, "B", "_fotoalbum")));
    }

    [Theory]
    [InlineData("Afrika 1998/Keňa", "Afrika 1998 · Keňa")]
    [InlineData("2026 léto", "2026 léto")]
    [InlineData("", "Všechny fotky")]
    public void Nazev_alba(string folder, string expected) => Assert.Equal(expected, FotoalbumService.AlbumName(folder));

    [Fact]
    public void Starsi_hodnota_cestovni_znamena_dovolenou()
    {
        Assert.Equal(FolderKind.Vacation, FolderKind.Normalize("cestovni"));
        Assert.Null(FolderKind.Normalize("nesmysl"));
    }
}

public class IptcLimitTests
{
    [Fact]
    public void Dlouhy_popisek_se_do_IPTC_zkrati_ale_jinde_zustane_cely()
    {
        using var t = new TempDir();
        var file = TestImages.Create(System.IO.Path.Combine(t.Path, "a.jpg"));
        var longText = string.Join(" ", Enumerable.Repeat("Západ slunce nad Masai Marou, řidič zastavil u akácie.", 60));
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(longText) > 2000);

        Exif.Tool.WriteCaption(file, longText);

        var iptc = Exif.Raw("-charset", "iptc=utf8", "-s3", "-IPTC:Caption-Abstract", file).TrimEnd('\n', '\r');
        var title = Exif.Raw("-charset", "iptc=utf8", "-s3", "-IPTC:ObjectName", file).TrimEnd('\n', '\r');
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(iptc) <= 2000);
        Assert.EndsWith("…", iptc);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(title) <= 64);
        Assert.Equal(longText, Exif.Tool.Read([file]).Single().Caption); // čte se celý (XMP)
        Assert.Equal(longText, Exif.Raw("-s3", "-IFD0:XPSubject", file).TrimEnd('\n', '\r'));
    }

    [Fact]
    public void Utf8Limit_neroztrhne_znak()
    {
        var s = ExifTool.Utf8Limit(new string('ř', 50), 64);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(s) <= 64);
        Assert.EndsWith("…", s);
    }
}

public class RenameTests
{
    [Fact]
    public void Servisni_slozka_z_doby_Fotoalba_se_prejmenuje()
    {
        using var t = new TempDir();
        t.Sub("A", PhotoLibrary.PreviousDatabaseFolder, "zaloha");
        var lib = new PhotoLibrary(System.IO.Path.Combine(t.Path, "A"));
        lib.MigrateServiceFolders();
        Assert.True(Directory.Exists(System.IO.Path.Combine(lib.DatabaseDir, "zaloha")));
        Assert.False(Directory.Exists(System.IO.Path.Combine(t.Path, "A", PhotoLibrary.PreviousDatabaseFolder)));
        Assert.True(PhotoLibrary.IsIgnoredDir(PhotoLibrary.PreviousDatabaseFolder));
    }

    [Fact]
    public void Servisni_slozka_Fotokronikare_se_prejmenuje_na_Fotokroniku()
    {
        using var t = new TempDir();
        t.Sub("A", PhotoLibrary.KronikarDatabaseFolder, "zaloha");
        var lib = new PhotoLibrary(System.IO.Path.Combine(t.Path, "A"));
        lib.MigrateServiceFolders();
        Assert.EndsWith("Fotokronika – databáze (nemazat)", lib.DatabaseDir);
        Assert.True(Directory.Exists(System.IO.Path.Combine(lib.DatabaseDir, "zaloha")));
        Assert.False(Directory.Exists(System.IO.Path.Combine(t.Path, "A", PhotoLibrary.KronikarDatabaseFolder)));
        Assert.True(PhotoLibrary.IsIgnoredDir(PhotoLibrary.KronikarDatabaseFolder));
    }
}
