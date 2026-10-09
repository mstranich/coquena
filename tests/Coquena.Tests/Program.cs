using System.Diagnostics;
using Coquena;

if (args.Length == 1 && args[0] == "--runtime-child")
{
    Console.CancelKeyPress += (_, e) => e.Cancel = true;
    Thread.Sleep(Timeout.Infinite); return;
}

if (args.Contains("--models-preset")) { Coquena.Tests.ServerRuntimeTests.Fixture(args); return; }
if (args.Length == 5 && args[0] == "--runtime-console-signal")
{ Environment.ExitCode = Coquena.Tests.ServerRuntimeTests.ConsoleSignal(args); return; }
if (args.Length == 2 && args[0] == "--internal-server-signal")
{ Environment.ExitCode = Coquena.Servers.ServerRuntime.SignalWorker(Coquena.Storage.Home.Resolve(), Guid.Parse(args[1])); return; }
if (args.Length is 1 or 2 && args[0] == "--runtime-tests")
{ Coquena.Tests.ServerRuntimeTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null); return; }

if (args.Length is 1 or 2 && args[0] == "--server-tests")
{
    Coquena.Tests.ServerTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null);
    return;
}

if (args.Length == 1 && args[0] == "--version") { Console.WriteLine("version: 1 (abcdef)"); return; }
if (args.Length == 2 && args[0] == "--configuration-backend-smoke")
{
    await Coquena.Tests.ConfigurationTests.BackendSmoke(args[1]);
    return;
}
if (args.Length is 1 or 2 && args[0] == "--configuration-tests")
{
    Coquena.Tests.ConfigurationTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null);
    return;
}
if (args.Length is 2 or 3 && args[0] == "--hf-resolve")
{
    await Coquena.Tests.ResolutionTests.Live(args[1], args.Length == 3 ? args[2] : null);
    return;
}

if (args.Length == 2 && args[0] == "--hf-store")
{
    await Coquena.Tests.ModelSourceTests.Live(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "--model-install-tests")
{
    await Coquena.Tests.ModelInstallerTests.Run(Path.GetFullPath(args[1]));
    return;
}

if (args.Length == 3 && args[0] == "--download-worker")
{
    Coquena.Tests.DownloadTests.Worker(args[1], Guid.Parse(args[2]));
    return;
}

if (args.Length == 2 && args[0] == "--storage-worker")
{
    Coquena.Tests.StorageTests.Worker(args[1]);
    return;
}

string[][] cases = [[], ["--help"], ["-h"], ["help"], ["invalid"],
    ["backend"], ["--json"], ["--help", "extra"], ["invalid", "--help"]];
var executable = args.Length == 1 ? Path.GetFullPath(args[0]) : null;
if (args.Length > 1 || (executable is not null && !File.Exists(executable)))
    throw new ArgumentException("Uso: Coquena.Tests [ruta-binario-nativo]");

foreach (var input in cases)
{
    var expected = input.Length == 0 ||
        (input.Length == 1 && input[0] is "--help" or "-h" or "help") ? 0 : 2;
    using var output = new StringWriter();
    using var error = new StringWriter();
    Verify(Cli.Run(input, output, error), output.ToString(), error.ToString());

    if (executable is not null)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in input) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new Exception("No inició el binario.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true);
            throw new Exception("El binario excedió 10 segundos.");
        }
        Verify(process.ExitCode, await stdout, await stderr);
    }

    void Verify(int code, string stdout, string stderr)
    {
        if (code != expected || (expected == 0
            ? !stdout.Contains("Uso: coquena") || !stdout.Contains("download") || stderr.Length != 0
            : stdout.Length != 0 || !stderr.Contains("Error:") || !stderr.Contains("coquena --help")))
            throw new Exception($"Falló [{string.Join(", ", input)}]: code={code}, out={stdout}, err={stderr}");
    }
}

Console.WriteLine($"OK: {cases.Length} casos administrados" +
    (executable is null ? "." : $" y {cases.Length} casos del ejecutable publicado."));

Coquena.Tests.ContractTests.Run();
Coquena.Tests.ConfigurationTests.Run(executable);
Coquena.Tests.ServerTests.Run(executable);
Coquena.Tests.ServerRuntimeTests.Run(executable);
Coquena.Tests.StorageTests.Run();
await Coquena.Tests.BackendTests.Run(executable);
await Coquena.Tests.DownloadTests.Run(executable);

await Coquena.Tests.InstallerTests.Run(executable);
await Coquena.Tests.ResolutionTests.Run();
await Coquena.Tests.ModelSourceTests.Run();
await Coquena.Tests.GgufTests.Run();
await Coquena.Tests.ModelInstallerTests.Run(executable);
