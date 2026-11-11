using Fotoalbum.Server;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Fotoalbum.Desktop;

/// <summary>
/// Okno Fotoalba: rozhraní z lokálního serveru zobrazené přes WebView2 (součást Windows 10/11).
/// Zavření okna nejdřív předá rozhraní, aby ukázalo souhrn fotek ke smazání a vše uložilo.
/// Server a hlídání karet běží dál (ikona u hodin).
/// </summary>
internal sealed class MainForm : Form
{
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly RunningServer _server;
    private bool _allowClose;
    private DateTime _closeAskedAt = DateTime.MinValue;

    public MainForm(RunningServer server)
    {
        _server = server;
        Text = "Fotoalbum";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
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
            var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fotoalbum", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: dataDir);
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
#if !DEBUG
            core.Settings.AreDevToolsEnabled = false;
#endif
            core.Settings.IsStatusBarEnabled = false;
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
            CloseConfirmed();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            MessageBox.Show("Okno Fotoalba se nepodařilo otevřít: " + ex.Message, "Fotoalbum", MessageBoxButtons.OK, MessageBoxIcon.Error);
            CloseConfirmed();
        }
    }

    /// <summary>Rozhraní potvrdilo zavření (souhrn mazání vyřešen, vše uloženo).</summary>
    public void CloseConfirmed()
    {
        _allowClose = true;
        Close();
    }

    /// <summary>Po zkopírování fotek z karty obnoví strom složek, pokud je okno otevřené.</summary>
    public void RefreshFolders()
    {
        if (_web.CoreWebView2 is not null)
            _ = _web.CoreWebView2.ExecuteScriptAsync("window.fotoalbum && window.fotoalbum.refreshFolders && window.fotoalbum.refreshFolders()");
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (_allowClose || _web.CoreWebView2 is null || e.CloseReason == CloseReason.WindowsShutDown)
        {
            if (!_allowClose) _server.Service.Shutdown();
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
}
