namespace Coquena;

// Parse without opening a scope: invalid syntax must have no persistent effects.
internal sealed record ServiceCommand(
    string Action, string? Selector, List<string> Files, List<string> EnvFiles,
    string? BasePath, bool Global, bool Detached, bool Private)
{
    internal static ServiceCommand Parse(string[] args)
    {
        var positionals = new List<string>();
        var files = new List<string>();
        var envFiles = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? basePath = null, service = null;
        bool global = false, detached = false, isPrivate = false, endOptions = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (endOptions) { positionals.Add(args[i]); continue; }
            var option = args[i] switch
            {
                "-f" => "--file", "-g" => "--global", "--base" => "--project-directory", _ => args[i]
            };
            if (option == "--") { endOptions = true; continue; }
            switch (option)
            {
                case "--global": case "--private": case "-d":
                    if (!seen.Add(option)) throw Invalid();
                    if (option == "--global") global = true;
                    else if (option == "--private") isPrivate = true;
                    else detached = true;
                    break;
                case "--file": case "--env-file": case "--project-directory": case "--service":
                    if (++i == args.Length || option is not ("--file" or "--env-file") && !seen.Add(option)) throw Invalid();
                    if (option == "--file") files.Add(args[i]);
                    else if (option == "--env-file") envFiles.Add(args[i]);
                    else if (option == "--project-directory") basePath = args[i];
                    else service = args[i];
                    break;
                default:
                    if (option.StartsWith('-')) throw Invalid();
                    positionals.Add(option);
                    break;
            }
        }
        if (global && (files.Count != 0 || envFiles.Count != 0 || basePath is not null || isPrivate)) throw Invalid();
        string action;
        string? selector = null;
        if (positionals.Count == 0) action = "help";
        else if (positionals.Count == 3 && positionals[0] == "default" && positionals[1] == "set" && service is null && !detached)
        {
            action = "default-set";
            selector = positionals[2];
            if (selector is "default" or "self") throw Invalid();
        }
        else if (positionals.Count == 1 && positionals[0] == "list" && service is null && !detached) action = "list";
        else
        {
            action = positionals[^1];
            if (action is not ("start" or "stop" or "restart" or "remove") || positionals.Count > 2 ||
                positionals.Count == 2 && (service is not null || positionals[0] is "start" or "stop" or "restart" or "remove" or "list" or "logs") ||
                detached && action is not ("start" or "restart")) throw Invalid();
            selector = service ?? (positionals.Count == 2 ? positionals[0] : "default");
            if (global && selector == "self") throw Invalid();
        }
        return new(action, selector, files, envFiles, basePath, global, detached, isPrivate);
    }

    private static ArgumentException Invalid() => new("Argumentos de service inválidos. Consulte coquena --help.");
}
