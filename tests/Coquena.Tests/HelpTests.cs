using System.Diagnostics;
using Coquena;

namespace Coquena.Tests;

internal static class HelpTests
{
    public static void Run(string? executable)
    {
        var root = Path.Combine(@"D:\tmp", "Coquena-041-" + Guid.NewGuid().ToString("N"));
        var home = Path.Combine(root, "home");
        Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable("COQUENA_HOME");
        Environment.SetEnvironmentVariable("COQUENA_HOME", home);
        try
        {
            string? help = null;
            foreach (var arguments in new string[][] { [], ["--help"], ["-h"], ["help"] })
            {
                var result = Invoke(arguments);
                Check(result.Code == 0 && result.Error == "", "help exit");
                help ??= result.Output;
                Check(result.Output == help, "help aliases agree");
                foreach (var syntax in new[] { "backend download --version <versión>", "model companion verify <nombre> [--set-baseline]",
                    "download list [--model|--companion|--backend]", "download status [<id-descarga>]",
                    "server [<nombre>] start [-d]", "server [<nombre>] restart", "server [<nombre>] logs [-f]",
                    "--alias y --values pueden repetirse", "--dry-run muestra los cambios sin aplicarlos" })
                    Check(result.Output.Contains(syntax), "syntax: " + syntax);
                Check(!result.Output.Contains('\u001b'), "redirected help has no ANSI escapes");
                Check(result.Output.Split('\n').All(line => line.TrimEnd('\r').Length <= 80), "help fits 80 columns");
                Check(!result.Output.Contains("GUID") && !result.Output.Contains("ejecutable actual") && !result.Output.Contains("No selecciona YAML"), "help omits implementation commentary");
                Check(!Directory.Exists(home), "help creates no home");
            }
            string[][] invalid = [["--dry"], ["--dry-run"], ["--help", "extra"], ["help", "model"],
                ["backend", "list", "--dry-run"], ["model", "list", "--dry-run"], ["download", "list", "--dry-run"],
                ["config", "param-reset", "--dry"], ["param", "edit", "ctx-size", "--dry"],
                ["param", "define", "x", "--type", "integer", "--scope", "model", "--target", "cli", "--dry-run"],
                ["server", "restart", "-d"], ["server", "stop", "-d"], ["server", "logs", "-d"],
                ["server", "start", "-f"], ["-f", "recipe.yaml"], ["service", "list"], ["runtime", "list", "--force"]];
            foreach (var arguments in invalid)
            {
                var result = Invoke(arguments);
                Check(result.Code == 2 && result.Output == "" && result.Error.Contains("coquena --help"), "invalid: " + string.Join(' ', arguments));
                Check(!Directory.Exists(home), "invalid creates no home");
            }
            Directory.CreateDirectory(home);
            File.WriteAllText(Path.Combine(home, "sentinel"), "estado conservado");
            var before = Snapshot();
            foreach (var arguments in invalid.Append(new[] { "--help" }))
            {
                _ = Invoke(arguments);
                Check(Snapshot() == before, "existing state preserved");
            }
            Console.WriteLine($"OK: ayuda, aliases y {invalid.Length} errores sin efectos ({(executable is null ? "administrado" : "Native AOT")}).");
        }
        finally { Environment.SetEnvironmentVariable("COQUENA_HOME", previous); }

        string Snapshot() => string.Join('\n', Directory.GetFiles(home, "*", SearchOption.AllDirectories).Order()
            .Select(path => Path.GetRelativePath(home, path) + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))))
            + string.Join('\n', Directory.GetDirectories(home, "*", SearchOption.AllDirectories).Order());
        (int Code, string Output, string Error) Invoke(string[] arguments)
        {
            if (executable is null)
            {
                using var output = new StringWriter(); using var error = new StringWriter();
                var code = Cli.Run(arguments, output, error);
                return (code, output.ToString(), error.ToString());
            }
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000)) { process.Kill(true); throw new Exception("Help timed out"); }
            return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
