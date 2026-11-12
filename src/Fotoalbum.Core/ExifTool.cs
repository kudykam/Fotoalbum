using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Fotoalbum.Core;

public sealed record FileMetadata(string Path, string? Caption, string? DateTaken);

/// <summary>
/// Čtení a zápis popisků přes ExifTool. Zápis mění jen metadata, obrazová data zůstávají beze změny.
/// Popisek se zapisuje do XMP dc:description, IPTC Caption-Abstract a EXIF ImageDescription (UTF-8).
/// </summary>
public sealed class ExifTool
{
    /// <summary>Texty, které do popisu fotky dávají fotoaparáty samy – nejsou to skutečné popisky.</summary>
    private static readonly HashSet<string> CameraJunk = new(StringComparer.OrdinalIgnoreCase)
    {
        "OLYMPUS DIGITAL CAMERA", "SONY DSC", "DIGITAL CAMERA", "KONICA MINOLTA DIGITAL CAMERA", "MINOLTA DIGITAL CAMERA",
        "SAMSUNG", "SAMSUNG CAMERA PICTURES", "Exif_JPEG_PICTURE", "EXIF_JPEG_PICTURE", "DCIM", "default", "Default",
        "LEICA", "Created with GIMP", "Picasa", "image", "Image", "NIKON", "PENTAX", "Untitled", "<unknown>", "unknown",
    };

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    public ExifTool(string executable) => Executable = executable;

    public string Executable { get; }

    /// <summary>Najde ExifTool: zadaná cesta, složka aplikace, PATH. Vrátí null, když není k dispozici.</summary>
    public static ExifTool? Locate(string? configuredPath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredPath)) candidates.Add(configuredPath);
        var baseDir = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDir, "exiftool", "exiftool.exe"));
        candidates.Add(Path.Combine(baseDir, "exiftool.exe"));
        candidates.Add(OperatingSystem.IsWindows() ? "exiftool.exe" : "exiftool");
        foreach (var c in candidates)
        {
            var tool = new ExifTool(c);
            if (tool.TryVersion() is not null) return tool;
        }
        return null;
    }

    public string? TryVersion()
    {
        try
        {
            var (code, output, _) = Run(["-ver"], TimeSpan.FromSeconds(15));
            return code == 0 ? output.Trim() : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Načte popisky a datum pořízení pro více souborů jedním spuštěním.</summary>
    public List<FileMetadata> Read(IReadOnlyCollection<string> fullPaths)
    {
        var result = new List<FileMetadata>();
        if (fullPaths.Count == 0) return result;
        foreach (var chunk in fullPaths.Chunk(300))
        {
            var args = new List<string>
            {
                "-j", "-G1", "-m", "-fast2",
                "-charset", "filename=utf8", "-charset", "exif=utf8", "-charset", "iptc=utf8",
                "-d", "%Y-%m-%dT%H:%M:%S",
                "-XMP-dc:Description", "-IPTC:Caption-Abstract", "-IFD0:ImageDescription", "-ExifIFD:DateTimeOriginal",
            };
            args.AddRange(chunk);
            var (_, output, _) = Run(args, Timeout);
            if (string.IsNullOrWhiteSpace(output)) continue;
            using var doc = JsonDocument.Parse(output);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var file = Str(el, "SourceFile");
                if (file is null) continue;
                var caption = Clean(Str(el, "XMP-dc:Description")) ?? Clean(Str(el, "IPTC:Caption-Abstract")) ?? Clean(Str(el, "IFD0:ImageDescription"));
                var date = Str(el, "ExifIFD:DateTimeOriginal");
                if (date is not null && !DateTime.TryParse(date, out _)) date = null;
                result.Add(new FileMetadata(Path.GetFullPath(file), caption, date));
            }
        }
        return result;
    }

    /// <summary>
    /// Datum a čas pořízení fotek i videí: DateTimeOriginal (fotky, AVCHD), jinak CreateDate (MP4/MOV,
    /// převedeno z UTC na místní čas). Soubory bez data chybí ve výsledku.
    /// </summary>
    public Dictionary<string, DateTime> ReadTakenDates(IReadOnlyCollection<string> fullPaths)
    {
        var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in fullPaths.Chunk(300))
        {
            var args = new List<string>
            {
                "-j", "-m", "-fast2", "-api", "QuickTimeUTC", "-charset", "filename=utf8",
                "-d", "%Y-%m-%dT%H:%M:%S", "-DateTimeOriginal", "-CreateDate",
            };
            args.AddRange(chunk);
            var (_, output, _) = Run(args, Timeout);
            if (string.IsNullOrWhiteSpace(output)) continue;
            using var doc = JsonDocument.Parse(output);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var file = Str(el, "SourceFile");
                if (file is null) continue;
                foreach (var tag in new[] { "DateTimeOriginal", "CreateDate" })
                    if (DateTime.TryParse(Str(el, tag), out var d) && d.Year > 1980) { result[Path.GetFullPath(file)] = d; break; }
            }
        }
        return result;
    }

    /// <summary>
    /// Zapíše popisek do fotky (prázdný text popisky odstraní).
    /// Celý popisek: XMP dc:description, IPTC Caption-Abstract, EXIF ImageDescription, Windows „Předmět“ a „Komentáře“.
    /// Krátký nadpis: XMP dc:title, IPTC ObjectName a Windows „Název“ (XPTitle).
    /// </summary>
    public void WriteCaption(string fullPath, string caption, string? title = null)
    {
        var value = EscapeC(caption.Trim());
        var t = EscapeC((title ?? CaptionTitle.From(caption) ?? "").Trim());
        var args = new List<string>
        {
            "-overwrite_original", "-m", "-ec",
            "-charset", "filename=utf8", "-charset", "exif=utf8", "-charset", "iptc=utf8",
            "-IPTC:CodedCharacterSet=UTF8",
            "-XMP-dc:Description=" + value,
            "-IPTC:Caption-Abstract=" + value,
            "-IFD0:ImageDescription=" + value,
            "-IFD0:XPSubject=" + value,
            "-IFD0:XPComment=" + value,
            "-XMP-dc:Title=" + t,
            "-IPTC:ObjectName=" + t,
            "-IFD0:XPTitle=" + t,
            fullPath,
        };
        var (code, output, error) = Run(args, Timeout);
        if (code != 0 || !output.Contains("1 image files updated") && !output.Contains("1 image files unchanged"))
            throw new IOException("Popisek se nepodařilo zapsat do fotky: " + (error.Trim().Length > 0 ? error.Trim() : output.Trim()));
    }

    public static string? Clean(string? s)
    {
        if (s is null) return null;
        var t = s.Replace("\r\n", "\n").Trim().Trim('\0').Trim();
        if (t.Length == 0 || CameraJunk.Contains(t)) return null;
        return t;
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.ToString(),
            _ => null,
        } : null;

    /// <summary>Hodnota pro volbu -ec: zpětné lomítko a konce řádků se zapíší jako C escape sekvence.</summary>
    private static string EscapeC(string s) => s.Replace("\\", "\\\\").Replace("\r\n", "\n").Replace("\n", "\\n");

    /// <summary>Spustí ExifTool; argumenty předává souborem (UTF-8), aby fungovala diakritika v cestách i textech.</summary>
    private (int Code, string Output, string Error) Run(IReadOnlyList<string> args, TimeSpan timeout)
    {
        string? argFile = null;
        var psi = new ProcessStartInfo(Executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (args.Count == 1 && args[0] == "-ver")
        {
            psi.ArgumentList.Add("-ver");
        }
        else
        {
            argFile = Path.Combine(Path.GetTempPath(), "fotoalbum-exif-" + Guid.NewGuid().ToString("N") + ".args");
            File.WriteAllLines(argFile, args, new UTF8Encoding(false));
            psi.ArgumentList.Add("-@");
            psi.ArgumentList.Add(argFile);
        }
        try
        {
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("ExifTool se nespustil.");
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeout))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* proces už skončil */ }
                throw new TimeoutException("ExifTool neodpověděl včas.");
            }
            return (p.ExitCode, outTask.Result, errTask.Result);
        }
        finally
        {
            if (argFile is not null) try { File.Delete(argFile); } catch { /* dočasný soubor */ }
        }
    }
}
