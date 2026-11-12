using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Fotoalbum.Core.Ai;

namespace Fotoalbum.Core.Tests;

/// <summary>Náhradní OpenRouter: zapisuje požadavky a vrací připravené odpovědi (bez internetu).</summary>
internal sealed class RecordingHandler(Func<HttpRequestMessage, string, (HttpStatusCode, string)> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Req, string Body)> Calls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        Calls.Add((request, body));
        var (code, json) = respond(request, body);
        return new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}

public class AiTests
{
    private const string Models = """{"data":[{"id":"anthropic/claude-haiku-5.5","name":"Claude Haiku 5.5","architecture":{"input_modalities":["text"]},"pricing":{"prompt":"0.0000001","completion":"0.0000005"}},{"id":"google/gemini-2.5-flash","name":"Gemini","architecture":{"input_modalities":["text","audio"]},"pricing":{"prompt":"0.0000003","completion":"0.0000025"}}]}""";

    private static string Answer(string content, double cost = 0.0003) =>
        new JsonObject { ["choices"] = new JsonArray { new JsonObject { ["message"] = new JsonObject { ["content"] = content } } }, ["usage"] = new JsonObject { ["cost"] = cost } }.ToJsonString();

    private static (AiService Ai, RecordingHandler H, List<double> Costs, AppSettings S) Make(Func<HttpRequestMessage, string, (HttpStatusCode, string)> respond, string? key = "sk-or-test")
    {
        var t = new TempDir();
        var secrets = new FileSecretStore(t.Path);
        secrets.Set(AiService.KeyName, key);
        var h = new RecordingHandler(respond);
        var costs = new List<double>();
        var settings = new AppSettings { Glossary = "Ngorongoro, Jarka" };
        return (new AiService(new HttpClient(h), secrets, () => settings, costs.Add), h, costs, settings);
    }

    [Fact]
    public async Task Prepis_posle_zvuk_jako_input_audio_s_klicem_a_slovnickem()
    {
        var (ai, h, costs, _) = Make((req, _) => req.RequestUri!.AbsolutePath.EndsWith("/models")
            ? (HttpStatusCode.OK, Models) : (HttpStatusCode.OK, Answer("„To bylo v Keni.“")));

        var text = await ai.TranscribeAsync("UklGRg==", "předtím");

        Assert.Equal("To bylo v Keni.", text);
        var chat = h.Calls.Single(c => c.Req.RequestUri!.AbsolutePath.EndsWith("/chat/completions"));
        Assert.Equal("Bearer", chat.Req.Headers.Authorization!.Scheme);
        var body = JsonNode.Parse(chat.Body)!;
        Assert.Equal("google/gemini-2.5-flash", body["model"]!.GetValue<string>());
        var parts = body["messages"]![0]!["content"]!.AsArray();
        Assert.Equal("wav", parts[1]!["input_audio"]!["format"]!.GetValue<string>());
        Assert.Contains("Ngorongoro", parts[0]!["text"]!.GetValue<string>());
        Assert.Equal([0.0003], costs);
    }

    [Fact]
    public async Task Korektura_pouzije_Haiku_a_precte_popisek_s_nadpisem()
    {
        var (ai, h, _, _) = Make((req, _) => req.RequestUri!.AbsolutePath.EndsWith("/models")
            ? (HttpStatusCode.OK, Models)
            : (HttpStatusCode.OK, Answer("Tady je výsledek:\n```json\n{\"popisek\": \"Západ slunce nad Masai Marou.\", \"nadpis\": \"Západ slunce.\"}\n```")));
        var ctx = new CaptionContext("Afrika 1998 · Keňa", "14. 7. 1998", ["Sloni u vody."], "Masai Mara", "stredni");

        var r = await ai.ImproveAsync("to bylo v Keni ne počkej", ctx);

        Assert.Equal("Západ slunce nad Masai Marou.", r.Caption);
        Assert.Equal("Západ slunce", r.Title);
        var body = JsonNode.Parse(h.Calls.Last().Body)!;
        Assert.Equal(AiService.DefaultTextModel, body["model"]!.GetValue<string>());
        Assert.Equal("system", body["messages"]![0]!["role"]!.GetValue<string>());
        Assert.Contains("Afrika 1998 · Keňa", body["messages"]![1]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Vybrany_model_v_nastaveni_ma_prednost()
    {
        var (ai, h, _, s) = Make((_, _) => (HttpStatusCode.OK, Answer("{\"popisek\":\"A.\",\"nadpis\":\"A\"}")));
        s.AiTextModel = "anthropic/claude-sonnet-5.5";
        await ai.FixAsync("Popisek.", "oprav to", new CaptionContext("Album", null, [], "", "stredni"));
        Assert.Equal("anthropic/claude-sonnet-5.5", JsonNode.Parse(h.Calls.Single().Body)!["model"]!.GetValue<string>());
    }

    [Fact]
    public async Task Chyby_jsou_srozumitelne()
    {
        var (bad, _, _, _) = Make((_, _) => (HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"No auth\"}}"));
        var e = await Assert.ThrowsAsync<AiException>(() => bad.FixAsync("a", "b", new CaptionContext("x", null, [], "", "stredni")));
        Assert.Contains("Klíč OpenRouter není platný", e.Message);

        var (nokey, _, _, _) = Make((_, _) => (HttpStatusCode.OK, "{}"), key: null);
        var e2 = await Assert.ThrowsAsync<AiException>(() => nokey.FixAsync("a", "b", new CaptionContext("x", null, [], "", "stredni")));
        Assert.Contains("Není zadaný klíč", e2.Message);
    }

    [Theory]
    [InlineData("{\"popisek\":\"Sloni.\",\"nadpis\":\"Sloni\"}", "Sloni.", "Sloni")]
    [InlineData("Sloni u napajedla. Byli tři.", "Sloni u napajedla. Byli tři.", "Sloni u napajedla")]
    [InlineData("{rozbitý json", "{rozbitý json", "{rozbitý json")]
    public void Odpoved_editora_se_precte_i_bez_ciste_JSON(string text, string caption, string title)
    {
        var (c, t) = AiService.ParseEditorAnswer(text);
        Assert.Equal(caption, c);
        Assert.Equal(title, t);
    }

    [Theory]
    [InlineData("Tak tohle je Masai Mara.", "Tak tohle je Masai Mara.", true)]
    [InlineData("tak tohle je masai mara", "Úvod. Tak tohle je Masai Mara.", true)]
    [InlineData("Večer jsme se vraceli.", "Tak tohle je Masai Mara.", false)]
    [InlineData("", "cokoli", true)]
    [InlineData("Ano.", "Ano.", false)]
    public void Opakovany_prepis_se_pozna(string transcript, string previous, bool repeat) =>
        Assert.Equal(repeat, AiService.IsRepeat(transcript, previous));

    [Fact]
    public async Task Predchozi_text_se_modelu_neposila()
    {
        var (ai, h, _, _) = Make((req, _) => req.RequestUri!.AbsolutePath.EndsWith("/models")
            ? (HttpStatusCode.OK, Models) : (HttpStatusCode.OK, Answer("Tak tohle je Masai Mara.")));
        var text = await ai.TranscribeAsync("UklGRg==", "Úvod. Tak tohle je Masai Mara.");
        Assert.Equal("", text); // zopakovaná věta se zahodí
        Assert.DoesNotContain("Úvod", h.Calls.Last().Body);
    }

    [Fact]
    public void Klic_se_nikdy_neukaze_cely()
    {
        var (ai, _, _, _) = Make((_, _) => (HttpStatusCode.OK, "{}"), key: "sk-or-v1-abcdef123456");
        Assert.Equal("…3456", ai.KeyHint);
    }
}

public class CaptionSourceTests
{
    [Fact]
    public void Rucni_text_a_text_od_AI_maji_ruzny_stav_a_nadpis()
    {
        using var t = new TempDir();
        using var db = new CatalogDb(System.IO.Path.Combine(t.Path, "k.db"));
        db.SaveCaption("a.jpg", "Sloni u vody", false);
        Assert.Equal(PhotoStatus.Manual, db.GetPhoto("a.jpg")!.Status);
        db.SaveCaption("a.jpg", "Sloní rodina u napajedla.", false, captionSource: CaptionSource.Ai, title: "Sloní rodina");
        var r = db.GetPhoto("a.jpg")!;
        Assert.Equal(PhotoStatus.Done, r.Status);
        Assert.Equal("Sloní rodina", r.Title);
    }

    [Fact]
    public void Databaze_verze_1_se_aktualizuje()
    {
        using var t = new TempDir();
        var file = System.IO.Path.Combine(t.Path, "k.db");
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + file + ";Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE photos(path TEXT PRIMARY KEY COLLATE NOCASE, caption TEXT NOT NULL DEFAULT '', is_draft INTEGER NOT NULL DEFAULT 0,
                  raw_dictation TEXT NOT NULL DEFAULT '', skip INTEGER NOT NULL DEFAULT 0, marked_delete INTEGER NOT NULL DEFAULT 0,
                  rotation INTEGER NOT NULL DEFAULT 0, everyday INTEGER NULL, date_taken TEXT NULL, file_size INTEGER NOT NULL DEFAULT 0,
                  file_mtime INTEGER NOT NULL DEFAULT 0, imported INTEGER NOT NULL DEFAULT 0, file_caption TEXT NULL, write_error TEXT NULL,
                  updated_at TEXT NOT NULL DEFAULT '');
                INSERT INTO photos(path, caption) VALUES('stara.jpg', 'Starý popisek');
                PRAGMA user_version=1;
                """;
            cmd.ExecuteNonQuery();
        }
        using var db = new CatalogDb(file);
        var r = db.GetPhoto("stara.jpg")!;
        Assert.Equal("Starý popisek", r.Caption);
        Assert.Equal(PhotoStatus.Done, r.Status); // dřívější popisky nedostanou značku „jen ruční text“
    }
}
