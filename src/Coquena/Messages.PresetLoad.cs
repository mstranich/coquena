namespace Coquena;

// load and unload of one published preset, from the CLI and from En vivo.
public static partial class Messages
{
    public static readonly Text PresetLoadUsage = new("Usage: coquena load <preset> [--service <name|default|self> | --global]");
    public static readonly Text PresetUnloadUsage = new("Usage: coquena unload <preset> [--service <name|default|self> | --global]");
    public static readonly Text PresetLoadHelp = new("  <preset>: published name project/key, the key of the recipe in this folder\n  or service/project/key. Without --service, the recipe target is used.\n  --global is the same as --service global. The service must be running;\n  nothing is started or published. One preset at a time.");
    public static readonly Text PresetAlreadyLoaded = new("{0} is already loaded.");
    public static readonly Text PresetNotLoaded = new("{0} is not loaded.");
    public static readonly Text PresetLoadRejected = new("The server rejected loading {0}.");
    public static readonly Text PresetUnloadRejected = new("The server rejected unloading {0}.");
    public static readonly Text PresetLoadFailed = new("Loading {0} failed; see the service log.");
    public static readonly Text PresetLoadDone = new("{0} loaded.");
    public static readonly Text PresetUnloadDone = new("{0} unloaded.");
    public static readonly Text PresetStateTimeout = new("{0} did not reach the requested state within {1} s.");
    public static readonly Text PresetServiceStopped = new("«{0}» is not running; start it first.");
    public static readonly Text PresetUnloadBusyNoInteraction = new("{0} is answering requests; unloading it needs confirmation in an interactive terminal.");
    public static readonly Text PresetUnloadBusyQuestion = new("{0} is answering requests. Unload it anyway? [y/N] ");
    public static readonly Text PresetUnloadBusyTitle = new("Unload {0}");
    public static readonly Text PresetUnloadBusyConfirm = new("The preset is answering requests; unloading it interrupts them.");
    public static readonly Text LiveLoadButton = new("Load");
    public static readonly Text LiveUnloadButton = new("Unload");
    public static readonly Text StepLoadPreset = new("Load {0}");
    public static readonly Text StepUnloadPreset = new("Unload {0}");
}
