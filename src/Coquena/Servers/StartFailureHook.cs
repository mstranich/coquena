using System.Diagnostics;
using System.Text.Json.Nodes;
using Coquena.Recipes;

namespace Coquena.Servers;

public sealed record StartFailureHook(string Command, string[] Arguments)
{
    public static StartFailureHook? FromRecipe(EffectiveRecipe recipe)
    {
        var hook = recipe.Root.Get("service")?.Get("server")?.Get("hooks")?.Get("start-failed");
        if (hook is null) return null;
        var command = hook.Get("command")!;
        var args = hook.Get("args")?.Items ?? [];
        if (command.Sensitive || args.Any(arg => arg.Sensitive))
            throw hook.Error("sensitive-hook-argument", "validation", "El hook no admite command ni argumentos interpolados sensibles.");
        return new(recipe.Selection.ResolvePath(command.Text!), args.Select(arg => arg.Text!).ToArray());
    }

    public async Task NotifyAsync(string serviceId, string candidateId, string recovery, bool available,
        string code, TextWriter? diagnostics)
    {
        try
        {
            var start = new ProcessStartInfo(Command) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(Command)! };
            foreach (var arg in Arguments) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            // Drain without retaining or publishing potentially sensitive hook output.
            async Task Drain(StreamReader reader)
            {
                var buffer = new char[4096];
                while (await reader.ReadAsync(buffer.AsMemory(), timeout.Token) != 0) { }
            }
            var stdout = Drain(process.StandardOutput); var stderr = Drain(process.StandardError);
            try
            {
                var payload = new JsonObject { ["format"] = 1, ["service-id"] = serviceId, ["candidate-id"] = candidateId,
                    ["recovery"] = recovery, ["available"] = available, ["diagnostic-code"] = code };
                await process.StandardInput.WriteLineAsync(payload.ToJsonString().AsMemory(), timeout.Token);
                process.StandardInput.Close();
                await process.WaitForExitAsync(timeout.Token);
                await Task.WhenAll(stdout, stderr);
                if (process.ExitCode != 0) diagnostics?.WriteLine("[Hook] Notificación fallida: código de salida no cero.");
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                timeout.Cancel();
                try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or OperationCanceledException or InvalidOperationException)
        { diagnostics?.WriteLine("[Hook] No se completó la notificación de fallo de inicio; el resultado del servicio se conserva."); }
    }
}
