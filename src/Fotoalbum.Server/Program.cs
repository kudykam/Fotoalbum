using Fotoalbum.Server;

// Samostatné spuštění serveru (vývoj, test v prohlížeči). Windows aplikace používá ServerHost přímo.
//   dotnet run --project src/Fotoalbum.Server -- --data ./tmp-data --url http://127.0.0.1:5180
var options = new ServerOptions();
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--data") options.DataDir = Path.GetFullPath(args[i + 1]);
    if (args[i] == "--url") options.Url = args[i + 1];
}

if (Environment.GetEnvironmentVariable("FOTOALBUM_FAKE_AI") == "1")
{
    options.AiHandler = new FakeOpenRouterHandler();
    Console.WriteLine("Pozor: AI je jen náhradní (FOTOALBUM_FAKE_AI=1), nic se neposílá na internet.");
}
await using var server = await ServerHost.StartAsync(options);
var quit = new TaskCompletionSource();
server.QuitRequested += () => quit.TrySetResult();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; quit.TrySetResult(); };

Console.WriteLine("Fotoalbum běží. Otevřete v prohlížeči:");
Console.WriteLine(server.StartUrl);
await quit.Task;
server.Service.Shutdown();
