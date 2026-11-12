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
    /// <summary>Jméno hostitele zobrazené v rozhraní („pc“ = Windows aplikace, „web“ = prohlížeč).</summary>
    public string HostKind { get; set; } = "web";
}

/// <summary>Běžící server: webové rozhraní + API nad <see cref="FotoalbumService"/>.</summary>
public sealed class RunningServer : IAsyncDisposable
{
    internal RunningServer(WebApplication app, FotoalbumService service, string token)
    {
        App = app;
        Service = service;
        Token = token;
    }

    public WebApplication App { get; }
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
    private const string CookieName = "fotoalbum_klic";

    public static async Task<RunningServer> StartAsync(ServerOptions options)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var service = new FotoalbumService(new AppPaths(options.DataDir), options.RecycleBinFactory);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(options.Url);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        var app = builder.Build();
        var running = new RunningServer(app, service, token);

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
                await ctx.Response.WriteAsJsonAsync(new { error = "Chybí přístupový klíč. Otevřete Fotoalbum z aplikace." });
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

        api.MapGet("/state", () => Results.Json(new { state = s.GetState(), host = options.HostKind, canPickFolder = options.PickFolder is not null }));

        api.MapPost("/settings", (SettingsRequest r) =>
        {
            s.Configure(r.PhotoRoot, r.ExifToolPath, r.AutoImport);
            return Results.Json(s.GetState());
        });

        api.MapPost("/pick-folder", async () =>
        {
            if (options.PickFolder is null) return Results.StatusCode(501);
            var folder = await options.PickFolder();
            return Results.Json(new { folder });
        });

        api.MapGet("/tree", () => Results.Json(s.GetTree()));
        api.MapGet("/folder", (string? path) => Results.Json(s.OpenFolder(path ?? "")));
        api.MapPost("/folder/kind", (FolderKindRequest r) => { s.SetFolderKind(r.Path, r.Kind); return Results.Ok(); });

        api.MapGet("/photo", (string path) => Results.Json(s.GetPhoto(path)));
        api.MapPut("/photo/caption", (CaptionRequest r) => Results.Json(s.SaveCaption(r.Path, r.Caption ?? "", r.IsDraft)));
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

public sealed record SettingsRequest(string? PhotoRoot, string? ExifToolPath, bool? AutoImport);
public sealed record FolderKindRequest(string Path, string? Kind);
public sealed record CaptionRequest(string Path, string? Caption, bool IsDraft);
public sealed record FlagsRequest(string Path, bool? Skip, bool? MarkedDelete, int? Rotation);
public sealed record PositionRequest(string Folder, string Path);
public sealed record QuitRequest(bool? MoveMarkedToBin);
