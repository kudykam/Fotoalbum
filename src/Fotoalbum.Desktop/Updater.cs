using Fotoalbum.Core;
using Fotoalbum.Server;
using Velopack;
using Velopack.Sources;

namespace Fotoalbum.Desktop;

/// <summary>
/// Automatické aktualizace z GitHub Releases (Velopack), specifikace 2.8.
/// Kontroluje minutu po startu a pak každých 6 h, stahuje potichu na pozadí.
/// Instaluje se jen když se nepracuje: při startu (Velopack sám) nebo po zavření okna (<see cref="ApplyIfReady"/>).
/// Kanál „stable“ = schválené verze (děda), „test“ = zkušební (jen správce, skryté nastavení).
/// </summary>
internal sealed class Updater : IAppUpdates, IDisposable
{
    private const string RepoUrl = "https://github.com/kudykam/Fotoalbum";
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly FotoalbumService _service;
    private readonly System.Threading.Timer _timer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private UpdateManager? _mgr;
    private VelopackAsset? _ready;
    private string _status = "none";
    private string? _message;
    private string? _updatedTo;

    /// <summary>Stažená aktualizace je připravená k instalaci.</summary>
    public event Action? Ready;

    public Updater(FotoalbumService service, string? updatedTo)
    {
        _service = service;
        _updatedTo = updatedTo;
        _timer = new System.Threading.Timer(_ => _ = CheckAsync(false), null, TimeSpan.FromMinutes(1), Interval);
    }

    private string? _packaged;

    /// <summary>Kanál: výslovná volba v nastavení, jinak kanál, ze kterého byl program nainstalován.</summary>
    private string Channel => _service.Settings.UpdateChannelChosen
        ? (_service.Settings.UpdateChannel == "test" ? "test" : "stable")
        : _packaged ??= PackagedChannel();

    private static string PackagedChannel()
    {
        try
        {
            // kanál uložený instalátorem (VelopackApp.Run nastaví výchozí lokátor při startu)
            var channel = Velopack.Locators.VelopackLocator.IsCurrentSet ? Velopack.Locators.VelopackLocator.Current.Channel : null;
            return channel == "test" ? "test" : "stable";
        }
        catch (Exception e) { Log.Error(e); return "stable"; }
    }

    private UpdateManager Manager(bool allowDowngrade)
    {
        var source = new GithubSource(RepoUrl, accessToken: null, prerelease: Channel == "test");
        return new UpdateManager(source, new UpdateOptions { ExplicitChannel = Channel, AllowVersionDowngrade = allowDowngrade });
    }

    public AppUpdateInfo Info
    {
        get
        {
            var mgr = _mgr ??= Manager(false);
            var version = mgr.IsInstalled ? mgr.CurrentVersion?.ToString() ?? ServerHost.AppVersion() : ServerHost.AppVersion();
            var info = new AppUpdateInfo(version, Channel, mgr.IsInstalled, mgr.IsInstalled ? _status : "unavailable",
                _ready?.Version?.ToString(), _message, _updatedTo);
            _updatedTo = null; // oznámení o provedené aktualizaci jen jednou
            return info;
        }
    }

    public Task CheckNowAsync() => CheckAsync(false);

    public async Task SetChannelAsync(string channel)
    {
        var previous = Channel;
        _service.SetUpdateChannel(channel);
        _ready = null;
        // návrat z testovací na stabilní: nainstalovat poslední stabilní, i když má nižší číslo
        await CheckAsync(allowDowngrade: previous == "test" && channel == "stable");
    }

    private async Task CheckAsync(bool allowDowngrade)
    {
        if (!await _gate.WaitAsync(0)) return; // už běží
        try
        {
            var mgr = _mgr = Manager(allowDowngrade);
            if (!mgr.IsInstalled) { _status = "unavailable"; return; } // spuštěno z rozbaleného zipu / vývoj
            if (mgr.UpdatePendingRestart is { } pending) { _ready = pending; _status = "ready"; Ready?.Invoke(); return; }
            _status = "checking"; _message = null;
            var update = await mgr.CheckForUpdatesAsync();
            if (update is null) { _status = "uptodate"; return; }
            _status = "downloading";
            await mgr.DownloadUpdatesAsync(update);
            _ready = update.TargetFullRelease;
            _status = "ready";
            Log.Import($"Aktualizace: stažena verze {_ready.Version} ({Channel})");
            Ready?.Invoke();
        }
        catch (Exception e)
        {
            // bez internetu apod. – zkusí se příště, uživatele to neruší
            _status = "error";
            _message = "Kontrola aktualizací se nepovedla (není připojení k internetu?).";
            Log.Error(e);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Nainstaluje staženou aktualizaci a spustí Fotoalbum znovu u hodin. Volat jen když okno není otevřené.</summary>
    public bool ApplyIfReady(Action beforeExit)
    {
        if (_ready is null || _mgr is null || !_mgr.IsInstalled) return false;
        try
        {
            beforeExit();
            _mgr.ApplyUpdatesAndRestart(_ready, ["--pozadi"]);
            return true;
        }
        catch (Exception e) { Log.Error(e); return false; }
    }

    public void Dispose() => _timer.Dispose();
}
