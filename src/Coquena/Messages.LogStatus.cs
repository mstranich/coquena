namespace Coquena;

// Status line at the foot of logs -f.
public static partial class Messages
{
    public static readonly Text LogStatusWaiting = new("waiting for observation");
    public static readonly Text LogStatusNoLoaded = new("no presets loaded");
    public static readonly Text LogStatusIdle = new("no tasks");
}
