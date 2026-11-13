using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace Fotoalbum.Core;

/// <summary>Zmenšené náhledy fotek v mezipaměti aplikace. Originály se nikdy nemění.</summary>
public sealed class Thumbnails(string cacheDir)
{
    private readonly SemaphoreSlim _gate = new(Math.Max(2, Environment.ProcessorCount / 2));

    /// <summary>Vrátí cestu k náhledu (JPEG) s delší stranou <paramref name="maxSize"/> px; vytvoří ho, pokud chybí.</summary>
    public async Task<string> GetAsync(string fullPath, int maxSize, CancellationToken ct = default)
    {
        var fi = new FileInfo(fullPath);
        if (!fi.Exists) throw new FileNotFoundException("Fotka neexistuje.", fullPath);
        var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(
            $"{fi.FullName}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}|{maxSize}"))).ToLowerInvariant();
        var target = Path.Combine(cacheDir, key[..2], key + ".jpg");
        if (File.Exists(target)) return target;

        await _gate.WaitAsync(ct);
        try
        {
            if (File.Exists(target)) return target;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            ct.ThrowIfCancellationRequested();
            var bytes = Render(fullPath, maxSize);
            ct.ThrowIfCancellationRequested();
            var tmp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(tmp, bytes, ct);
                File.Move(tmp, target, overwrite: true);
                return target;
            }
            finally { File.Delete(tmp); }
        }
        finally { _gate.Release(); }
    }

    public static byte[] Render(string fullPath, int maxSize)
    {
        using var stream = File.OpenRead(fullPath);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Soubor není čitelný obrázek.");
        var info = codec.Info;
        var scale = Math.Min(1f, (float)maxSize / Math.Max(info.Width, info.Height));
        // JPEG umí dekódovat rovnou zmenšený obraz (1/2, 1/4, 1/8) – výrazně rychlejší než plná velikost.
        var scaled = codec.GetScaledDimensions(scale);
        var decodeInfo = new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var decoded = new SKBitmap(decodeInfo);
        var res = codec.GetPixels(decodeInfo, decoded.GetPixels());
        if (res is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            throw new InvalidDataException("Fotku se nepodařilo načíst: " + res);

        var w = Math.Max(1, (int)Math.Round(info.Width * scale));
        var h = Math.Max(1, (int)Math.Round(info.Height * scale));
        using var resized = decoded.Width == w && decoded.Height == h
            ? decoded.Copy()
            : decoded.Resize(new SKImageInfo(w, h), new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var oriented = ApplyOrigin(resized, codec.EncodedOrigin);
        using var image = SKImage.FromBitmap(oriented);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 84);
        return data.ToArray();
    }

    /// <summary>Otočí obraz podle EXIF orientace, aby náhled odpovídal tomu, jak fotku ukazují prohlížeče.</summary>
    private static SKBitmap ApplyOrigin(SKBitmap src, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return src.Copy();
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var dst = new SKBitmap(swap ? src.Height : src.Width, swap ? src.Width : src.Height);
        using var c = new SKCanvas(dst);
        var w = src.Width; var h = src.Height;
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: c.Scale(-1, 1); c.Translate(-w, 0); break;
            case SKEncodedOrigin.BottomRight: c.RotateDegrees(180); c.Translate(-w, -h); break;
            case SKEncodedOrigin.BottomLeft: c.Scale(1, -1); c.Translate(0, -h); break;
            case SKEncodedOrigin.LeftTop: c.Scale(-1, 1); c.RotateDegrees(90); break;
            case SKEncodedOrigin.RightTop: c.Translate(h, 0); c.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: c.Scale(-1, 1); c.Translate(-h, w); c.RotateDegrees(-90); break;
            case SKEncodedOrigin.LeftBottom: c.Translate(0, w); c.RotateDegrees(-90); break;
        }
        using var img = SKImage.FromBitmap(src);
        c.DrawImage(img, 0, 0, SKSamplingOptions.Default);
        return dst;
    }
}
