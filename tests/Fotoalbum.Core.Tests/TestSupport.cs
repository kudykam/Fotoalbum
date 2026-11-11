using SkiaSharp;

namespace Fotoalbum.Core.Tests;

/// <summary>Dočasná složka s vygenerovanými testovacími JPEGy (žádné skutečné fotky).</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fotoalbum-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Sub(params string[] parts)
    {
        var p = System.IO.Path.Combine([Path, .. parts]);
        Directory.CreateDirectory(p);
        return p;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

public static class TestImages
{
    /// <summary>Vytvoří JPEG: modrá plocha s červeným čtvercem v levém horním rohu (kvůli kontrole orientace).</summary>
    public static string Create(string path, int width = 300, int height = 200)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var bmp = new SKBitmap(width, height);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(new SKColor(30, 80, 200));
            using var red = new SKPaint { Color = new SKColor(230, 20, 20) };
            c.DrawRect(0, 0, width / 4f, height / 4f, red);
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Jpeg, 90);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    public static SKBitmap Decode(byte[] jpeg) => SKBitmap.Decode(jpeg);

    public static SKBitmap Decode(string path) => SKBitmap.Decode(path);

    public static bool IsRed(SKColor c) => c.Red > 150 && c.Green < 100 && c.Blue < 100;
}

public static class Exif
{
    public static ExifTool Tool => ExifTool.Locate(null)
        ?? throw new InvalidOperationException("Testy potřebují ExifTool (Linux: apt install libimage-exiftool-perl).");

    /// <summary>Spustí exiftool přímo (pro ověření, co je skutečně v souboru).</summary>
    public static string Raw(params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(Tool.Executable)
        {
            RedirectStandardOutput = true, UseShellExecute = false,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return o;
    }
}
