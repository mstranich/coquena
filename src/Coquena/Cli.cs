namespace Coquena;

public static class Cli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length > 0 && args[0] == "server")
            return ServerCli.Run(args[1..], output, error);
        if (args.Length > 0 && args[0] is "config" or "preset" or "param")
            return ConfigurationCli.Run(args[0], args[1..], output, error);
        if (args.Length > 0 && args[0] is "system" or "backend")
            return BackendCli.Run(args, output, error);
        if (args.Length > 0 && args[0] == "download")
            return DownloadCli.Run(args[1..], output, error);
        if (args.Length > 0 && args[0] == "model")
            return ModelCli.Run(args[1..], output, error);
        if (args.Length == 0 ||
            (args.Length == 1 && args[0] is "--help" or "-h" or "help"))
        {
            output.WriteLine("Coquena — manager de llama.cpp");
            output.WriteLine("Uso: coquena [--help|-h|help] | system | backend <remote|install> [cpu|vulkan|cuda@<mayor>] [--release bNNNN|latest] | backend list | backend use [selector] | download <list|status|pause|resume|cancel>");
            output.WriteLine("Modelos: model download <usuario/repositorio[:cuantización]> | model list");
            output.WriteLine("Configuración: config show | config set <param> <valor> | config unset <param> [--family F|--model M|--preset P]");
            output.WriteLine("Presets: preset create <modelo> <nombre> | preset model set <preset> <modelo> | preset list [--model M] | preset <show|remove> <nombre>");
            output.WriteLine("Parámetros: param list | param show <param> [--backend ID|--version RELEASE [--distribution ORIGEN]]");
            output.WriteLine("Servidores: server create <nombre> [--port N] | server default [nombre] | server list | server [nombre] show | server [nombre] use <selector|default>");
            output.WriteLine("Servidor: server [nombre] set <param> <valor> | unset <param> | preset <add|remove> <preset>... | preset list");
            output.WriteLine("Instalación de backends oficiales y modelos públicos HF, con controles locales de descarga.");
            return 0;
        }

        error.WriteLine("Error: comando o argumentos no reconocidos.");
        error.WriteLine("Consulte coquena --help para ver las opciones disponibles.");
        return 2;
    }
}
