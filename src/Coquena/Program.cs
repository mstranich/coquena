using Coquena;
using Coquena.Servers;
using Coquena.Storage;

Console.OutputEncoding = new System.Text.UTF8Encoding(false);

if (args.Length == 2 && args[0] == "--internal-server-signal" && Guid.TryParse(args[1], out var id))
{
    try { return ServerRuntime.SignalWorker(Home.Resolve(), id); }
    catch { return 1; }
}

return Cli.Run(args, Console.Out, Console.Error);
