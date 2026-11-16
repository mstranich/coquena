namespace Coquena;

public static partial class Messages
{
    public static readonly Text DisplayDriverSupportsCuda = new("Driver supports CUDA: {0}.{1}");
    public static readonly Text DisplayNativeWinXRequired = new("Native win-x64 required.");
    public static readonly Text DisplayLocalObservationsUpdatedO = new("Local observations updated: {0:O}");
    public static readonly Text DisplayIntegrityO = new("{0} | integrity {1} | {2:O}");
    public static readonly Text DisplayExternalResourceRegisteredD = new("External resource registered: {0:D} | {1}");
    public static readonly Text DisplayLocalRegistrationPreserved = new("Local registration preserved {0}.");
    public static readonly Text DisplayLocalRegistrationPreservedDetail = new("Local registration preserved {0}: {1}");
    public static readonly Text DisplayService = new("Service {0}{1} | {2} | {3}");
    public static readonly Text DisplayProject = new("Project {0}: {1}");
    public static readonly Text DisplayCtrlCCloseObserverO = new("{0} | Ctrl\u002BC: close observer | {1:O}");
    public static readonly Text DisplayWaitingForSample = new("waiting for sample");
    public static readonly Text DisplayServiceWaitingForSample = new("Service: waiting for sample");
    public static readonly Text DisplayHHMmSsProcessAPIHealth = new("{0} | {1:HH:mm:ss} | process {2} | API {3} | health {4}");
    public static readonly Text DisplayHardwareOO = new("Hardware | {0} | {1:O} \u2014 {2:O}");
    public static readonly Text DisplayOOProcessAPIHealth = new("  {0:O} \u2014 {1:O} | process {2} | API {3} | health {4}");
    public static readonly Text DisplayEntryO = new("  Entry {0}: {1} | {2:O} | {3}");
    public static readonly Text DisplayNoOwnPublicationsRecorded = new("No own publications recorded.");
    public static readonly Text DisplayAliasDoesNotExist = new("Alias does not exist.");
    public static readonly Text DisplayServiceCreateNameGlobalListShowDefault = new("service: create \u003Cname\u003E --global, list, show, default set, start, stop, restart, remove, logs [list|follow|show \u003Clog-id\u003E] [-f].");
    public static readonly Text DisplayDefaultService = new("Default service: {0}.");
    public static readonly Text DisplayProjectRow = new("project | {0} | {1}");
    public static readonly Text DisplayServiceStopped = new("Service stopped.");
    public static readonly Text DisplayServerDPort = new("Server {0} ({1:D}); port {2}.");
    public static readonly Text DisplayInstalledBuildD = new("Installed build: {0} ({1:D})");
    public static readonly Text DisplayUpUsage = new("{0} [-f file]... [--env-file file]... [--project-directory path] [--select reference]... [--json]\n{1} --global [--select reference]... [--json]\nReferences: backend://variant, backend://alias, backend://project/alias, model://key, companion://key");
    public static readonly Text DisplayVerifyUsage = new("verify [-f file] [--env-file file] [--project-directory path] [--select reference]... [--checksum] [--json]\nverify --global [--select reference]... [--include-unused] [--checksum] [--json]\nReferences: backend://variant, backend://alias, backend://project/alias, model://key, companion://key");
    public static readonly Text DisplayNoReasonReported = new("no reason reported");
    public static readonly Text DisplayDiagnostic = new("Diagnostic: {0}");
    public static readonly Text DisplayOption = new("Option: ");
    public static readonly Text DisplayReasoningTag = new("(reasoning)");
    public static readonly Text DisplayDefault = new("\u00B7 default");
    public static readonly Text DisplayPORT = new("PORT");
    public static readonly Text DisplayNotAvailable = new("n/a");
    public static readonly Text DisplayReasoningBody = new("(reasoning) {0}");
    public static readonly Text DisplayCompleteMoveBlocked = new("Complete move blocked:\n{0}");
    public static readonly Text DisplayRegisteredEntriesList = new("  Registered entries: {0}");
}
