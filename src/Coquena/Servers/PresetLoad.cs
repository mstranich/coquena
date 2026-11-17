using Coquena.Storage;

namespace Coquena.Servers;

// What load and unload need from the router: the state of one entry and the two requests.
public interface IPresetLoader
{
    Task<(bool Loaded, bool Failed)> LoadStateAsync(string name, CancellationToken cancellation);
    Task<bool> RequestLoadAsync(string name, CancellationToken cancellation);
    Task<bool> RequestUnloadAsync(string name, CancellationToken cancellation);
    Task<bool> ProcessingAsync(string name, CancellationToken cancellation);
}

// load and unload of one published preset: the request, then waiting until the router reports
// the final state. Neither starts services nor publishes; both are shared by the CLI and the TUI.
public static class PresetLoad
{
    public static readonly TimeSpan LoadLimit = TimeSpan.FromSeconds(120);
    public static readonly TimeSpan UnloadLimit = TimeSpan.FromSeconds(60);

    public static async Task<string> RunAsync(IPresetLoader router, string entry, bool load, CancellationToken cancellation,
        TimeSpan? poll = null, TimeSpan? limit = null)
    {
        var interval = poll ?? TimeSpan.FromMilliseconds(250);
        var (loaded, _) = await router.LoadStateAsync(entry, cancellation);
        if (load == loaded) return (load ? Messages.PresetAlreadyLoaded : Messages.PresetNotLoaded).Format(entry);
        if (!await (load ? router.RequestLoadAsync(entry, cancellation) : router.RequestUnloadAsync(entry, cancellation)))
            throw new StorageException(load ? "load-rejected" : "unload-rejected", (load ? Messages.PresetLoadRejected : Messages.PresetUnloadRejected).Format(entry));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var end = limit ?? (load ? LoadLimit : UnloadLimit);
        while (true)
        {
            var (now, failed) = await router.LoadStateAsync(entry, cancellation);
            if (load && failed) throw new StorageException("load-failed", Messages.PresetLoadFailed.Format(entry));
            if (now == load) return (load ? Messages.PresetLoadDone : Messages.PresetUnloadDone).Format(entry);
            if (clock.Elapsed >= end) throw new StorageException(load ? "load-timeout" : "unload-timeout", Messages.PresetStateTimeout.Format(entry, (int)end.TotalSeconds));
            await Task.Delay(interval, cancellation);
        }
    }
}
