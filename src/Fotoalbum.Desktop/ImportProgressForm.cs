using Fotoalbum.Core.Import;

namespace Fotoalbum.Desktop;

/// <summary>
/// Malé okno vpravo dole: průběh kopírování a výsledek. Nekrade focus, po úspěchu se samo zavře.
/// Velké písmo a jednoduché texty pro staršího uživatele.
/// </summary>
internal sealed class ImportProgressForm : Form
{
    private static readonly Color Accent = Color.FromArgb(43, 104, 75);
    private static readonly Color Ink = Color.FromArgb(23, 32, 28);
    private static readonly Color Muted = Color.FromArgb(82, 97, 90);

    private readonly Label _title = new() { AutoSize = false, Dock = DockStyle.Top, Height = 40, Font = new Font("Segoe UI Semibold", 15f), ForeColor = Ink };
    private readonly Label _detail = new() { AutoSize = false, Dock = DockStyle.Top, Height = 66, Font = new Font("Segoe UI", 12f), ForeColor = Muted };
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 26, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };
    private readonly Button _stop = MakeButton("Zastavit");
    private readonly Button _open = MakeButton("Otevřít složku");
    private readonly Button _close = MakeButton("Zavřít");
    private readonly string _drive;
    private readonly string _library;
    private readonly System.Windows.Forms.Timer _autoClose = new();
    private string? _lastFolder;
    private ImportPlan? _plan;

    public event Action? StopRequested;

    public ImportProgressForm(string drive, string library)
    {
        _drive = drive;
        _library = library;
        Text = "Fotokronikář – fotky z karty";
        Icon = TrayContext.AppIcon.Load(new Size(32, 32));
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = true;
        TopMost = true;
        BackColor = Color.White;
        Padding = new Padding(20, 16, 20, 16);
        ClientSize = new Size(560, 230);
        StartPosition = FormStartPosition.Manual;
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.AddRange([_close, _open, _stop]);
        _open.Visible = _close.Visible = false;
        _open.BackColor = Accent; _open.ForeColor = Color.White;

        var spacer = new Panel { Dock = DockStyle.Top, Height = 10 };
        Controls.AddRange([buttons, _detail, spacer, _bar, _title]);

        _title.Text = $"Karta z fotoaparátu ({drive.TrimEnd('\\')})";
        _detail.Text = "Hledám nové fotky, které ještě nejsou v počítači…";

        _stop.Click += (_, _) => { _stop.Enabled = false; _detail.Text = "Zastavuji…"; StopRequested?.Invoke(); };
        _close.Click += (_, _) => Close();
        _open.Click += (_, _) => { TrayContext.OpenFolder(_lastFolder is null ? _library : Path.Combine(_library, _lastFolder)); Close(); };
        _autoClose.Tick += (_, _) => Close();
    }

    protected override bool ShowWithoutActivation => true;

    private static Button MakeButton(string text) => new()
    {
        Text = text, AutoSize = true, MinimumSize = new Size(120, 40), Font = new Font("Segoe UI", 11f),
        FlatStyle = FlatStyle.System, Margin = new Padding(8, 0, 0, 0),
    };

    public void ShowPlan(ImportPlan plan)
    {
        _plan = plan;
        if (plan.Items.Count == 0) return;
        _bar.Style = ProgressBarStyle.Continuous;
        _bar.Maximum = plan.Items.Count;
        _title.Text = $"Kopíruji z karty {Count(plan.Items)}";
    }

    public void ShowProgress(ImportProgress p)
    {
        if (p.Total == 0) return;
        _bar.Value = Math.Min(p.Done, _bar.Maximum);
        if (p.CurrentFolder.Length > 0)
        {
            _lastFolder = p.CurrentFolder;
            _detail.Text = $"{p.Done + 1} z {p.Total}  ·  {p.CurrentFile}\ndo složky „{p.CurrentFolder}“";
        }
    }

    public void ShowDone(ImportResult r)
    {
        _bar.Value = _bar.Maximum;
        _stop.Visible = false;
        _open.Visible = _close.Visible = true;
        _lastFolder = r.Folders.LastOrDefault() ?? _lastFolder;
        var folders = string.Join(", ", r.Folders.Select(f => $"„{f}“"));
        if (r.Errors.Count == 0)
        {
            _title.Text = $"Hotovo – zkopírováno {(_plan is null ? r.Copied.ToString() : Count(_plan.Items))}";
            _title.ForeColor = Accent;
            _detail.Text = $"Uloženo do {(r.Folders.Count == 1 ? "složky" : "složek")} {folders}." +
                (r.AlreadyPresent > 0 ? $" Už dřív zkopírované přeskočeno: {r.AlreadyPresent}." : "") + "\nKartu můžete vyjmout.";
            CloseAfter(TimeSpan.FromSeconds(30));
        }
        else
        {
            _title.Text = $"Zkopírováno {r.Copied}, {r.Errors.Count} se nepodařilo";
            _title.ForeColor = Color.FromArgb(194, 42, 42);
            _detail.Text = "Zkuste kartu vyjmout a znovu zasunout. Podrobnosti jsou v souboru import.log.\n" + r.Errors[0];
        }
    }

    public void ShowNothingNew(int alreadyPresent)
    {
        _bar.Style = ProgressBarStyle.Continuous;
        _bar.Maximum = 1; _bar.Value = 1;
        _stop.Visible = false;
        _close.Visible = true;
        _title.Text = "Na kartě nejsou žádné nové fotky";
        _detail.Text = alreadyPresent > 0 ? $"Všechny fotky a videa z karty ({alreadyPresent}) už v počítači jsou." : "Na kartě nejsou žádné fotky ani videa.";
        CloseAfter(TimeSpan.FromSeconds(10));
    }

    public void ShowCancelled()
    {
        _stop.Visible = false;
        _close.Visible = true;
        _open.Visible = _lastFolder is not null;
        _title.Text = "Kopírování zastaveno";
        _detail.Text = "Fotky zkopírované do této chvíle zůstaly v počítači. Zbytek se zkopíruje při příštím připojení karty.";
    }

    public void ShowError(string message)
    {
        _stop.Visible = false;
        _close.Visible = true;
        _title.Text = "Kopírování se nepovedlo";
        _title.ForeColor = Color.FromArgb(194, 42, 42);
        _detail.Text = message;
    }

    private void CloseAfter(TimeSpan t)
    {
        _autoClose.Interval = (int)t.TotalMilliseconds;
        _autoClose.Start();
    }

    /// <summary>Česky skloňované „1 fotka / 2 fotky / 5 fotek“.</summary>
    /// <summary>„312 fotek a 12 videí“ – česky skloňované počty.</summary>
    private static string Count(IReadOnlyCollection<ImportItem> items)
    {
        var videos = items.Count(i => MediaFiles.IsVideo(i.TargetName));
        var photos = items.Count - videos;
        var p = $"{photos} {(photos == 1 ? "fotku" : photos is >= 2 and <= 4 ? "fotky" : "fotek")}";
        var v = $"{videos} {(videos == 1 ? "video" : videos is >= 2 and <= 4 ? "videa" : "videí")}";
        return videos == 0 ? p : photos == 0 ? v : $"{p} a {v}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _autoClose.Dispose();
        base.Dispose(disposing);
    }
}
