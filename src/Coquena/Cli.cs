namespace Coquena;

public static class Cli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length > 0 && args[0] == "download")
            return DownloadCli.Run(args[1..], output, error);
        if (args.Length == 0 ||
            (args.Length == 1 && args[0] is "--help" or "-h" or "help"))
        {
            output.WriteLine("Coquena — manager de llama.cpp");
            output.WriteLine("Uso: coquena [--help|-h|help] | download <list|status|pause|resume|cancel>");
            output.WriteLine("Controles locales de descarga; proveedores e instalación aún no integrados.");
            return 0;
        }

        error.WriteLine("Error: comando o argumentos no reconocidos.");
        error.WriteLine("Consulte coquena --help para ver las opciones disponibles.");
        return 2;
    }
}
