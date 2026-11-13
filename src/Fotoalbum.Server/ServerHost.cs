using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Fotoalbum.Core;
using Microsoft.Extensions.FileProviders;

namespace Fotoalbum.Server;

public sealed class ServerOptions
{
    /// <summary>Adresa serveru. Výchozí je jen tento počítač na volném portu.</summary>
    public string Url { get; set; } = "http://127.0.0.1:0";
    public string? DataDir { get; set; }
    public Func<string, IRecycleBin>? RecycleBinFactory { get; set; }
    /// <summary>Výběr složky dialogem Windows (jen v desktopové aplikaci).</summary>
    public Func<Task<string?>>? PickFolder { get; set; }
    /// <summary>Úložiště klíče AI (Windows: DPAPI). Prázdné = soubor v datech aplikace (vývoj).</summary>
    public Fotoalbum.Core.Ai.ISecretStore? Secrets { get; set; }
    /// <summary>Náhradní odpovědi AI pro testy (FOTOALBUM_FAKE_AI=1).</summary>
    public HttpMessageHandler? AiHandler { get; set; }
    /// <summary>Jméno hostitele zobrazené v rozhraní („pc“ = Windows aplikace, „web“ = prohlížeč).</summary>
    public string HostKind { get; set; } = "web";
    /// <summary>Aktualizace (jen Windows aplikace).</summary>
    public IAppUpdates? Updates { get; set; }
}

/// <summary>Běžící server: webové rozhraní + API nad <see cref="FotoalbumService"/>.</summary>
public sealed class RunningServer : IAsyncDisposable
{
    internal RunningServer(WebApplication app, FotoalbumService service, string token, ServerOptions options)
    {
        Options = options;
        App = app;
        Service = service;
        Token = token;
    }

    public WebApplication App { get; }
    /// <summary>Nastavení serveru; některé části (např. aktualizace) jde doplnit až po spuštění.</summary>
    public ServerOptions Options { get; }
    public FotoalbumService Service { get; }
    public string Token { get; }
    public string BaseUrl => App.Urls.First().Replace("0.0.0.0", "127.0.0.1").TrimEnd('/');
    /// <summary>Adresa, kterou otevře okno aplikace. Jednorázový klíč se uloží do cookie.</summary>
    public string StartUrl => $"{BaseUrl}/?t={Token}";

    /// <summary>Rozhraní potvrdilo zavření programu (souhrn mazání vyřešen, vše uloženo).</summary>
    public event Action? QuitRequested;
    internal void RaiseQuit() => QuitRequested?.Invoke();

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
        Service.Dispose();
    }
}

public static class ServerHost
{
    /// <summary>Verze sestavení (např. 0.3.0), bez doplňku za „+“.</summary>
    public static string AppVersion()
    {
        var v = System.Reflection.Assembly.GetEntryAssembly()?
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "0.0.0";
        var plus = v.IndexOf('+');
        return plus > 0 ? v[..plus] : v;
    }

    private const string CookieName = "fotoalbum_klic";

    public static async Task<RunningServer> StartAsync(ServerOptions options)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var service = new FotoalbumService(new AppPaths(options.DataDir), options.RecycleBinFactory,
            secrets: options.Secrets, aiHandler: options.AiHandler);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(options.Url);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        var app = builder.Build();
        var running = new RunningServer(app, service, token, options);

        app.Use(async (ctx, next) =>
        {
            // Ochrana: API přijímá jen požadavky z okna aplikace (cookie s jednorázovým klíčem).
            if (ctx.Request.Path == "/" && ctx.Request.Query["t"] == token)
            {
                ctx.Response.Cookies.Append(CookieName, token, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict });
                ctx.Response.Redirect("/");
                return;
            }
            if (ctx.Request.Path.StartsWithSegments("/api") && ctx.Request.Cookies[CookieName] != token)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsJsonAsync(new { error = "Chybí přístupový klíč. Otevřete Fotokroniku z aplikace." });
                return;
            }
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            try { await next(); }
            catch (Exception e) when (ctx.Request.Path.StartsWithSegments("/api") && !ctx.Response.HasStarted)
            {
                ctx.Response.StatusCode = e switch
                {
                    UnauthorizedAccessException => 403,
                    FileNotFoundException or DirectoryNotFoundException => 404,
                    InvalidOperationException => 409,
                    Fotoalbum.Core.Ai.AiException => 503,
                    ArgumentException or JsonException or BadHttpRequestException => 400,
                    _ => 500,
                };
                await ctx.Response.WriteAsJsonAsync(new { error = e.Message });
            }
        });

        var ui = new ManifestEmbeddedFileProvider(Assembly.GetExecutingAssembly(), "ui");
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = ui });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = ui,
            OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-cache",
        });

        MapApi(app, running, options);
        await app.StartAsync();
        return running;
    }

    private static void MapApi(WebApplication app, RunningServer running, ServerOptions options)
    {
        var s = running.Service;
        var api = app.MapGroup("/api");

        api.MapGet("/state", () => Results.Json(new
        {
            state = s.GetState(), host = options.HostKind, canPickFolder = options.PickFolder is not null,
            app = options.Updates?.Info ?? new AppUpdateInfo(AppVersion(), s.Settings.UpdateChannel, false, "unavailable", null, null, null),
        }));

        api.MapPost("/update/check", async () =>
        {
            if (options.Updates is null) return Results.StatusCode(501);
            await options.Updates.CheckNowAsync();
            return Results.Json(options.Updates.Info);
        });
        api.MapPost("/update/channel", async (ChannelRequest r) =>
        {
            if (options.Updates is null) return Results.StatusCode(501);
            await options.Updates.SetChannelAsync(r.Channel);
            return Results.Json(options.Updates.Info);
        });

        api.MapPost("/settings", (SettingsRequest r) =>
        {
            s.Configure(r.PhotoRoot, r.ExifToolPath, r.AutoImport, r.TextSize);
            return Results.Json(s.GetState());
        });

        api.MapPost("/pick-folder", async () =>
        {
            if (options.PickFolder is null) return Results.StatusCode(501);
            var folder = await options.PickFolder();
            return Results.Json(new { folder });
        });

        api.MapPut("/settings/panel", (PanelWidthRequest r) =>
        {
            s.SetPanelWidth(r.Panel, r.Width);
            return Results.Ok();
        });

        api.MapPut("/reading/layout", (ReadingLayoutRequest r) =>
        {
            s.SetReadingLayout(r.Layout);
            return Results.Ok();
        });

        api.MapGet("/reading/albums", (CancellationToken ct) => Results.Json(s.GetReadingAlbums(ct)));
        api.MapPut("/reading/intro", (AlbumIntroRequest r) =>
        {
            s.SetAlbumIntro(r.Path, r.Intro ?? "");
            return Results.Ok();
        });
        api.MapPost("/ai/search", async (SearchRequest r, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(r.Query) || r.Query.Length > 500) throw new ArgumentException("Neplatný dotaz.");
            return Results.Json(await s.Ai.InterpretSearchAsync(r.Query, DateOnly.FromDateTime(DateTime.Today), ct));
        });

        api.MapGet("/tree", () => Results.Json(s.GetTree()));
        api.MapGet("/folder", (string? path) => Results.Json(s.OpenFolder(path ?? "")));
        api.MapPost("/folder/kind", (FolderKindRequest r) => { s.SetFolderKind(r.Path, r.Kind); return Results.Ok(); });
        api.MapPost("/folder/complete", (FolderCompletionRequest r) => Results.Json(s.CompleteFolder(r.Path, r.UndoBatch)));

        api.MapGet("/photo", (string path) => Results.Json(s.GetPhoto(path)));
        api.MapPut("/photo/caption", (CaptionRequest r) => Results.Json(s.SaveCaption(r.Path, r.Caption ?? "", r.IsDraft, r.Source, r.Title, r.Raw)));
        api.MapPost("/photo/flags", (FlagsRequest r) =>
            Results.Json(s.SetFlags(r.Path, r.Skip, r.MarkedDelete, r.Rotation)));
        api.MapPost("/photo/leave", (PositionRequest r) => { s.Leave(r.Folder, r.Path); return Results.Ok(); });
        api.MapPost("/photo/position", (PositionRequest r) => { s.SetPosition(r.Folder, r.Path); return Results.Ok(); });
        api.MapGet("/photo/history", (string path) => Results.Json(s.History(path)));

        api.MapGet("/thumb", async (string path, int? size, CancellationToken ct) =>
        {
            var full = s.Library.Resolve(path);
            var file = await s.Thumbs.GetAsync(full, Math.Clamp(size ?? 360, 64, 2400), ct);
            return Results.File(file, "image/jpeg", lastModified: File.GetLastWriteTimeUtc(file));
        });
        api.MapGet("/image", (string path) =>
        {
            var full = s.Library.Resolve(path);
            if (!PhotoLibrary.IsPhotoFile(full) || !File.Exists(full)) throw new FileNotFoundException("Fotka neexistuje.");
            return Results.File(full, "image/jpeg", lastModified: File.GetLastWriteTimeUtc(full), enableRangeProcessing: true);
        });

        api.MapGet("/trash", () => Results.Json(s.MarkedForDelete()));

        // ---------- AI: diktování a korektura (specifikace 3) ----------
        api.MapGet("/ai", () => Results.Json(s.AiStatus()));
        api.MapPost("/ai/settings", (AiSettingsRequest r) =>
        {
            s.ConfigureAi(r.Key, r.TextModel, r.AudioModel, r.Glossary, r.CaptionLength);
            return Results.Json(s.AiStatus());
        });
        api.MapGet("/ai/models", async (CancellationToken ct) =>
        {
            var all = await s.Ai.ListModelsAsync(ct);
            var text = all.Where(m => !m.Audio || m.Id.StartsWith("google/", StringComparison.Ordinal))
                .Where(m => m.Id.StartsWith("anthropic/claude", StringComparison.Ordinal) || m.Id.StartsWith("google/gemini", StringComparison.Ordinal)
                    || m.Id.StartsWith("openai/gpt", StringComparison.Ordinal) || m.Id.StartsWith("mistralai/", StringComparison.Ordinal))
                .OrderBy(m => m.CompletionPricePerM ?? 999).Take(60).ToList();
            var audio = all.Where(m => m.Audio).OrderBy(m => m.PromptPricePerM ?? 999).ToList();
            return Results.Json(new { text, audio });
        });
        api.MapPost("/ai/transcribe", async (TranscribeRequest r, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(r.Audio) || r.Audio.Length > 20_000_000) throw new ArgumentException("Neplatná nahrávka.");
            return Results.Json(new { text = await s.Ai.TranscribeAsync(r.Audio, r.Previous ?? "", ct) });
        });
        api.MapPost("/ai/improve", async (ImproveRequest r, CancellationToken ct) =>
        {
            var res = await s.Ai.ImproveAsync(r.Dictation ?? "", s.ContextFor(r.Path), ct);
            return Results.Json(new { caption = res.Caption, title = res.Title, cost = res.CostUsd });
        });
        api.MapPost("/ai/fix", async (FixRequest r, CancellationToken ct) =>
        {
            var res = await s.Ai.FixAsync(r.Caption ?? "", r.Instruction ?? "", s.ContextFor(r.Path), ct);
            return Results.Json(new { caption = res.Caption, title = res.Title, cost = res.CostUsd });
        });

        // Zavření programu: rozhraní nejdřív ukáže souhrn fotek ke smazání, pak pošle rozhodnutí.
        api.MapPost("/quit", (QuitRequest r) =>
        {
            var moved = 0;
            if (r.MoveMarkedToBin is { } bin && s.MarkedForDelete().Count > 0) moved = s.ResolveMarked(bin);
            s.Shutdown();
            running.RaiseQuit();
            return Results.Json(new { moved });
        });
    }
}

public sealed record PanelWidthRequest(string Panel, int? Width);
public sealed record ReadingLayoutRequest(string Layout);
public sealed record AlbumIntroRequest(string Path, string? Intro);
public sealed record SearchRequest(string Query);
public sealed record ChannelRequest(string Channel);
public sealed record AiSettingsRequest(string? Key, string? TextModel, string? AudioModel, string? Glossary, string? CaptionLength);
public sealed record TranscribeRequest(string Audio, string? Previous);
public sealed record ImproveRequest(string Path, string? Dictation);
public sealed record FixRequest(string Path, string? Caption, string? Instruction);
public sealed record SettingsRequest(string? PhotoRoot, string? ExifToolPath, bool? AutoImport, int? TextSize);
public sealed record FolderKindRequest(string Path, string? Kind);
public sealed record FolderCompletionRequest(string Path, string? UndoBatch);
public sealed record CaptionRequest(string Path, string? Caption, bool IsDraft, string? Source = null, string? Title = null, string? Raw = null);
public sealed record FlagsRequest(string Path, bool? Skip, bool? MarkedDelete, int? Rotation);
public sealed record PositionRequest(string Folder, string Path);
public sealed record QuitRequest(bool? MoveMarkedToBin);
