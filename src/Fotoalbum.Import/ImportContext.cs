using System.Diagnostics;
using Fotoalbum.Core;
using Fotoalbum.Core.Import;
using Microsoft.Win32;

namespace Fotoalbum.Import;

/// <summary>Běh na pozadí: ikona v oznamovací oblasti, hlídání karet, fronta kopírování.</summary>
internal sealed class ImportContext : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "FotoalbumImport";

    private readonly NotifyIcon _tray;
    private readonly DeviceWatcher _watcher = new();
    private readonly SynchronizationContext _ui;
    private readonly Queue<string> _queue = new();
    private readonly HashSet<string> _queued = new(StringComparer.OrdinalIgnoreCase);
    private bool _busy;

    public ImportContext(bool startedByWindows)
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        var autostart = new ToolStripMenuItem("Spouštět s Windows") { CheckOnClick = true, Checked = IsAutostart() };
        autostart.CheckedChanged += (_, _) => SetAutostart(autostart.Checked);
        var menu = new ContextMenuStrip { Font = new Font("Segoe UI", 11f) };
        menu.Items.Add("Zkopírovat fotky z připojené karty", null, (_, _) => ScanAllDrives(userAsked: true));
        menu.Items.Add(autostart);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Ukončit kopírování z karet", null, (_, _) => ExitThread());

        _tray = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
            Text = "Fotoalbum – po připojení karty zkopíruje nové fotky",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ScanAllDrives(userAsked: true);

        // Při prvním spuštění se doplněk sám zařadí mezi programy spouštěné s Windows.
        if (!startedByWindows && !IsAutostart() && !AutostartWasDisabled()) { SetAutostart(true); autostart.Checked = true; }

        _watcher.DriveArrived += drive =>
            // chvíli počkat, než Windows kartu plně připojí
            Task.Delay(2500).ContinueWith(_ => _ui.Post(__ => Enqueue(drive), null));

        // Karta mohla být zasunutá už při startu počítače.
        Task.Delay(TimeSpan.FromSeconds(startedByWindows ? 20 : 3)).ContinueWith(_ => _ui.Post(__ => ScanAllDrives(userAsked: false), null));
    }

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
        if (CardImporter.FindCameraFolders(drive).Count == 0) return; // není to karta z fotoaparátu
        var settings = new SettingsStore(new AppPaths()).Current;
        if (!settings.AutoImport && !userAsked) return;
        if (_queued.Add(drive)) _queue.Enqueue(drive);
        RunNext();
    }

    private async void RunNext()
    {
        if (_busy || _queue.Count == 0) return;
        _busy = true;
        var drive = _queue.Dequeue();
        try { await ImportDrive(drive); }
        catch (Exception e) { Log.Write(e); }
        finally
        {
            _queued.Remove(drive);
            _busy = false;
            RunNext();
        }
    }

    private async Task ImportDrive(string drive)
    {
        var paths = new AppPaths();
        var settings = new SettingsStore(paths).Current;
        if (string.IsNullOrWhiteSpace(settings.PhotoRoot) || !Directory.Exists(settings.PhotoRoot))
        {
            _tray.ShowBalloonTip(8000, "Fotoalbum", "Fotky z karty nelze zkopírovat: ve Fotoalbu není nastavená složka s fotkami.", ToolTipIcon.Warning);
            return;
        }

        using var cts = new CancellationTokenSource();
        var form = new ProgressForm(drive, settings.PhotoRoot);
        form.StopRequested += () => cts.Cancel();
        form.Show();

        var importer = new CardImporter(settings.PhotoRoot, ExifTool.Locate(settings.ExifToolPath), paths.ImportIndexFile);
        var progress = new Progress<ImportProgress>(form.ShowProgress);
        try
        {
            var plan = await Task.Run(() => importer.Plan(drive, cts.Token), cts.Token);
            form.ShowPlan(plan);
            if (plan.Items.Count == 0) { form.ShowNothingNew(plan.AlreadyPresent); return; }
            var result = await Task.Run(() => importer.Execute(plan, progress, cts.Token), cts.Token);
            form.ShowDone(result);
            Log.Write($"{drive}: zkopírováno {result.Copied}, už v PC {result.AlreadyPresent}, chyb {result.Errors.Count}");
            foreach (var err in result.Errors) Log.Write("  " + err);
        }
        catch (OperationCanceledException)
        {
            form.ShowCancelled();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write(e);
            form.ShowError("Kartu se nepodařilo přečíst. Byla vyjmuta? " + e.Message);
        }
    }

    // ---------- spouštění s Windows ----------

    private static bool IsAutostart()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(RunName) is string;
    }

    private static string DisabledMarker => Path.Combine(new AppPaths().DataDir, "import-bez-autostartu");
    private static bool AutostartWasDisabled() => File.Exists(DisabledMarker);

    private static void SetAutostart(bool on)
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

    protected override void ExitThreadCore()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _watcher.Dispose();
        base.ExitThreadCore();
    }

    internal static void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (Exception e) { Log.Write(e); }
    }
}
