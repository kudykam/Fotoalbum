using System.Diagnostics;
using Fotoalbum.Core;
using Fotoalbum.Core.Import;
using Fotoalbum.Server;
using Microsoft.Win32;

namespace Fotoalbum.Desktop;

/// <summary>
/// Celá aplikace: ikona u hodin, lokální server (pro okno a později tablet), okno Fotoalba na požádání
/// a hlídání karet z fotoaparátu – po zasunutí karty se nové fotky samy zkopírují.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "Fotoalbum";

    private readonly NotifyIcon _tray;
    private readonly DeviceWatcher _watcher = new();
    private readonly SynchronizationContext _ui;
    private readonly Queue<string> _queue = new();
    private readonly HashSet<string> _queued = new(StringComparer.OrdinalIgnoreCase);
    private readonly RegisteredWaitHandle _showWait;
    private RunningServer? _server;
    private Updater? _updater;
    private MainForm? _window;
    private bool _importBusy;
    private bool _exitAfterWindowCloses;
    private bool _shutDown;

    public TrayContext(bool startHidden, EventWaitHandle showSignal)
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(_ui);

        var autostart = new ToolStripMenuItem("Spouštět s Windows") { CheckOnClick = true, Checked = IsAutostart() };
        autostart.CheckedChanged += (_, _) => SetAutostart(autostart.Checked);
        var menu = new ContextMenuStrip { Font = new Font("Segoe UI", 11f) };
        menu.Items.Add("Otevřít Fotoalbum", null, (_, _) => ShowWindow()).Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        menu.Items.Add("Zkopírovat fotky z karty", null, (_, _) => ScanAllDrives(userAsked: true));
        menu.Items.Add("Nastavení (správce)…", null, (_, _) => { ShowWindow(); _window?.ShowSettings(); });
        menu.Items.Add(autostart);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Ukončit Fotoalbum", null, (_, _) => ExitApp());

        _tray = new NotifyIcon
        {
            Icon = AppIcon.Load(SystemInformation.SmallIconSize),
            Text = "Fotoalbum",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowWindow();

        // Fotoalbum se samo zařadí mezi programy spouštěné s Windows (aby fungovalo kopírování z karty);
        // když to uživatel v menu vypne, už se to znovu nezapne.
        if (!AutostartWasDisabled() && (!IsAutostart() || !AutostartPointsHere())) { SetAutostart(true); autostart.Checked = true; }

        // Druhé spuštění Fotoalba (ikona na ploše) jen otevře okno této instance.
        _showWait = ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => _ui.Post(__ => ShowWindow(), null), null, -1, false);

        _watcher.DriveArrived += drive => Task.Delay(2500).ContinueWith(_ => _ui.Post(__ => Enqueue(drive), null));
        Application.ApplicationExit += (_, _) => Shutdown();

        _ = StartAsync(startHidden);
    }

    private async Task StartAsync(bool startHidden)
    {
        try
        {
            _server = await ServerHost.StartAsync(new ServerOptions
            {
                HostKind = "pc",
                RecycleBinFactory = _ => new WindowsRecycleBin(),
                PickFolder = PickFolderAsync,
            });
            _server.QuitRequested += () => _ui.Post(_ => _window?.CloseConfirmed(), null);

            var updatedTo = JustUpdatedTo();
            if (updatedTo is not null) _tray.ShowBalloonTip(6000, "Fotoalbum", $"Fotoalbum bylo aktualizováno na verzi {updatedTo}.", ToolTipIcon.Info);
            _updater = new Updater(_server.Service, updatedTo);
            _server.Options.Updates = _updater;
            // aktualizace se instaluje jen když okno není otevřené (nikdy uprostřed práce)
            _updater.Ready += () => _ui.Post(_ =>
            {
                if (_window is null) _updater.ApplyIfReady(Shutdown);
            }, null);
            if (!startHidden) ShowWindow();
            // Karta mohla být zasunutá už při spuštění.
            await Task.Delay(TimeSpan.FromSeconds(startHidden ? 20 : 3));
            ScanAllDrives(userAsked: false);
        }
        catch (Exception e)
        {
            Log.Error(e);
            MessageBox.Show("Fotoalbum se nepodařilo spustit: " + e.Message, "Fotoalbum", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ExitThread();
        }
    }

    // ---------- okno ----------

    private void ShowWindow()
    {
        if (_server is null) return;
        if (_window is { IsDisposed: false })
        {
            if (_window.WindowState == FormWindowState.Minimized) _window.WindowState = FormWindowState.Maximized;
            _window.Show();
            _window.Activate();
            return;
        }
        _window = new MainForm(_server);
        _window.FormClosed += (_, _) =>
        {
            _window = null;
            if (_exitAfterWindowCloses) FinishExit();
            else if (_updater?.ApplyIfReady(Shutdown) == true) { /* program se aktualizuje a sám znovu spustí u hodin */ }
            else _tray.ShowBalloonTip(4000, "Fotoalbum", "Vše je uloženo. Fotoalbum dál hlídá kartu z fotoaparátu – ikona je tady u hodin.", ToolTipIcon.Info);
        };
        _window.Show();
    }

    private void ExitApp()
    {
        if (_window is { IsDisposed: false })
        {
            // Okno nejdřív ukáže souhrn fotek ke smazání a uloží vše; pak se aplikace ukončí.
            _exitAfterWindowCloses = true;
            _window.Show();
            _window.Close();
            return;
        }
        FinishExit();
    }

    private void FinishExit()
    {
        Shutdown();
        ExitThread();
    }

    private void Shutdown()
    {
        if (_shutDown) return;
        _shutDown = true;
        try { _server?.Service.Shutdown(); } catch (Exception e) { Log.Error(e); }
    }

    private Task<string?> PickFolderAsync()
    {
        var tcs = new TaskCompletionSource<string?>();
        _ui.Post(_ =>
        {
            using var dlg = new FolderBrowserDialog { Description = "Vyberte složku s fotkami", UseDescriptionForTitle = true, ShowNewFolderButton = false };
            tcs.SetResult(dlg.ShowDialog(_window) == DialogResult.OK ? dlg.SelectedPath : null);
        }, null);
        return tcs.Task;
    }

    // ---------- kopírování z karty ----------

    private void ScanAllDrives(bool userAsked)
    {
        var found = false;
        foreach (var d in DriveInfo.GetDrives())
        {
            if (d.DriveType is not (DriveType.Removable or DriveType.Fixed) || !d.IsReady) continue;
            if (CardImporter.FindCameraFolders(d.RootDirectory.FullName).Count == 0) continue;
            found = true;
            Enqueue(d.RootDirectory.FullName, userAsked);
        }
        if (!found && userAsked)
            _tray.ShowBalloonTip(5000, "Fotoalbum", "Není připojená žádná karta z fotoaparátu.", ToolTipIcon.Info);
    }

    private void Enqueue(string drive, bool userAsked = false)
    {
        if (_server is null || CardImporter.FindCameraFolders(drive).Count == 0) return;
        if (!_server.Service.Settings.AutoImport && !userAsked) return;
        if (_queued.Add(drive)) _queue.Enqueue(drive);
        RunNextImport();
    }

    private async void RunNextImport()
    {
        if (_importBusy || _queue.Count == 0) return;
        _importBusy = true;
        var drive = _queue.Dequeue();
        try { await ImportDrive(drive); }
        catch (Exception e) { Log.Error(e); }
        finally
        {
            _queued.Remove(drive);
            _importBusy = false;
            RunNextImport();
        }
    }

    private async Task ImportDrive(string drive)
    {
        var settings = _server!.Service.Settings;
        if (string.IsNullOrWhiteSpace(settings.PhotoRoot) || !Directory.Exists(settings.PhotoRoot))
        {
            _tray.ShowBalloonTip(8000, "Fotoalbum", "Fotky z karty nelze zkopírovat: není nastavená složka s fotkami.", ToolTipIcon.Warning);
            return;
        }

        using var cts = new CancellationTokenSource();
        var form = new ImportProgressForm(drive, settings.PhotoRoot);
        form.StopRequested += () => cts.Cancel();
        form.Show();

        var importer = new CardImporter(settings.PhotoRoot, ExifTool.Locate(settings.ExifToolPath), new AppPaths().ImportIndexFile);
        var progress = new Progress<ImportProgress>(form.ShowProgress);
        try
        {
            var plan = await Task.Run(() => importer.Plan(drive, cts.Token), cts.Token);
            form.ShowPlan(plan);
            if (plan.Items.Count == 0) { form.ShowNothingNew(plan.AlreadyPresent); return; }
            var result = await Task.Run(() => importer.Execute(plan, progress, cts.Token), cts.Token);
            form.ShowDone(result);
            Log.Import($"{drive}: zkopírováno {result.Copied} do {string.Join(", ", result.Folders)}, už v PC {result.AlreadyPresent}, chyb {result.Errors.Count}");
            foreach (var err in result.Errors) Log.Import("  " + err);
            _window?.RefreshFolders();
        }
        catch (OperationCanceledException) { form.ShowCancelled(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(e);
            form.ShowError("Kartu se nepodařilo přečíst. Byla vyjmuta? " + e.Message);
        }
    }

    // ---------- spouštění s Windows ----------

    /// <summary>Vrátí novou verzi, pokud se program od minulého spuštění aktualizoval (a zapamatuje si aktuální).</summary>
    private static string? JustUpdatedTo()
    {
        try
        {
            var file = Path.Combine(new AppPaths().DataDir, "posledni-verze.txt");
            var now = ServerHost.AppVersion();
            var before = File.Exists(file) ? File.ReadAllText(file).Trim() : null;
            File.WriteAllText(file, now);
            return before is not null && before != now ? now : null;
        }
        catch (IOException) { return null; }
    }

    private static bool AutostartPointsHere()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(RunName) is string v && v.Contains(Environment.ProcessPath ?? "\0", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAutostart()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(RunName) is string;
    }

    private static string DisabledMarker => Path.Combine(new AppPaths().DataDir, "bez-spousteni-s-windows");
    private static bool AutostartWasDisabled() => File.Exists(DisabledMarker);

    private static void SetAutostart(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on)
            {
                k.SetValue(RunName, $"\"{Environment.ProcessPath}\" --pozadi");
                if (File.Exists(DisabledMarker)) File.Delete(DisabledMarker);
            }
            else
            {
                k.DeleteValue(RunName, throwOnMissingValue: false);
                File.WriteAllText(DisabledMarker, "Uživatel vypnul spouštění s Windows.");
            }
        }
        catch (Exception e) { Log.Error(e); }
    }

    internal static class AppIcon
    {
        /// <summary>Ikona Fotoalba v požadované velikosti (ostrá i u hodin).</summary>
        public static Icon Load(Size size)
        {
            try
            {
                var file = Path.Combine(AppContext.BaseDirectory, "fotoalbum.ico");
                if (File.Exists(file)) return new Icon(file, size);
            }
            catch (Exception e) { Log.Error(e); }
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        }
    }

    internal static void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (Exception e) { Log.Error(e); }
    }

    protected override void ExitThreadCore()
    {
        _showWait.Unregister(null);
        _tray.Visible = false;
        _tray.Dispose();
        _watcher.Dispose();
        _updater?.Dispose();
        try { _server?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(10)); } catch (Exception e) { Log.Error(e); }
        base.ExitThreadCore();
    }
}
