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
          service (server)                 Administrar servicios y su ejecución
          up [--start-service]             Publicar presets de la combinación elegida
          down                             Retirar presets propios sin detener servicio
          apply|withdraw <clave>           Publicar o retirar un preset individual
          config                           Configurar directorios y retención
          param                            Administrar definiciones de parámetros

        Ayuda:
          -h, --help                       Mostrar esta ayuda (también: coquena help)

        Publicación:
          preset apply|withdraw <clave>     Equivalente a los atajos apply/withdraw
          -f/--file <yaml>                  Capas ordenadas; --env-file es explícito
          --project-directory <carpeta>     Base del proyecto (alias --base)
          --private                        Permitir referencias exteriores privadas
            target YAML sólo admite global (predeterminado) o self.
            Global usa el servicio global designado default; self usa el propio.
            Requiere interacción; no admite -d. --start-service sólo autoriza el inicio.
            Otra combinación reconcilia los propios y conserva los de otros proyectos.

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
          model list                       Listar inventario y progreso conocido
          model show [<alias>]             Consultar intención sin descargar
          model add <ruta.gguf> --name <nombre> [--try-match]
            Registrar un externo; matching informativo opcional, sin adoptar bytes
          model download <repositorio[:cuantización]>
            Elegir y descargar un modelo de Hugging Face
          model remove <nombre|id>         Retirar archivos propios; conservar externos
          model protect|unprotect <installation-id>
            Cambiar protección explícita sin borrar ni retirar referencias

            repositorio: usuario/repositorio. Nombre local:
            usuario/repositorio:cuantización.
            --base <carpeta>, --file <receta> / -f (repetible),
            --env-file <archivo> (repetible), --global / -g, --json.
            Una descarga explícita protege sin editar YAML ni crear presets.

        Auxiliares:
          model companion list             Listar auxiliares y progreso conocido
          model companion show [<alias>]   Consultar intención sin descargar
          model companion add <ruta.gguf> --name <nombre> [--try-match]
            Nombre local: agregar --type mmproj|mtp
            Nombre remoto: usuario/repositorio#tipo:cuantización
          model companion download <repositorio#tipo[:cuantización]>
            Elegir y descargar un auxiliar; tipo: mmproj o mtp
          model companion remove <nombre|id>
            Retirar archivos propios de la unidad; conservar archivos externos
          model companion protect|unprotect <installation-id>
            Cambiar protección explícita con los mismos selectores de ámbito

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
          service list                     Listar servicios conocidos
          service default set <nombre>     Asignar el alias global predeterminado
          service [<nombre|default|self>] start|restart [-d]
            Preparar e iniciar; -d devuelve el control sin detener el backend
          service [<nombre|default|self>] stop
            Detener la instancia verificada
          service [<nombre|default|self>] remove
            Retirar un servicio detenido conservando archivos y logs

            server es alias de service. Sin selector se usa default; self indica
            el servicio de la receta del proyecto. --service selecciona un nombre.
            -f/--file y --env-file son repetibles; --project-directory fija la base.
            Ctrl+C detiene el proceso en primer plano; otro Ctrl+C fuerza el cierre.

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
