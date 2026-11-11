namespace Fotoalbum.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var single = new Mutex(true, "Fotoalbum-" + Environment.UserName, out var first);
        if (!first)
        {
            MessageBox.Show("Fotoalbum už je spuštěné.", "Fotoalbum", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Application.ThreadException += (_, e) => ErrorLog.Write(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ErrorLog.Write(e.ExceptionObject as Exception);
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal static class ErrorLog
{
    public static string File => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fotoalbum", "chyby.log");

    public static void Write(Exception? e)
    {
        if (e is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.AppendAllText(File, $"{DateTime.Now:O}\n{e}\n\n");
        }
        catch (IOException) { }
    }
}
