using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Fotoalbum.Server;

/// <summary>
/// Náhradní OpenRouter pro vývoj a automatické testy rozhraní (proměnná FOTOALBUM_FAKE_AI=1).
/// Nevolá internet; vrací předem připravené odpovědi. Ve Windows aplikaci se nikdy nepoužívá.
/// </summary>
public sealed class FakeOpenRouterHandler : HttpMessageHandler
{
    private int _segment;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await Task.Delay(300, ct);
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/models"))
            return Json("""{"data":[{"id":"anthropic/claude-haiku-5.5","name":"Claude Haiku 5.5","architecture":{"input_modalities":["text","image"]},"pricing":{"prompt":"0.0000001","completion":"0.0000005"}},{"id":"anthropic/claude-sonnet-5.5","name":"Claude Sonnet 5.5","architecture":{"input_modalities":["text","image"]},"pricing":{"prompt":"0.000002","completion":"0.00001"}},{"id":"google/gemini-2.5-flash","name":"Gemini 2.5 Flash","architecture":{"input_modalities":["text","image","audio"]},"pricing":{"prompt":"0.0000003","completion":"0.0000025"}}]}""");

        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
        var userText = string.Concat(body["messages"]!.AsArray().Last()!["content"]!.AsArray()
            .Select(p => p?["text"]?.GetValue<string>() ?? ""));
        string answer;
        if (body["messages"]!.ToJsonString().Contains("input_audio"))
        {
            string[] parts = ["Tak tohle je, to bylo v Keni, ne počkej, už Tanzanie, ne, Keňa, Masai Mara,", "večer, když jsme se vraceli ze safari a slunce zapadalo za akácii."];
            answer = parts[Math.Min(_segment++, parts.Length - 1)];
        }
        else if (userText.Contains("Pokyn k úpravě"))
            answer = """{"popisek":"Západ slunce nad Masai Marou v Keni. Při návratu z večerního safari zastavil řidič Samuel, aby bylo možné zachytit slunce za siluetou akácie.","nadpis":"Západ slunce nad Masai Marou"}""";
        else
            answer = """{"popisek":"Západ slunce nad Masai Marou v Keni. Při návratu z večerního safari zastavil řidič Joseph, aby bylo možné zachytit slunce za siluetou akácie.","nadpis":"Západ slunce nad Masai Marou"}""";
        var resp = new JsonObject
        {
            ["choices"] = new JsonArray { new JsonObject { ["message"] = new JsonObject { ["content"] = answer } } },
            ["usage"] = new JsonObject { ["cost"] = 0.0003 },
        };
        return Json(resp.ToJsonString());
    }

    private static HttpResponseMessage Json(string s) =>
        new(HttpStatusCode.OK) { Content = new StringContent(s, Encoding.UTF8, "application/json") };
}
