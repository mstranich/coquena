namespace Coquena;

// backend and system commands.
public static partial class Messages
{
    public static readonly Text BackendProbeUsage = new("Error: invalid arguments. Usage: coquena system probe | backend remote [cpu|vulkan|cuda@<major>] [--release bNNNN|latest]. See coquena --help.");
    public static readonly Text ProbeCpu = new("CPU: {0}");
    public static readonly Text ProbeCuda = new("CUDA: {0} — {1}");
    public static readonly Text ProbeCudaDevice = new("CUDA device: {0}, SM {1}.{2}");
    public static readonly Text ProbeVulkan = new("Vulkan: {0} — {1}");
    public static readonly Text BackendUnsupportedPlatform = new("This path is limited to native Windows x64.");
    public static readonly Text BackendRemoteOrigin = new("Official origin ggml-org/llama.cpp; filter: {0}, {1}.");
    public static readonly Text Automatic = new("automatic");
    public static readonly Text BackendArtifact = new("Artifact: {0} ({1})");
    public static readonly Text BackendArtifactIndented = new("  Artifact: {0} ({1})");
    public static readonly Text UnknownSize = new("unknown");
    public static readonly Text BackendAlternatives = new("Alternatives queried (other OS/architectures are always rejected):");
    public static readonly Text BackendNoCandidate = new("The queried release offers no valid selection within the requested filter.");
    public static readonly Text BackendQueryCancelled = new("Query cancelled; no local changes.");
    public static readonly Text ErrorCodeDetailSeeHelp = new("Error: {0}. {1} See coquena --help.");
    public static readonly Text ErrorInvalidArgumentsSeeHelp = new("Error: invalid arguments. See coquena --help.");
    public static readonly Text BackendExternal = new("External backend: {0} | {1} | {2} | {3}:{4} | {5}");
    public static readonly Text BackendNoDefault = new("No default backend.");
    public static readonly Text BackendDefaultLatest = new("Default backend: {0}/{1}:latest");
    public static readonly Text BackendDefaultPinned = new("Pinned default backend: {0}");
    public static readonly Text BackendNoInstallable = new("No installable candidate.");
    public static readonly Text BackendSelection = new("Selection: ggml/{0}:{1} [{2}] — {3}");
    public static readonly Text BackendAlternative = new("Alternative: {0}:{1} {2} [{3}]");
    public static readonly Text BackendOperationForeground = new("Operation {0}; download/reinstallation in the foreground.");
    public static readonly Text ErrorSeeHelpAndStatus = new("Error: {0}. See coquena --help and download status.");
    public static readonly Text BackendResultOperation = new(" | operation {0}");
    public static readonly Text BackendUpdateUsage = new("Error: invalid arguments. Usage: coquena backend update [latest-selector]. See coquena --help.");
    public static readonly Text ErrorDetailSeeHelp = new("Error: {0} — {1}. See coquena --help.");
    public static readonly Text BackendMetadataOnly = new("Metadata query; it does not download or change the installed build or the pinned selections.");
    public static readonly Text ErrorCodeSeeHelp = new("Error: {0}. See coquena --help.");
    public static readonly Text BackendObservation = new("{0} ({1}) | installed={2} | checked={3} | {4} | {5}");
    public static readonly Text BackendNoneInScope = new("No backends in the selected scope.");
    public static readonly Text BackendNoneInstalled = new("No backends installed.");
    public static readonly Text BackendNoTracking = new("No official latest tracking registered.");
    public static readonly Text BackendNoTrackingExcluded = new("No official latest tracking registered; pins and external ones excluded.");
    public static readonly Text BackendSubcommands = new("backend (alias runtime): show, list, download, protect, unprotect, remove. See coquena --help.");
    public static readonly Text BackendInvalidArguments = new("Error: invalid backend arguments. See coquena --help.");
}
