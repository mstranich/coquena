using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class PresetLoadTests
{
    // A router whose entry changes state after a number of observations, or fails.
    private sealed class FakeRouter(bool loaded, bool accept = true, bool fail = false, int after = 1) : IPresetLoader
    {
        public bool Loaded = loaded;
        private bool? target;
        private int observations;
        public int Requests;
        public Task<(bool Loaded, bool Failed)> LoadStateAsync(string name, CancellationToken cancellation)
        {
            if (target is { } wanted && !fail && ++observations > after) Loaded = wanted;
            return Task.FromResult((Loaded, fail && target == true));
        }
        public Task<bool> RequestLoadAsync(string name, CancellationToken cancellation) { Requests++; if (accept) target = true; return Task.FromResult(accept); }
        public Task<bool> RequestUnloadAsync(string name, CancellationToken cancellation) { Requests++; if (accept) target = false; return Task.FromResult(accept); }
        public Task<bool> ProcessingAsync(string name, CancellationToken cancellation) => Task.FromResult(false);
    }

    public static void Run(Action<bool, string> check)
    {
        var fast = TimeSpan.FromMilliseconds(1);
        string Run(IPresetLoader router, bool load, TimeSpan? limit = null) =>
            PresetLoad.RunAsync(router, "p/x", load, CancellationToken.None, fast, limit).GetAwaiter().GetResult();
        string? Fails(IPresetLoader router, bool load, TimeSpan? limit = null)
        {
            try { Run(router, load, limit); return null; } catch (StorageException error) { return error.Code; }
        }

        var loading = new FakeRouter(false, after: 2);
        check(Run(loading, true) == Messages.PresetLoadDone.Format("p/x") && loading.Loaded, "load waits until the router reports it loaded");
        var unloading = new FakeRouter(true);
        check(Run(unloading, false) == Messages.PresetUnloadDone.Format("p/x") && !unloading.Loaded, "unload waits until it is no longer loaded");
        var already = new FakeRouter(true);
        check(Run(already, true) == Messages.PresetAlreadyLoaded.Format("p/x") && already.Requests == 0, "loading a loaded preset sends no request");
        var absent = new FakeRouter(false);
        check(Run(absent, false) == Messages.PresetNotLoaded.Format("p/x") && absent.Requests == 0, "unloading an unloaded preset sends no request");
        check(Fails(new FakeRouter(false, accept: false), true) == "load-rejected", "a rejected load is reported");
        check(Fails(new FakeRouter(false, fail: true), true) == "load-failed", "a failed load is reported");
        check(Fails(new FakeRouter(false, after: int.MaxValue), true, TimeSpan.FromMilliseconds(20)) == "load-timeout", "a load that never ends reaches its limit");

        // The CLI names one preset; anything else is a usage error before touching services.
        var output = new StringWriter(); var error = new StringWriter();
        check(PresetLoadCli.Run("load", [], output, error) == 2 && error.ToString().Contains("coquena load"), "load without a preset shows its usage");
        check(PresetLoadCli.Run("unload", ["a/x", "b/y"], output, error) == 2, "unload accepts a single preset");
        check(PresetLoadCli.Run("load", ["--help"], output, error) == 0 && output.ToString().Contains("--service"), "load --help describes the options");
    }
}
