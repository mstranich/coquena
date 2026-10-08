namespace Coquena;

public static class Cli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length > 0 && args[0] is "system" or "backend")
            return BackendCli.Run(args, output, error);
        if (args.Length > 0 && args[0] == "download")
            return DownloadCli.Run(args[1..], output, error);
        if (args.Length == 0 ||
            (args.Length == 1 && args[0] is "--help" or "-h" or "help"))
        {
            output.WriteLine("Coquena — manager de llama.cpp");
            output.WriteLine("Uso: coquena [--help|-h|help] | system | backend remote [cpu|vulkan|cuda@<mayor>] [--release bNNNN|latest] | download <list|status|pause|resume|cancel>");
            output.WriteLine("Consulta de backends oficiales y controles locales de descarga; instalación aún no integrada.");
            return 0;
        }

        error.WriteLine("Error: comando o argumentos no reconocidos.");
        error.WriteLine("Consulte coquena --help para ver las opciones disponibles.");
        return 2;
    }
}
