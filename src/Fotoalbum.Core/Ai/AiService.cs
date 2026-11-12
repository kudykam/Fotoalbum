using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fotoalbum.Core.Ai;

/// <summary>Uložení API klíče mimo repozitář (Windows: šifrovaně přes DPAPI).</summary>
public interface ISecretStore
{
    string? Get(string name);
    void Set(string name, string? value);
}

/// <summary>
/// Náhradní úložiště pro vývoj a testy (soubor v datech aplikace, jen pro aktuálního uživatele).
/// Ve Windows aplikaci se používá šifrování DPAPI.
/// </summary>
public sealed class FileSecretStore(string dir) : ISecretStore
{
    private string FileOf(string name) => Path.Combine(dir, "tajne-" + name + ".txt");

    public string? Get(string name) => File.Exists(FileOf(name)) ? File.ReadAllText(FileOf(name)).Trim() : null;

    public void Set(string name, string? value)
    {
        Directory.CreateDirectory(dir);
        if (string.IsNullOrWhiteSpace(value)) { if (File.Exists(FileOf(name))) File.Delete(FileOf(name)); return; }
        File.WriteAllText(FileOf(name), value.Trim());
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(FileOf(name), UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}

public sealed record AiModel(string Id, string Name, bool Audio, double? PromptPricePerM, double? CompletionPricePerM);

/// <summary>Kontext pro korekturu: co AI o fotce smí vědět (žádná fotka, jen text).</summary>
public sealed record CaptionContext(string Album, string? Date, IReadOnlyList<string> NeighbourCaptions, string Glossary, string Length);

public sealed record ImproveResult(string Caption, string Title, double CostUsd);

public sealed class AiException(string message) : Exception(message);

/// <summary>
/// Přepis řeči a korektura popisků přes OpenRouter (specifikace 3.4).
/// Do cloudu jde jen zvuk diktátu a text; fotky nikdy.
/// </summary>
public sealed class AiService
{
    public const string KeyName = "openrouter";
    public const string DefaultTextModel = "anthropic/claude-haiku-5.5";
    /// <summary>Preferované modely pro přepis češtiny (první dostupný se použije, dokud správce nevybere jiný).</summary>
    public static readonly string[] PreferredAudioModels =
        ["google/gemini-2.5-flash", "google/gemini-flash-latest", "openai/gpt-4o-audio-preview"];

    private static readonly Uri BaseUri = new("https://openrouter.ai/api/v1/");
    private readonly HttpClient _http;
    private readonly ISecretStore _secrets;
    private readonly Func<AppSettings> _settings;
    private readonly Action<double> _addCost;
    private List<AiModel>? _models;
    private DateTime _modelsAt;

    public AiService(HttpClient http, ISecretStore secrets, Func<AppSettings> settings, Action<double> addCost)
    {
        _http = http;
        _secrets = secrets;
        _settings = settings;
        _addCost = addCost;
        if (_http.BaseAddress is null) _http.BaseAddress = BaseUri;
        _http.Timeout = TimeSpan.FromSeconds(90);
    }

    public bool HasKey => !string.IsNullOrWhiteSpace(_secrets.Get(KeyName));

    /// <summary>Konec klíče pro zobrazení (…a1b2), nikdy celý klíč.</summary>
    public string? KeyHint { get { var k = _secrets.Get(KeyName); return k is { Length: > 4 } ? "…" + k[^4..] : null; } }

    public void SetKey(string? key) => _secrets.Set(KeyName, key);

    // ---------- modely ----------

    /// <summary>Živý seznam modelů OpenRouteru (veřejný, bez klíče); hodinu v paměti.</summary>
    public async Task<List<AiModel>> ListModelsAsync(CancellationToken ct = default)
    {
        if (_models is not null && DateTime.UtcNow - _modelsAt < TimeSpan.FromHours(1)) return _models;
        using var res = await _http.GetAsync("models", ct);
        res.EnsureSuccessStatusCode();
        var root = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct))!;
        var list = new List<AiModel>();
        foreach (var m in root["data"]?.AsArray() ?? [])
        {
            if (m is null) continue;
            var id = m["id"]?.GetValue<string>();
            if (id is null) continue;
            var inputs = m["architecture"]?["input_modalities"]?.AsArray().Select(x => x?.GetValue<string>()).ToList() ?? [];
            list.Add(new AiModel(id, m["name"]?.GetValue<string>() ?? id, inputs.Contains("audio"),
                PricePerM(m["pricing"]?["prompt"]), PricePerM(m["pricing"]?["completion"])));
        }
        _models = list;
        _modelsAt = DateTime.UtcNow;
        return list;
    }

    private static double? PricePerM(JsonNode? n) =>
        n is not null && double.TryParse(n.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v * 1_000_000 : null;

    public async Task<string> TextModelAsync(CancellationToken ct = default)
    {
        var chosen = _settings().AiTextModel;
        if (!string.IsNullOrWhiteSpace(chosen)) return chosen;
        try
        {
            var models = await ListModelsAsync(ct);
            if (models.Any(m => m.Id == DefaultTextModel)) return DefaultTextModel;
            // novější/starší Haiku, pokud se identifikátor změnil
            return models.Where(m => m.Id.StartsWith("anthropic/claude-haiku", StringComparison.Ordinal))
                .Select(m => m.Id).OrderDescending().FirstOrDefault() ?? DefaultTextModel;
        }
        catch (HttpRequestException) { return DefaultTextModel; }
    }

    public async Task<string> AudioModelAsync(CancellationToken ct = default)
    {
        var chosen = _settings().AiAudioModel;
        if (!string.IsNullOrWhiteSpace(chosen)) return chosen;
        var models = await ListModelsAsync(ct);
        foreach (var p in PreferredAudioModels) if (models.Any(m => m.Id == p && m.Audio)) return p;
        return models.FirstOrDefault(m => m.Audio && m.Id.StartsWith("google/gemini", StringComparison.Ordinal))?.Id
            ?? models.FirstOrDefault(m => m.Audio)?.Id
            ?? throw new AiException("OpenRouter teď nenabízí žádný model pro přepis řeči.");
    }

    // ---------- přepis řeči ----------

    /// <summary>Přepíše úsek diktátu (WAV, base64). <paramref name="previousText"/> pomáhá navázat na předchozí větu.</summary>
    public async Task<string> TranscribeAsync(string wavBase64, string previousText, CancellationToken ct = default)
    {
        var glossary = _settings().Glossary?.Trim();
        var instruction = """
            Přepiš přesně tuto českou nahrávku do textu. Píše se popisek k fotografii, mluví starší pán.
            Zapiš doslova, co zazní, včetně opakování a oprav (nic neupravuj, nevynechávej, nepřidávej).
            Používej správnou českou diakritiku a interpunkci. Vrať jen přepsaný text, bez uvozovek a komentářů.
            Pokud v nahrávce není žádná řeč, vrať prázdný text.
            """;
        if (!string.IsNullOrEmpty(glossary)) instruction += "\nSprávný pravopis jmen a míst: " + glossary;
        if (!string.IsNullOrWhiteSpace(previousText)) instruction += "\nPředchozí část diktátu (jen pro kontext, nepřepisuj ji): " + Tail(previousText, 400);

        var content = new JsonArray
        {
            new JsonObject { ["type"] = "text", ["text"] = instruction },
            new JsonObject { ["type"] = "input_audio", ["input_audio"] = new JsonObject { ["data"] = wavBase64, ["format"] = "wav" } },
        };
        var model = await AudioModelAsync(ct);
        var (text, _) = await ChatAsync(model, null, content, 0, ct);
        return CleanTranscript(text);
    }

    public static string CleanTranscript(string s)
    {
        s = s.Trim().Trim('"', '„', '“', '”').Trim();
        return s.Equals("(ticho)", StringComparison.OrdinalIgnoreCase) ? "" : s;
    }

    // ---------- korektura ----------

    private const string EditorRules = """
        Jsi editor rodinného fotoalba. Píšeš popisky k fotografiím z cest ve stylu neosobního cestopisu
        (bez „já“ a „my“, věcně, srozumitelně a poutavě, spisovná čeština).
        Pravidla:
        - Vycházej jen z toho, co bylo řečeno. Nevymýšlej žádná fakta, jména, čísla ani místa.
        - Diktát obsahuje přeřeknutí, opakování a opravy („ne počkej, ne Keňa, Tanzanie“) – pochop, jak to bylo myšleno, a použij opravenou verzi.
        - Jména a místa piš podle slovníčku, pokud je uveden.
        - Nadpis je krátké shrnutí (nejvýš 60 znaků, bez tečky na konci).
        Odpověz pouze JSON objektem: {"popisek": "...", "nadpis": "..."}
        """;

    public Task<ImproveResult> ImproveAsync(string dictation, CaptionContext ctx, CancellationToken ct = default) =>
        EditAsync($"""
            Z tohoto diktátu vytvoř popisek fotografie.
            {ContextText(ctx)}
            Diktát:
            {dictation.Trim()}
            """, ct);

    public Task<ImproveResult> FixAsync(string currentCaption, string instruction, CaptionContext ctx, CancellationToken ct = default) =>
        EditAsync($"""
            Uprav existující popisek podle hlasového pokynu. Změň jen to, co pokyn žádá, zbytek ponech.
            {ContextText(ctx)}
            Současný popisek:
            {currentCaption.Trim()}
            Pokyn k úpravě (přepis řeči, může obsahovat přeřeknutí):
            {instruction.Trim()}
            """, ct);

    private static string ContextText(CaptionContext c)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Délka popisku: {c.Length switch { "kratky" => "jedna věta", "delsi" => "3–5 vět", _ => "1–3 věty" }}.");
        sb.AppendLine($"Album: {c.Album}");
        if (!string.IsNullOrWhiteSpace(c.Date)) sb.AppendLine($"Datum pořízení: {c.Date}");
        if (!string.IsNullOrWhiteSpace(c.Glossary)) sb.AppendLine($"Slovníček jmen a míst: {c.Glossary.Trim()}");
        if (c.NeighbourCaptions.Count > 0)
            sb.AppendLine("Popisky sousedních fotek (jen pro návaznost, neopakuj je): " + string.Join(" | ", c.NeighbourCaptions.Select(x => Tail(x, 300))));
        return sb.ToString().TrimEnd();
    }

    private async Task<ImproveResult> EditAsync(string userText, CancellationToken ct)
    {
        var model = await TextModelAsync(ct);
        var (text, cost) = await ChatAsync(model, EditorRules, new JsonArray { new JsonObject { ["type"] = "text", ["text"] = userText } }, 0.3, ct);
        var (caption, title) = ParseEditorAnswer(text);
        if (caption.Length == 0) throw new AiException("AI nevrátila žádný popisek. Zkuste to prosím znovu.");
        return new ImproveResult(caption, title, cost);
    }

    /// <summary>Přečte odpověď editora; snese i text okolo JSONu nebo odpověď bez JSONu.</summary>
    public static (string Caption, string Title) ParseEditorAnswer(string text)
    {
        var t = text.Trim();
        var start = t.IndexOf('{');
        var end = t.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            try
            {
                var o = JsonNode.Parse(t[start..(end + 1)]);
                var cap = o?["popisek"]?.GetValue<string>()?.Trim() ?? "";
                var title = o?["nadpis"]?.GetValue<string>()?.Trim().TrimEnd('.') ?? "";
                if (cap.Length > 0) return (cap, title.Length > 0 ? title : CaptionTitle.From(cap) ?? "");
            }
            catch (JsonException) { /* níže použijeme celý text */ }
        }
        t = t.Trim('`').Trim();
        return (t, CaptionTitle.From(t) ?? "");
    }

    // ---------- volání OpenRouteru ----------

    private async Task<(string Text, double Cost)> ChatAsync(string model, string? system, JsonArray userContent, double temperature, CancellationToken ct)
    {
        var key = _secrets.Get(KeyName);
        if (string.IsNullOrWhiteSpace(key)) throw new AiException("Není zadaný klíč OpenRouter (nastavení správce, Ctrl+Alt+S).");
        var messages = new JsonArray();
        if (system is not null) messages.Add(new JsonObject { ["role"] = "system", ["content"] = system });
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = userContent });
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages,
            ["temperature"] = temperature,
            ["max_tokens"] = 2000,
            ["usage"] = new JsonObject { ["include"] = true },
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Headers.Add("X-Title", "Fotoalbum");
        HttpResponseMessage res;
        try { res = await _http.SendAsync(req, ct); }
        catch (HttpRequestException) { throw new AiException("Nepodařilo se spojit s AI. Je počítač připojený k internetu?"); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new AiException("AI neodpověděla včas. Zkuste to prosím znovu."); }
        using (res)
        {
            var json = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                var msg = TryError(json);
                throw new AiException((int)res.StatusCode switch
                {
                    401 => "Klíč OpenRouter není platný. Zkontrolujte ho v nastavení.",
                    402 => "Na účtu OpenRouter došel kredit.",
                    429 => "AI je teď přetížená, zkuste to za chvíli.",
                    _ => "AI vrátila chybu" + (msg is null ? "." : ": " + msg),
                });
            }
            var root = JsonNode.Parse(json)!;
            var text = ExtractText(root["choices"]?[0]?["message"]?["content"]);
            var cost = root["usage"]?["cost"] is JsonNode c && double.TryParse(c.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
            if (cost > 0) _addCost(cost);
            return (text, cost);
        }
    }

    private static string ExtractText(JsonNode? content) => content switch
    {
        null => "",
        JsonValue v => v.GetValue<string>(),
        JsonArray a => string.Concat(a.Select(p => p?["text"]?.GetValue<string>() ?? "")),
        _ => content.ToString(),
    };

    private static string? TryError(string json)
    {
        try { return JsonNode.Parse(json)?["error"]?["message"]?.GetValue<string>(); }
        catch (JsonException) { return null; }
    }

    private static string Tail(string s, int max) => s.Length <= max ? s : "…" + s[^max..];
}
