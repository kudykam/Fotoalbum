using Fotoalbum.Server;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Fotoalbum.Desktop;

/// <summary>
/// Okno aplikace: uvnitř běží lokální server a rozhraní se zobrazuje přes WebView2 (součást Windows 10/11).
/// Zavření okna nejdřív předá rozhraní, aby ukázalo souhrn fotek ke smazání a vše uložilo.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private RunningServer? _server;
    private bool _allowClose;
    private DateTime _closeAskedAt = DateTime.MinValue;

    public MainForm()
    {
        Text = "Fotoalbum";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1000, 640);
        BackColor = Color.FromArgb(245, 247, 245);
        Controls.Add(_web);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            _server = await ServerHost.StartAsync(new ServerOptions
            {
                HostKind = "pc",
                RecycleBinFactory = _ => new WindowsRecycleBin(),
                PickFolder = PickFolderAsync,
            });
            _server.QuitRequested += () => BeginInvoke(() => { _allowClose = true; Close(); });

            var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fotoalbum", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: dataDir);
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
#if !DEBUG
            core.Settings.AreDevToolsEnabled = false;
#endif
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = true;
            core.NewWindowRequested += (_, a) => a.Handled = true;
            core.NavigationStarting += (_, a) =>
            {
                if (!a.Uri.StartsWith(_server.BaseUrl, StringComparison.OrdinalIgnoreCase)) a.Cancel = true;
            };
            core.Navigate(_server.StartUrl);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show("Chybí součást Windows „Microsoft Edge WebView2“. Nainstalujte ji prosím ze stránek Microsoftu a spusťte Fotoalbum znovu.",
                "Fotoalbum", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _allowClose = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            MessageBox.Show("Fotoalbum se nepodařilo spustit: " + ex.Message, "Fotoalbum", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _allowClose = true;
            Close();
        }
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (_allowClose || _server is null || _web.CoreWebView2 is null || e.CloseReason == CloseReason.WindowsShutDown)
        {
            if (!_allowClose) _server?.Service.Shutdown();
            base.OnFormClosing(e);
            return;
        }
        // Druhé zavření během krátké doby (např. rozhraní neodpovídá): uložit a zavřít bez dotazu.
        if (DateTime.UtcNow - _closeAskedAt < TimeSpan.FromSeconds(8))
        {
            _server.Service.Shutdown();
            base.OnFormClosing(e);
            return;
        }
        e.Cancel = true;
        _closeAskedAt = DateTime.UtcNow;
        await _web.CoreWebView2.ExecuteScriptAsync("window.fotoalbum && window.fotoalbum.requestClose()");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _server?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private Task<string?> PickFolderAsync() =>
        Task.FromResult((string?)Invoke(() =>
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "Vyberte složku s fotkami",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false,
            };
            return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedPath : null;
        }));
}
