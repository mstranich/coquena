namespace Coquena;

public static class Cli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        var prefix = 0;
        while (prefix < args.Length)
        {
            if (args[prefix] is "--global" or "-g" or "--private") { prefix++; continue; }
            if (args[prefix] is "--file" or "-f" or "--env-file" or "--project-directory" or "--base" or "--service")
            { if (prefix + 1 >= args.Length) break; prefix += 2; continue; }
            break;
        }
        if (prefix < args.Length && args[prefix] == "import") return ImportCli.Run(args, output, error);
        if (prefix < args.Length && args[prefix] == "verify") return VerifyCli.Run(args, output, error);
        if (prefix < args.Length && (args[prefix] is "up" or "down" or "apply" or "withdraw" ||
            args[prefix] == "preset" && prefix + 1 < args.Length && args[prefix + 1] is "apply" or "withdraw"))
            return PublicationCli.Run(args, output, error);
        if (prefix > 0 && prefix < args.Length && args[prefix] is "service" or "server")
            return ScopedServiceCli.Run(args[..prefix].Concat(args[(prefix + 1)..]).ToArray(), output, error);
        if (args.Length > 0 && args[0] is "server" or "service")
            return ScopedServiceCli.Run(args[1..], output, error);
        if (args.Length > 0 && args[0] is "config" or "preset" or "param")
            return ConfigurationCli.Run(args[0], args[1..], output, error);
        if (args.Length > 0 && args[0] is "backend" or "runtime")
            return ScopedBackendCli.Run(args[1..], output, error);
        if (args.Length > 0 && args[0] == "system")
            return BackendCli.Run(args, output, error);
        if (args.Length > 0 && args[0] == "download")
            return ScopeDownloadCli.Run(args[1..], output, error);
        if (args.Length > 0 && args[0] == "model")
            return ScopedModelCli.Run(args[1..], output, error);
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
