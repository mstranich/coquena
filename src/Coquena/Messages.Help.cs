namespace Coquena;

// coquena --help: the whole text is one entry so each language can arrange its columns.
public static partial class Messages
{
    public static readonly Text Help = new("""
        Manages local llama.cpp models and servers.

        Usage: coquena <command> [arguments] [options]

        Commands:
          init [<directory>]               Initialize a minimal recipe
          system probe                     Show available hardware and accelerators
          ps [--service <name|default|self>] [-w|--watch]
                                           Query or follow hardware and services
          tui                              Open the terminal service monitor
          backend (runtime)                Query and prepare backends by scope
          model                            Download and manage models
          model companion                  Manage mmproj and MTP companions
          download                         Query and control downloads
          preset                           Create and configure model presets
            Also apply, withdraw, chat, load and unload; see Presets
          service (server)                 Manage services and their execution
          import <recipe.yaml>             Import global intent without starting
          logs                             Query and follow service logs
          update [--global]                Check for updates and save a plan
          upgrade [--global]               Apply valid proposals of the current plan
            --select reference             Filter update/upgrade; repeatable option
          usage (df)                       See used and reclaimable space
          prune (clean) [--global] [-f <file>] [--project <id>]
            [--dry-run|--yes]
                                           Delete what is not used
            --model --companion --backend  Filter types in usage and clean
            --select reference             Choose resources; repeatable option
            --downloads                    Include preparation of stopped downloads
          project list [--json]            List known projects and their state
          project forget <id>... [--dry-run|--yes]
          project forget --inaccessible [--dry-run|--yes]
                                           Remove index entries; deletes no files
          combination list | forget <id>   Query or forget combinations
          verify [--global] [--checksum] [--json]  Audit resources
            --checksum                     Check the SHA-256 of each file
            --select reference             Choose a resource to audit; repeatable
            --include-unused               Include unused installations (global)
            References: backend://cuda@12, model://key, companion://key.
            Backend by alias: backend://alias or backend://project/alias.
            Also model://author/repository:quantization.
            Companions: companion://author/repository#type:quantization.
          up [--start-service]             Publish presets of the chosen combination
          down                             Withdraw own presets without stopping
          config                           Configure language, directories, retention
          param                            Manage parameter definitions

        Help:
          -h, --help                       Show this help (also: coquena help)
          --version                        Show the Coquena version

        Import:
          --select service,presets,model,params  Import only declared categories
          --as-default                     Make the imported service the default
          --replace <entity>               Allow replacement; repeatable option
          --force                          Replace conflicts
            Incompatible with --select and --replace.
            Selectors: service:name, preset:key, model:key,
            companion:key, param:key.
            Local presets and resources lose the project prefix.
            Remote resources keep it.

        Publication:
          preset apply|withdraw <preset>    Same as the apply/withdraw shortcuts
          -f/--file <yaml>                  Ordered layers; --env-file is explicit
          --project-directory <folder>      Project base (alias --base)
          --private                        Allow private outer references
            publish-to selects global (default), self or a global service name.
            --service <name|default|self> selects the target.
            Down without a selector uses the registered target.
            Global uses the global service named default; self uses its own.
            Needs interaction; does not accept -d. --start-service only allows start.
            Another combination reconciles its own and keeps other projects' ones.

        Backends:
          backend set --version <version> --variant <variant>
          backend set --executable <path>
          backend show                     Query the backend of the recipe
          backend list                     List the revisions of the scope
          backend download --version <version> --variant <variant> [--force]
            Prepare and protect an official installation without editing YAML
          backend protect <installation-id>  Protect a managed revision
          backend unprotect <installation-id>
            Remove explicit protection without deleting files or references
          backend remove <installation-id>
            Remove a managed installation, without protection or references

            Scope: current project; -f/--file repeatable and --env-file explicit.
            --project-directory <folder> selects the base (alias --base).
            --global (alias -g) selects COQUENA_HOME/global.
            backend show global accepts --service <name|default>.
            --json shows structured data. runtime is an alias of backend.
            version: latest or bNNNN; variant: cpu, vulkan or cuda@<major>.
            --force allows trying an incompatible backend with a warning;
            it does not skip integrity or change the chosen variant.
            An external executable is declared in service.backend.executable.

        Models:
          model list                       List inventory and known progress
          model show [<alias>]             Query intent without downloading
          model add <path.gguf> --name <name> [--try-match]
            Register an external one; optional informative matching, no adoption
          model download <repository[:quantization]>
            Choose and download a model from Hugging Face
          model remove <name|id>           Remove own files; keep external ones
          model protect|unprotect <installation-id>
            Change explicit protection without deleting or removing references

            repository: user/repository. Local name:
            user/repository:quantization.
            --base <folder>, --file <recipe> / -f (repeatable),
            --env-file <file> (repeatable), --global / -g, --json.
            An explicit download protects without editing YAML or creating presets.

        Companions:
          model companion list             List companions and known progress
          model companion show [<alias>]   Query intent without downloading
          model companion add <path.gguf> --name <name> [--try-match]
            Local name: add --type mmproj|mtp
            Remote name: user/repository#type:quantization
          model companion download <repository#type[:quantization]>
            Choose and download a companion; type: mmproj or mtp
          model companion remove <name|id>
            Remove the unit's own files; keep external files
          model companion protect|unprotect <installation-id>
            Change explicit protection with the same scope selectors

          model [companion] add --name <key> --file <weight>
                [--file <shard>] [--type <role>]
          model [companion] set <key> [--repository <repo>] [--revision <rev>]
                [--quantization <type> | --weight <file>] [--type <role>]
            Save the definitions in the selected YAML.

        Downloads:
          download list [--model|--companion|--backend]
            List downloads; filter by resource type
          download status|show [<download-id>]
            Query a download; without an ID, list all of them
          download pause <download-id>      Pause and keep what was downloaded
          download resume <download-id> [--force]
            Resume; --force only allows trying an incompatible backend
          download cancel <download-id>     Cancel and delete the temporary files

              Scope: current project; --base <folder> selects the folder.
              --file <recipe> (alias -f) selects the Coquena file.
              --global (alias -g) selects COQUENA_HOME/global.
              --json shows exact data.

            Use the ID shown when the download starts. Ctrl+C pauses without
            cancelling it.

        Presets:
          preset list | show <name>
          preset set <preset> <argument> <yaml-value>
          preset bind <preset> <argument> <model://key|companion://key>
          preset unset <preset> <argument>
          preset clone <origin> <name>
          preset remove <name>
          apply|withdraw <preset>          Publish or withdraw a single preset
            preset: the key or the published name recipe/key
          chat <preset> [--service <name|default|self> | --global] [--load]
                                           Try a preset with a chat and its telemetry
            --load asks to load the preset; Enter sends when it finishes
            -p|--prompt <text|-> sends one prompt; --json, --reasoning
          load|unload <preset> [--service <name|default|self> | --global]
                                           Load or unload a published preset
            preset: project/key, the local key or service/project/key

        Logs:
          service [<name|default|self>] logs [list|follow|show <log-id>] [-f]
          logs [list|follow|show <log-id>] [-f] [--service <service>]
            Without a subcommand: recent output (up to 64 KiB). List: kept IDs.
            Show: fixed history. Follow/-f: continues across restarts; in a
            terminal the last row shows the live service state.
            Ctrl+C closes only the observer. server is an alias of service.

        Servers:
          service create <name> --global   Create an official service, not started
          service list                     List known services
          service [<name|default|self>] show
            Query effective definition, identity, scope and default alias
          service default set <name>       Assign the default global alias
          service [<name|default|self>] start|restart [-d]
            Prepare and start; -d returns control without stopping the backend
            A missing default in an empty home is prepared without a recipe.
          service [<name|default|self>] stop
            Stop the verified instance
          service [<name|default|self>] remove
            Remove a stopped service keeping files and logs

            server is an alias of service. Without a selector, default is used;
            self is the service of the project recipe. --service selects a name.
            -f/--file and --env-file are repeatable; --project-directory sets base.
            Ctrl+C stops the foreground process; another Ctrl+C forces the close.

        Configuration:
          config language show|unset       Query/reset the language (auto)
          config language set <auto|code> Choose the language of the texts
          config language list             List available languages
          config language export <code>    Write a language as JSON, a template
            External languages: COQUENA_HOME/languages/<code>.json
          config observer-retry show|unset  Query/reset reconnection (1..30 s)
          config observer-retry set <initial-s> <maximum-s>
                                           Exponential backoff, at most 3600 s
          config log-retention show        Query the global log retention
          config log-retention set <size>  Set the global log limit
            Integer in bytes or B; KiB/MiB/GiB/TiB/PiB/EiB and their aliases
            KB/MB/GB/TB/PB/EB use base 1024; k/m/g/t/p/e use base 1000.
            Case insensitive; decimal point. Output is always B/KiB/MiB/etc.
          config log-retention unset       Reset the initial limit of 100 MiB
          config backends-dir show         Show the backends directory
          config backends-dir set <path>    Change the backends directory
          config backends-dir unset        Reset the backends directory
          config models-dir show           Show the models directory
          config models-dir set <path>      Change the models directory
          config models-dir unset          Reset the models directory
          config param-reset [--dry-run]    Reset parameter definitions

        Parameters:
          param list | show <name> [--global]
          param edit <name> --global [--add-alias <alias>]...
            [--remove-alias <alias>]...
            Add or remove alternative names in the global catalog
          param define <name> --file <definition.yaml>
          param set <name> <field> <yaml-value>
          param unset <name> <field>
          param remove <name>
          param reset [--yes]              Reset the global catalog

        Environment:
          COQUENA_HOME                     Data and configuration directory
            (absolute path)

        Notation: <value> required; [value] optional; ... one or more; |
            alternatives.
        """);
}
