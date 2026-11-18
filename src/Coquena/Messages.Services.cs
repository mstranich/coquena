namespace Coquena;

// service, logs, ps and recipe edit commands.
public static partial class Messages
{
    public static readonly Text ModelUpgradeDone = new("{0} | upgrade and cleanup completed.");
    public static readonly Text ModelPlanAction = new("  {0} | {1} | origin {2}");
    public static readonly Text ModelNewContent = new("new content");
    public static readonly Text DownloadCancelledId = new("{0}: download cancelled.");
    public static readonly Text DownloadPausedId = new("{0}: download paused.");

    public static readonly Text ServerNoDefault = new("No default server.");
    public static readonly Text ServerDefault = new("Default server: {0}");
    public static readonly Text ServerInvalid = new("Error: server command or arguments not recognized. See coquena --help.");
    public static readonly Text DiagnosticLine = new("Diagnostic: {0}");
    public static readonly Text DiagnosticIndented = new("  Diagnostic: {0}");
    public static readonly Text LogsOptionWithoutValue = new("logs option without a value. See coquena --help.");
    public static readonly Text LogsInvalidArguments = new("Invalid logs arguments. See coquena --help.");
    public static readonly Text LogsRetentionPostponed = new("[Logs] Retention postponed: lifecycle operation in progress.");
    public static readonly Text NotAvailableSingular = new("not available");
    public static readonly Text RecipeWithoutService = new("The recipe does not define service.");
    public static readonly Text ServiceChanged = new("The selected service changed; no substitute is chosen.");
    public static readonly Text ServiceAlreadyRunning = new("Service already running.");
    public static readonly Text CandidateFailed = new("Candidate failure ({0}). [r] Retry, [c] Cancel:");
    public static readonly Text CandidateFailedWithLastGood = new("Candidate failure ({0}). [r] Retry, [u] Use last good, [c] Cancel:");
    public static readonly Text ServiceInstance = new("Service {0}; instance {1}.");
    public static readonly Text LogRow = new("{0} | {1} | service {2} | instance {3} | {4} | {5} | registered active: {6} | {7}");

    public static readonly Text ServerRemoved = new("Server removed: {0} ({1}); resources and logs kept.");
    public static readonly Text ServerBackend = new("{0} backend: {1} {2}/{3} {4}");
    public static readonly Text ServerPreset = new("Preset: {0} ({1})");
    public static readonly Text ServiceRemoved = new("Service {0} removed; files and logs kept.");

    // ps
    public static readonly Text PsInstance = new("  Instance {0}");
    public static readonly Text PsService = new("Service {0} | {1} | {2} | instance {3}");
    public static readonly Text NoneFeminine = new("none");
    public static readonly Text PsUsage = new("Use ps [-w|--watch [--interval <s>]] with a valid service selection.");
    public static readonly Text HardwareUnavailable = new("Hardware not available: {0}");
    public static readonly Text PsLinuxServices = new("Services: persisted identity only supported on Windows; PID/API are not queried from Linux.");
    public static string PresetState(string state) => state switch { "loaded" => PresetLoaded, "unloaded" => PresetUnloaded, _ => state };
    internal static readonly Text PresetLoaded = new("loaded");
    internal static readonly Text PresetUnloaded = new("published, not loaded");
    public static readonly Text PsNoPresets = new("  No published presets.");
    public static readonly Text PsNoServices = new("No known services in the selection.");
    public static readonly Text PsProjectIndex = new("Project index: {0}");
    public static readonly Text PsScopeError = new("Scope {0}: {1}");
    public static readonly Text PsWatchEntry = new("  {0}: {1} | catalog {2} | ");
    public static readonly Text UnavailableReason = new("not available: {0}");
    public static readonly Text PsHardware = new("Hardware {0} | {1} | machine scope");
    public static readonly Text PsGpu = new("GPU {0} | {1} | shared by the machine");
    public static readonly Text PsIntention = new("  Intent: {0}; recovery: {1}; composition confirmed in API: {2}");
    public static readonly Text PsIntentPresets = new("  Presets in the current intent of the recipe: {0}");
    public static readonly Text PsActiveComposition = new("  Registered run composition: {0}; draft: {1}");
    public static readonly Text PsSample = new("  {0}: {1} | source {2} | scope {3} | {4}");
    public static readonly Text PsSampleWindow = new(" | window {0} ms");
    public static readonly Text PsSampleNoWindow = new(" | window not reported/instant");

    // recipe edit
    public static readonly Text ResetGlobalCatalogQuestion = new("Reset the global catalog? [y/N]");
    public static readonly Text DefinitionPresent = new("The definition is already present.");
    public static readonly Text LiteralListReference = new("The format does not accept references written as literal text inside a list.");
    public static readonly Text LayerSelectedTwice = new("A YAML layer is selected more than once.");
    public static readonly Text SavedIn = new("Saved in {0}.");
    public static readonly Text SeeSyntax = new("See the syntax with coquena --help.");
    public static readonly Text EditInvalid = new("Invalid edit syntax or selection.");

    // data views
    public static readonly Text TreeEmpty = new("(empty)");
    public static readonly Text TreeYes = new("yes");
    public static readonly Text TreeNo = new("no");
}
