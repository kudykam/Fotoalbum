namespace Fotoalbum.Core;

/// <summary>
/// Krátký nadpis fotky (Windows „Název“, záhlaví fotky ve Fotoalbu).
/// Zatím první věta popisku; v etapě 2 ho bude navrhovat AI při korektuře.
/// </summary>
public static class CaptionTitle
{
    public const int MaxLength = 80;

    public static string? From(string? caption)
    {
        if (string.IsNullOrWhiteSpace(caption)) return null;
        var text = caption.Replace('\n', ' ').Trim();
        var end = FirstSentenceEnd(text);
        var sentence = (end > 0 ? text[..end] : text).Trim().TrimEnd('.', ':', ';', ',').Trim();
        if (sentence.Length <= MaxLength) return sentence;
        var cut = sentence.LastIndexOf(' ', MaxLength - 1);
        return (cut > 30 ? sentence[..cut] : sentence[..(MaxLength - 1)]).TrimEnd(',', ';', ':', '–', '-', ' ') + "…";
    }

    /// <summary>Konec první věty: tečka/vykřičník/otazník následovaný mezerou a velkým písmenem.</summary>
    private static int FirstSentenceEnd(string t)
    {
        for (var i = 0; i < t.Length - 2; i++)
        {
            if (t[i] is '.' or '!' or '?' && t[i + 1] == ' ' && char.IsUpper(t[i + 2]))
                return i + 1;
        }
        return -1;
    }
}
