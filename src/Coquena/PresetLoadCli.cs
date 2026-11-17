using Coquena.Observation;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena;

// load|unload <preset> [--service <name> | --global]: one published preset at a time.
// The preset is named as in chat; a stopped service is rejected instead of started.
internal static class PresetLoadCli
{
    public static int Run(string verb, string[] args, TextWriter output, TextWriter error)
    {
        var load = verb == "load";
        var usage = (load ? Messages.PresetLoadUsage : Messages.PresetUnloadUsage).ToString();
        if (args is ["--help"] or ["-h"])
        {
            output.WriteLine(usage);
            output.WriteLine(Messages.PresetLoadHelp);
            return 0;
        }
        string? reference = null, serviceName = null;
        var valid = true;
        for (var i = 0; i < args.Length && valid; i++)
        {
            if (args[i] == "--service" && i + 1 < args.Length && serviceName is null) serviceName = args[++i];
            else if (args[i] is "--global" or "-g" && serviceName is null) serviceName = "global";
            else if (!args[i].StartsWith('-') && reference is null) reference = args[i];
            else valid = false;
        }
        if (!valid || reference is null || reference.Length == 0 || reference.StartsWith('/') || reference.EndsWith('/') ||
            reference.Contains("//", StringComparison.Ordinal))
        {
            error.WriteLine(usage);
            return 2;
        }
        try
        {
            if (ChatCli.Resolve(reference, serviceName, output, error, offerStart: false) is not var (_, address, entry)) return 2;
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
            { Timeout = TimeSpan.FromSeconds(30) };
            var router = new LlamaPresetRouter(client, address);
            // Unloading a preset that is answering requests needs confirmation.
            if (!load && router.ProcessingAsync(entry, CancellationToken.None).GetAwaiter().GetResult())
            {
                if (Console.IsInputRedirected) { error.WriteLine(Messages.PresetUnloadBusyNoInteraction.Format(entry)); return 2; }
                output.Write(Messages.PresetUnloadBusyQuestion.Format(entry));
                if (!Messages.IsYes(Console.ReadLine())) return 130;
            }
            output.WriteLine(PresetLoad.RunAsync(router, entry, load, CancellationToken.None).GetAwaiter().GetResult());
            return 0;
        }
        catch (StorageException e)
        {
            error.WriteLine(verb + ": " + e.Message);
            return 1;
        }
        catch (Exception e) when (ServiceObservation.ObservationError(e) || e is HttpRequestException or TaskCanceledException)
        {
            error.WriteLine(verb + ": " + e.Message);
            return 2;
        }
    }
}
