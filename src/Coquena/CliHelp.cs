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
          init [<directorio>]              Inicializar una receta mínima
          system probe                     Mostrar hardware y aceleradores disponibles
          ps [--service <nombre|default|self>] [-w|--watch]
                                           Consultar o seguir hardware y servicios
          backend (runtime)                Consultar y preparar backends por ámbito
          model                            Descargar y administrar modelos
          model companion                  Administrar auxiliares mmproj y MTP
          download                         Consultar y controlar descargas
          preset                           Crear y configurar presets de modelos
          service (server)                 Administrar servicios y su ejecución
          import <receta.yaml>             Importar intención global sin iniciar
          update [--global]               Consultar novedades y guardar un plan
          upgrade [--global]              Aplicar propuestas válidas del plan vigente
          clean [--dry-run|--yes]          Previsualizar y liberar recursos elegibles
            --select referencia           Filtrar update/upgrade; opción repetible
          combination list | forget <id>   Consultar u olvidar combinaciones
          verify [--global] [--checksum] [--json]  Auditar recursos
            --checksum                     Comprobar SHA-256 de cada archivo
            --select referencia            Elegir recurso a auditar; opción repetible
            --include-unused               Incluir instalaciones sin uso (global)
            Referencias: backend://cuda@12, model://clave, companion://clave.
            Backend por alias: backend://alias o backend://proyecto/alias.
            También model://autor/repositorio:cuantización.
            Companions: companion://autor/repositorio#tipo:cuantización.
          up [--start-service]             Publicar presets de la combinación elegida
          down                             Retirar presets propios sin detener servicio
          apply|withdraw <clave>           Publicar o retirar un preset individual
          config                           Configurar directorios y retención
          param                            Administrar definiciones de parámetros

        Ayuda:
          -h, --help                       Mostrar esta ayuda (también: coquena help)

        Importación:
          --select service,presets,model,params  Importar sólo categorías declaradas
          --as-default                     Designar el servicio importado predeterminado
          --replace <entidad>               Autorizar reemplazo; opción repetible
          --force                          Reemplazar conflictos
            Incompatible con --select y --replace.
            Selectores: service:nombre, preset:clave, model:clave,
            companion:clave, param:clave.
            Presets y recursos locales pierden el prefijo del proyecto.
            Los recursos remotos lo conservan.

        Publicación:
          preset apply|withdraw <clave>     Equivalente a los atajos apply/withdraw
          -f/--file <yaml>                  Capas ordenadas; --env-file es explícito
          --project-directory <carpeta>     Base del proyecto (alias --base)
          --private                        Permitir referencias exteriores privadas
            target YAML sólo admite global (predeterminado) o self.
            --service <nombre|default|self> selecciona el destino.
            Down sin selector usa el destino registrado.
            Global usa el servicio global designado default; self usa el propio.
            Requiere interacción; no admite -d. --start-service sólo autoriza el inicio.
            Otra combinación reconcilia los propios y conserva los de otros proyectos.

        Backends:
          backend set --version <versión> --variant <variante>
          backend set --executable <ruta>
          backend show                     Consultar el backend de la receta
          backend list                     Listar revisiones del ámbito
          backend download --version <versión> --variant <variante> [--force]
            Preparar y proteger una instalación oficial sin editar YAML
          backend protect <id-instalación>  Proteger una revisión gestionada
          backend unprotect <id-instalación>
            Retirar protección explícita sin borrar archivos ni referencias
          backend remove <id-instalación>
            Retirar una instalación gestionada, sin protección ni referencias

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

          model [companion] add --name <clave> --file <peso>
                [--file <shard>] [--type <rol>]
          model [companion] set <clave> [--repository <repo>] [--revision <rev>]
                [--quantization <tipo> | --weight <archivo>] [--type <rol>]
            Guardar las definiciones en el YAML seleccionado.

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
          preset list | show <nombre>
          preset set <preset> <argumento> <valor-yaml>
          preset bind <preset> <argumento> <model://clave|companion://clave>
          preset unset <preset> <argumento>
          preset clone <origen> <nombre>
          preset remove <nombre>

        Logs:
          service [<nombre|default|self>] logs [list|follow|show <log-id>] [-f]
          logs [list|follow|show <log-id>] [-f] [--service <servicio>]
            Sin subcomando: salida reciente (hasta 64 KiB). List: IDs retenidos.
            Show: histórico fijo. Follow/-f: continúa entre reinicios.
            Ctrl+C cierra sólo el observador. Server es alias de service.

        Servidores:
          service create <nombre> --global Crear servicio oficial sin iniciarlo
          service list                     Listar servicios conocidos
          service [<nombre|default|self>] show
            Consultar definición efectiva, identidad, ámbito y alias default
          service default set <nombre>     Asignar el alias global predeterminado
          service [<nombre|default|self>] start|restart [-d]
            Preparar e iniciar; -d devuelve el control sin detener el backend
            Default ausente en una home vacía se prepara sin receta previa.
          service [<nombre|default|self>] stop
            Detener la instancia verificada
          service [<nombre|default|self>] remove
            Retirar un servicio detenido conservando archivos y logs

            server es alias de service. Sin selector se usa default; self indica
            el servicio de la receta del proyecto. --service selecciona un nombre.
            -f/--file y --env-file son repetibles; --project-directory fija la base.
            Ctrl+C detiene el proceso en primer plano; otro Ctrl+C fuerza el cierre.

        Configuración:
          config observer-retry show|unset  Consultar/restablecer reconexión (1..30 s)
          config observer-retry set <inicial-s> <máximo-s>
                                           Espera exponencial, máximo 3600 s
          config log-retention show        Consultar la retención global de logs
          config log-retention set <tamaño> Establecer el límite global de logs
            Entero en bytes o B; KiB/MiB/GiB/TiB/PiB/EiB y sus alias
            KB/MB/GB/TB/PB/EB usan base 1024; k/m/g/t/p/e usan base 1000.
            Sin distinguir mayúsculas; punto decimal. Salida siempre B/KiB/MiB/etc.
          config log-retention unset       Restablecer el límite inicial de 100 MiB
          config backends-dir show         Mostrar el directorio de backends
          config backends-dir set <ruta>    Cambiar el directorio de backends
          config backends-dir unset        Restablecer el directorio de backends
          config models-dir show           Mostrar el directorio de modelos
          config models-dir set <ruta>      Cambiar el directorio de modelos
          config models-dir unset          Restablecer el directorio de modelos
          config param-reset [--dry-run]    Restablecer definiciones de parámetros

        Parámetros:
          param list | show <nombre> [--global]
          param define <nombre> --file <definición.yaml>
          param set <nombre> <campo> <valor-yaml>
          param unset <nombre> <campo>
          param remove <nombre>
          param reset [--yes]              Restablecer el catálogo global

        Entorno:
          COQUENA_HOME                     Directorio de datos y configuración (ruta
            absoluta)

        Notación: <valor> obligatorio; [valor] opcional; ... uno o varios; |
            alternativas.
        """;
}
