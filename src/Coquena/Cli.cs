namespace Coquena;

public static class Cli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length > 0 && args[0] == "server")
            return ServerCli.Run(args[1..], output, error);
        if (args.Length > 0 && args[0] is "config" or "preset" or "param")
            return ConfigurationCli.Run(args[0], args[1..], output, error);
        if (args.Length > 0 && args[0] is "backend" or "runtime")
            return ScopedBackendCli.Run(args[1..], output, error);
        if (args.Length > 0 && args[0] == "system")
            return BackendCli.Run(args, output, error);
        if (args.Length > 0 && args[0] == "download")
            return ScopeDownloadCli.Run(args[1..], output, error);
        if (args.Length > 0 && args[0] == "model")
            return ModelCli.Run(args[1..], output, error);
        if (args.Length == 0 ||
            (args.Length == 1 && args[0] is "--help" or "-h" or "help"))
        {
            CliHelp.Write(output);
            return 0;
        }

        error.WriteLine("Error: comando o argumentos no reconocidos.");
        error.WriteLine("Consulte coquena --help para ver las opciones disponibles.");
        return 2;
    }
}
