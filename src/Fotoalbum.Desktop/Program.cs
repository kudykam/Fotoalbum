namespace Fotoalbum.Desktop;

internal static class Program
{
    /// <summary>
    /// Fotoalbum běží jako jeden program: lokální server, okno aplikace a hlídání karet z fotoaparátu.
    /// <c>--pozadi</c> = spuštěno s Windows, okno se neotevře (jen ikona u hodin).
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        var user = Environment.UserName;
        using var single = new Mutex(true, "Fotoalbum-" + user, out var first);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Fotoalbum-Ukazat-" + user);
        if (!first)
        {
            // Fotoalbum už běží (třeba jen u hodin) – požádat ho, ať otevře okno.
            showSignal.Set();
            return;
        }
        Application.ThreadException += (_, e) => Log.Error(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error(e.ExceptionObject as Exception);
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext(startHidden: args.Contains("--pozadi"), showSignal));
    }
}

internal static class Log
{
    private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fotoalbum");
    public static string ErrorFile => Path.Combine(Dir, "chyby.log");
    public static string ImportFile => Path.Combine(Dir, "import.log");

    public static void Error(Exception? e) { if (e is not null) Append(ErrorFile, e.ToString()); }
    public static void Import(string text) => Append(ImportFile, text);

    private static void Append(string file, string text)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}\n");
        }
        catch (IOException) { }
    }
}
