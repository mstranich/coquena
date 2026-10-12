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
            output.WriteLine("Uso: coquena [--help|-h|help] | system probe | backend <remote|install> [cpu|vulkan|cuda@<mayor>] [--release bNNNN|latest] | backend list | backend use [selector] | download <list|status|pause|resume|cancel>");
            output.WriteLine("Modelos: model download <usuario/repositorio[:cuantización]> | model list");
            output.WriteLine("Modelos externos: model add <ruta.gguf> --name <usuario/repositorio:cuantización>; model companion add <ruta.gguf> --name <usuario/repositorio#mmproj|mtp:cuantización>; model companion list (alta local sin copia, red ni hash completo).");
            output.WriteLine("Backends externos: backend register <ruta-llama-server> --name <nombre> --family <familia> [--distribution propietario/repositorio]; backend use [selector|nombre-externo]; server [nombre] use <nombre-externo>");
            output.WriteLine("Novedades: backend update [selector-latest] (todos los seguimientos o [ggml/|oficial/]cpu|vulkan|cuda@<mayor>:latest); backend list muestra la última consulta guardada. No descarga ni instala.");
            output.WriteLine("Configuración: config log-retention show | config log-retention set <tamaño> | config log-retention unset");
            output.WriteLine("Actualización: backend upgrade [selector-latest] aplica el candidato guardado por update; reutiliza un build instalado tras validarlo. No cambia pins ni instancias activas.");
            output.WriteLine("Builds: config backend-retention show | set <cantidad> | unset (inicial: 5 por distribución/familia/plataforma; limpieza sólo tras upgrade exitoso).");
            output.WriteLine("Catálogo de fábrica: config param-reset [--dry-run] (requiere confirmación para aplicar)");
            output.WriteLine("Directorios: config models-dir show | set <ruta> | unset; config backends-dir show | set <ruta> | unset (rutas relativas al home; elección interactiva de traslado)");
            output.WriteLine("Presets: preset set <preset> <param> <valor> | preset unset <preset> <param> | preset create <modelo> <nombre> | preset model set <preset> <modelo> | preset list [--model M] | preset <show|remove> <nombre>");
            output.WriteLine("Parámetros: param list | param show <param> [--backend ID|--version RELEASE [--distribution ORIGEN]]");
            output.WriteLine("Catálogo oficial: param define <param> --type integer|decimal|string|boolean|flag|enum --scope model|server --target cli|ini [--description TEXTO] [--default VALOR] [--alias=ALIAS]... [--values VALOR]...");
            output.WriteLine("Edición: param edit <param> [opciones] [--clear-default|--clear-description|--clear-aliases] [--dry-run] | param remove <param> [--dry-run]");
            output.WriteLine("Soporte: param support set <param> [ggml/FAMILIA|oficial/FAMILIA|--backend ID] [--from bNNNN] [--until bNNNN] [--dry-run] | param support remove <param> [selector|--backend ID] [--dry-run]");
            output.WriteLine("Servidores: server create <nombre> [--port N] | server default [nombre] | server list | server [nombre] show | server [nombre] use <selector|default>");
            output.WriteLine("Servidor: server [nombre] set <param> <valor> | unset <param> | preset <add|remove> <preset>... | preset list");
            output.WriteLine("Procesos: server [nombre] start [-d] | server [nombre] stop | server stop --id <id-instancia> | server [nombre] restart");
            output.WriteLine("Retención: server [nombre] log-retention show | set <tamaño> | unset (inicial: 100 MiB; admite B, KB/MB/GB, KiB/MiB/GiB)");
            output.WriteLine("Consulta: server ps | server [nombre] logs [-f] (Ctrl+C termina sólo el seguimiento)");
            output.WriteLine("Primer plano: Ctrl+C solicita parada (10 s); segundo Ctrl+C fuerza cierre inmediato.");
            output.WriteLine("Instalación de backends oficiales y modelos públicos HF, con controles locales de descarga.");
            return 0;
        }

        error.WriteLine("Error: comando o argumentos no reconocidos.");
        error.WriteLine("Consulte coquena --help para ver las opciones disponibles.");
        return 2;
    }
}
