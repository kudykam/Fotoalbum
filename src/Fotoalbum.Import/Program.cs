namespace Fotoalbum.Import;

/// <summary>
/// Doplněk Fotoalbum Import: běží na pozadí (ikona u hodin), po připojení karty z fotoaparátu
/// sám zkopíruje nové fotky do složek „rok + roční období“ a ukáže průběh. Kartu nemění.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var single = new Mutex(true, "Fotoalbum-Import-" + Environment.UserName, out var first);
        if (!first) return; // už běží – nic dalšího není potřeba
        Application.ThreadException += (_, e) => Log.Write(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write(e.ExceptionObject as Exception);
        ApplicationConfiguration.Initialize();
        Application.Run(new ImportContext(startedByWindows: args.Contains("--pozadi")));
    }
}

internal static class Log
{
    public static string File => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fotoalbum", "import.log");

    public static void Write(Exception? e) => Write(e?.ToString());

    public static void Write(string? text)
    {
        if (text is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.AppendAllText(File, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}\n");
        }
        catch (IOException) { }
    }
}
