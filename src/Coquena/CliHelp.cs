namespace Coquena;

internal static class CliHelp
{
    public static void Write(TextWriter output)
    {
        var color = ReferenceEquals(output, Console.Out) && !Console.IsOutputRedirected &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")) &&
            Environment.GetEnvironmentVariable("TERM") != "dumb";
        foreach (var line in Text.Split('\n'))
        {
            var heading = line.Length > 0 && !char.IsWhiteSpace(line[0]);
            var command = line.StartsWith("  ", StringComparison.Ordinal) && line.Length > 2 && !char.IsWhiteSpace(line[2]);
            if (color && (heading || command))
            {
                var split = command ? line.IndexOf("  ", 2, StringComparison.Ordinal) : -1;
                if (split < 0) split = line.Length;
                output.Write(heading ? "\u001b[1m" : "\u001b[36m");
                output.Write(line[..split]);
                output.Write("\u001b[0m");
                output.WriteLine(line[split..]);
            }
            else output.WriteLine(line);
        }
    }

    private const string Text = """
        Administra modelos y servidores locales de llama.cpp.

        Uso: coquena <comando> [argumentos] [opciones]

        Comandos:
          system probe                     Mostrar hardware y aceleradores disponibles
          backend (runtime)                Consultar y preparar backends por ámbito
          model                            Descargar y administrar modelos
          model companion                  Administrar auxiliares mmproj y MTP
          download                         Consultar y controlar descargas
          preset                           Crear y configurar presets de modelos
          server                           Configurar, iniciar y detener servidores
          config                           Configurar directorios y retención
          param                            Administrar definiciones de parámetros

        Ayuda:
          -h, --help                       Mostrar esta ayuda (también: coquena help)

        Backends:
          backend show                     Consultar el backend de la receta
          backend list                     Listar revisiones del ámbito
          backend download --version <versión> --variant <variante> [--force]
            Preparar y proteger una instalación oficial sin editar YAML
          backend protect <id-instalación>  Proteger una revisión gestionada
          backend unprotect <id-instalación>
            Retirar protección explícita sin borrar archivos ni referencias

            Ámbito: proyecto actual; -f/--file repetible y --env-file explícito.
            --project-directory <carpeta> selecciona la base (alias --base).
            --global (alias -g) selecciona COQUENA_HOME/global.
            backend show global admite --service <nombre|default>.
            --json muestra datos estructurados. runtime es alias de backend.
            versión: latest o bNNNN; variante: cpu, vulkan o cuda@<mayor>.
            --force permite intentar un backend incompatible con advertencia;
            no omite integridad ni cambia la variante elegida.
            Un ejecutable externo se declara en service.backend.executable.

        Modelos:
          model list                       Listar modelos registrados
          model download <repositorio[:cuantización]>
            Elegir y descargar un modelo de Hugging Face
          model add <archivo.gguf> --name <nombre> [--try-match]
            Registrar un modelo local; --try-match busca su origen en Hugging Face
          model refresh <nombre>           Actualizar información de archivos locales
          model verify <nombre> [--set-baseline]
            Comprobar integridad; --set-baseline fija una referencia para un externo
          model update [<nombre>]          Buscar actualizaciones sin descargar pesos
          model upgrade [<nombre>]         Descargar la actualización encontrada
          model remove <nombre>            Retirar el modelo; conservar archivos
            externos

            repositorio: usuario/repositorio. Nombre local:
            usuario/repositorio:cuantización.
            update/upgrade sin nombre incluyen todos los modelos y auxiliares.

        Auxiliares:
          model companion list             Listar auxiliares registrados
          model companion download <repositorio#tipo[:cuantización]>
            Elegir y descargar un auxiliar; tipo: mmproj o mtp
          model companion add <archivo.gguf> --name <nombre> [--try-match]
            Registrar un auxiliar local; nombre: usuario/repositorio#tipo:cuantización
          model companion refresh <nombre> Actualizar información de archivos locales
          model companion verify <nombre> [--set-baseline]
            Comprobar integridad; --set-baseline fija una referencia para un externo
          model companion update [<nombre>]
            Buscar actualizaciones de auxiliares sin descargar pesos
          model companion upgrade [<nombre>]
            Descargar las actualizaciones encontradas
          model companion remove <nombre>  Retirar el auxiliar; conservar archivos
            externos

        Descargas:
          download list [--model|--companion|--backend]
            Listar descargas; filtrar por tipo de recurso
          download status [<id-descarga>]   Consultar una descarga; sin ID, listar todas
          download pause <id-descarga>      Pausar y conservar lo descargado
          download resume <id-descarga> [--force]
            Reanudar; --force sólo permite intentar un backend incompatible
          download cancel <id-descarga>     Cancelar y eliminar los archivos temporales

              Ámbito: proyecto actual; --base <carpeta> selecciona la carpeta.
              --file <receta> (alias -f) selecciona el archivo de Coquena.
              --global (alias -g) selecciona COQUENA_HOME/global.
              --json muestra datos exactos.

            Usar el ID mostrado al iniciar la descarga. Ctrl+C pausa sin cancelarla.

        Presets:
          preset create <modelo> <nombre>   Crear un preset para un modelo
          preset list [--model <modelo>]    Listar presets; filtrar por modelo
          preset show <nombre>             Mostrar la configuración del preset
          preset set <preset> <param> <valor>
            Asignar un valor a un parámetro
          preset bind <preset> <param> <recurso>
            Asociar un modelo o auxiliar al parámetro
          preset unset <preset> <param>     Quitar el valor propio del parámetro
          preset clone <preset> <nombre> [--model <modelo>]
            Copiar un preset; opcionalmente cambiar el modelo
          preset model set <preset> <modelo>
            Cambiar el modelo del preset
          preset remove <nombre>           Eliminar un preset

        Servidores:
          server create <nombre> [--port <N>]
            Crear un servidor; opcionalmente indicar su puerto
          server default [<nombre>]        Consultar o elegir el servidor predeterminado
          server list                      Listar servidores configurados
          server ps                        Mostrar estado de los procesos
          server [<nombre>] show           Mostrar la configuración del servidor
          server [<nombre>] use <backend|default>
            Elegir backend; default usa el backend predeterminado
          server [<nombre>] set <param> <valor>
            Asignar un valor a un parámetro del servidor
          server [<nombre>] unset <param>   Quitar el valor propio del parámetro
          server [<nombre>] preset add <preset>...
            Agregar uno o varios presets
          server [<nombre>] preset remove <preset>...
            Quitar uno o varios presets
          server [<nombre>] preset list     Listar presets del servidor
          server [<nombre>] start [-d]      Iniciar; -d permite salir dejándolo en
            ejecución
          server [<nombre>] stop            Detener el servidor
          server stop --id <id-instancia>   Detener el proceso indicado por server ps
          server [<nombre>] restart         Reiniciar el servidor
          server [<nombre>] logs [-f]       Leer logs; -f sigue las nuevas entradas
          server [<nombre>] remove          Eliminar un servidor detenido; conservar
            logs
          server [<nombre>] log-retention show
            Consultar la retención de logs
          server [<nombre>] log-retention set <tamaño>
            Establecer el límite de logs del servidor
          server [<nombre>] log-retention unset
            Volver a la retención global

            Omitir nombre selecciona el predeterminado; escribir default selecciona
            el servidor llamado default. Ctrl+C detiene el servidor en primer plano;
            al seguir logs, sólo termina el seguimiento.

        Configuración:
          config log-retention show        Consultar la retención global de logs
          config log-retention set <tamaño> Establecer el límite global de logs
            Entero en bytes o B; KiB/MiB/GiB/TiB/PiB/EiB y sus alias
            KB/MB/GB/TB/PB/EB usan base 1024; k/m/g/t/p/e usan base 1000.
            Sin distinguir mayúsculas; punto decimal. Salida siempre B/KiB/MiB/etc.
          config log-retention unset       Restablecer el límite inicial de 100 MiB
          config backend-retention show    Consultar cuántos builds conservar
          config backend-retention set <cantidad>
            Conservar esa cantidad de builds tras actualizar (entero positivo)
          config backend-retention unset   Restablecer la retención inicial de 5 builds
          config backends-dir show         Mostrar el directorio de backends
          config backends-dir set <ruta>    Cambiar el directorio de backends
          config backends-dir unset        Restablecer el directorio de backends
          config models-dir show           Mostrar el directorio de modelos
          config models-dir set <ruta>      Cambiar el directorio de modelos
          config models-dir unset          Restablecer el directorio de modelos
          config param-reset [--dry-run]    Restablecer definiciones de parámetros

        Parámetros:
          param list [<filtro>]             Listar definiciones de parámetros
          param show <param> [<filtro>]     Mostrar una definición y su compatibilidad
          param define <param> --type <tipo> --scope <model|server> --target <cli|ini>
                       [<metadatos>]
            Crear una definición de parámetro
          param edit <param> [<metadatos>] [--type <tipo>] [--scope <model|server>]
                     [--target <cli|ini>] [--clear-default] [--clear-description]
                     [--clear-aliases] [--dry-run]
            Editar una definición; --clear-* quita el dato correspondiente
          param remove <param> [--dry-run]  Eliminar una definición
          param support set <param> [<soporte>] [--from <bNNNN>] [--until <bNNNN>]
                            [--dry-run]
            Indicar familias o builds compatibles con un parámetro
          param support remove <param> [<soporte>] [--dry-run]
            Quitar una declaración de compatibilidad

            filtro: --backend <id-backend> o --version <bNNNN> [--distribution
            <ggml|oficial>].
            soporte: ggml/<familia>, oficial/<familia> o --backend <id-backend>;
                     omitido, se aplica a todos los backends oficiales.
            tipo: integer, decimal, string, boolean, flag o enum.
            metadatos: [--description <texto>] [--default <valor>]
                       [--alias=<alias>]... [--values <valor>]...
            --alias y --values pueden repetirse. Para alias con guion: --alias=-x.
            --clear-default, --clear-description y --clear-aliases excluyen asignar
            el dato correspondiente en la misma operación.
            --dry-run muestra los cambios sin aplicarlos en las operaciones indicadas.

        Entorno:
          COQUENA_HOME                     Directorio de datos y configuración (ruta
            absoluta)

        Notación: <valor> obligatorio; [valor] opcional; ... uno o varios; |
            alternativas.
        """;
}
