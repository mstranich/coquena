namespace Coquena;

public static class Cli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
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
            output.WriteLine("Instalación de backends oficiales y modelos públicos HF, con controles locales de descarga.");
            return 0;
        }

        error.WriteLine("Error: comando o argumentos no reconocidos.");
        error.WriteLine("Consulte coquena --help para ver las opciones disponibles.");
        return 2;
    }
}
