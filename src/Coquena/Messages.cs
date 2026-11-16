using System.Globalization;

namespace Coquena;

// Texts shown to people, kept apart from the logic that decides them. Code decides with
// codes and data; this catalog turns them into words. Tests compare codes, data or these
// entries, never literal wording. English lives here; other languages map the same keys
// (the member names) in languages/<code>.json. See Localization.
// Style: plain, impersonal wording, no defensive disclaimers.
public static partial class Messages
{
    // An explicit static constructor makes the first use of any member register every text.
    static Messages() { }
    internal static void Load() { }

    // Errors keep their code at the end, for scripts and reports: «Error: text (code).»
    public static string Error(string code, string text) => ErrorFormat.Format(text.TrimEnd().TrimEnd('.'), code);
    internal static readonly Text ErrorFormat = new("Error: {0} ({1}).");

    // Answers to [y/N] questions, comma separated.
    internal static readonly Text YesAnswers = new("y,yes");
    public static bool IsYes(string? answer) => answer?.Trim() is { Length: > 0 } text &&
        YesAnswers.ToString().Split(',').Contains(text, StringComparer.OrdinalIgnoreCase);

    // languages
    public static readonly Text LanguageNotLoaded = new("The language {0} could not be loaded; English is used instead: {1}");
    public static readonly Text LanguageEmpty = new("The language file is empty.");
    public static readonly Text BackendMissingCudart = new("A matching cudart dependency for the same release, toolkit and platform is missing.");
    public static readonly Text LanguageInvalidValue = new("Invalid or duplicated text key: {0}.");
    public static readonly Text LanguageInvalidFormat = new("Invalid text placeholders.");
    public static readonly Text LanguageUnknownKey = new("Unknown text key: {0}.");
    public static readonly Text LanguagePlaceholders = new("The placeholders of {0} do not match the English text.");
    public static readonly Text LanguageShow = new("Language: {0} (in use: {1}).");
    public static readonly Text LanguageUnavailable = new("The language {0} is not available. See: coquena config language list");
    public static readonly Text LanguageExported = new("Language {0} written to {1}.");
    public static readonly Text LanguageRestart = new("The change applies the next time Coquena starts.");
    public static string LanguageOrigin(string origin) => origin switch
    {
        "built-in" => LanguageBuiltIn, "embedded" => LanguageEmbedded, _ => LanguageExternal
    };
    internal static readonly Text LanguageBuiltIn = new("built-in");
    internal static readonly Text LanguageEmbedded = new("embedded");
    internal static readonly Text LanguageExternal = new("external file");
    public static readonly Text LanguageAuto = new("automatic");

    public static string UnknownOptions(string command) => Error("invalid-selection", UnknownOptionsText.Format(command));
    internal static readonly Text UnknownOptionsText = new("Those options are not recognized. See: coquena {0} --help");
    public static readonly Text UnknownCommand = new("That command is not recognized. See: coquena --help");

    public static string Kind(string kind) => kind switch
    {
        "backend" => KindBackend, "companion" => KindCompanion, "download" => KindDownload, _ => KindModel
    };
    internal static readonly Text KindBackend = new("Backend");
    internal static readonly Text KindCompanion = new("Companion");
    internal static readonly Text KindDownload = new("Download");
    internal static readonly Text KindModel = new("Model");

    // prune
    public static string PruneItem(string kind, string name, long bytes) => $"{Kind(kind)} {name}  {ByteSize.Format(bytes)}";
    public static string PruneReclaimable(long bytes, bool global) => bytes == 0 ? PruneNothing :
        PruneCanFree.Format(ByteSize.Format(bytes), global ? " --global" : "");
    internal static readonly Text PruneNothing = new("There is nothing to free.");
    internal static readonly Text PruneCanFree = new("{0} can be freed. To delete it: coquena prune{1} --yes.");
    public static string PruneKept(string name, string code) => code switch
    {
        "file-ownership-uncertain-or-external" or "file-ownership-uncertain" => PruneKeptOwnership.Format(name),
        "file-inaccessible" => PruneKeptUnreadable.Format(name),
        _ => PruneKeptCode.Format(name, code)
    };
    internal static readonly Text PruneKeptOwnership = new("{0} is kept: the file could not be confirmed as Coquena's.");
    internal static readonly Text PruneKeptUnreadable = new("{0} is kept: the file could not be read.");
    internal static readonly Text PruneKeptCode = new("{0} is kept ({1}).");
    public static string PruneKept(Projects.CleanupNote note) => note.Reference is { } reference
        ? PruneKeptReference.Format(note.Name, CleanupReference(reference)) : PruneKept(note.Name, note.Code);
    internal static readonly Text PruneKeptReference = new("{0} is kept: {1}.");
    public static string CleanupReference(Projects.UsageReference reference) => (reference.Kind switch
    {
        "combination-index" => ReferenceCombination,
        "update-plan" => ReferenceUpdatePlan,
        "active" => ReferenceActive,
        "last-good" => ReferenceLastGood,
        "download" => ReferenceDownload,
        _ => ReferenceService
    }).Format(reference.Scope);
    internal static readonly Text ReferenceCombination = new("a combination registered in {0} uses it");
    internal static readonly Text ReferenceUpdatePlan = new("a pending update in {0} needs it");
    internal static readonly Text ReferenceActive = new("it is part of a run registered in {0}");
    internal static readonly Text ReferenceLastGood = new("it is kept for recovery in {0}");
    internal static readonly Text ReferenceDownload = new("a pending download in {0} needs it");
    internal static readonly Text ReferenceService = new("a service or publication registered in {0} references it");
    public static string UsageReferencedRow(string name, Projects.UsageReference reference) => UsageReferencedRowText.Format(name, CleanupReference(reference));
    internal static readonly Text UsageReferencedRowText = new("    {0} is kept: {1}.");
    public static string PruneConfirmationRequired => Error("confirmation-required", PruneConfirmationText);
    internal static readonly Text PruneConfirmationText = new("Confirmation is missing. Repeat with --yes to delete what is shown");
    public static readonly Text PruneAsk = new("Delete what is shown? [y/N] ");
    public static string PruneFreed(long bytes) => PruneFreedText.Format(ByteSize.Format(bytes));
    internal static readonly Text PruneFreedText = new("✔ {0} freed.");
    public static string PruneFailed(string name, string code) => Error(code, PruneFailedText.Format(name));
    internal static readonly Text PruneFailedText = new("{0} could not be deleted");
    public static readonly Text CombinationForgotten = new("Combination forgotten. The YAML, weights and publications are kept.");

    // Maintenance: codes and persisted data remain independent of these texts.
    public static readonly Text UsageDescription = new("Shows the space used and the files that can be deleted in each project and in global storage. Alias: df.");
    public static readonly Text UsageEmptySelection = new("There are no Coquena files in the selected projects or global storage.");
    public static readonly Text UsageNoProject = new("There is no project in this folder.");
    public static readonly Text UsageProjectEmpty = new("This project has no files saved by Coquena yet.");
    public static readonly Text UsageGlobalEmpty = new("There are no files in global storage yet.");
    public static readonly Text UsageHere = new("This project");
    public static readonly Text UsageGlobal = new("Global");
    public static readonly Text UsageProject = new("Project");
    public static readonly Text UsageOtherProject = new("Other project");
    public static readonly Text UsageBlockedDetails = new("⚠ Coquena could not open these registered projects. This does not prevent cleaning resources without current references:");
    public static readonly Text UsageForgetHint = new("  See the list with coquena project list. If those projects are no longer used, review their removal with coquena project forget --inaccessible --dry-run.");
    public static readonly Text UsageBlocked = new("  Cannot clean yet: Coquena could not read some projects and does not know which files they still use.");
    public static readonly Text UsageNothingToClean = new("  There are no files that can be deleted with the chosen filters.");
    public static readonly Text MaintenanceModels = new("Models");
    public static readonly Text MaintenanceCompanions = new("Companions");
    public static readonly Text MaintenanceBackends = new("Backends");
    public static readonly Text UsageDownloadUncertain = new("to review");
    public static readonly Text UsageDownloadReclaimable = new("can be deleted");
    public static readonly Text ProjectHelpDescription = new("Lists the projects registered in Coquena or removes the chosen ones from that list. A full identifier or its beginning can be used, if it identifies a single project.");
    public static readonly Text ProjectInvalidArguments = new("Those arguments are not recognized. See coquena project --help.");
    public static readonly Text ProjectForgetConfirmationRequired = new("Review the projects with --dry-run. To remove them from the list without an interactive question, use --yes.");
    public static readonly Text ProjectForgetAsk = new("Forget the projects shown? [y/N] ");
    public static readonly Text CleanupTypeMismatch = new("The given resource does not belong to any of the chosen types.");
    public static readonly Text ProjectUniqueIdRequired = new("Use an identifier from coquena project list. If only the beginning is written, it must identify a single project.");
    public static readonly Text ProjectUnreachableForCleanup = new("That project could not be opened. Check its location and permissions with coquena project list. If it is no longer used, it can be removed with coquena project forget.");
    public static readonly Text ProjectForgetSelectionRequired = new("Give the projects to forget, or use --inaccessible to choose all the ones Coquena cannot open.");
    public static readonly Text ProjectIndexEmpty = new("There are no known projects.");
    public static readonly Text ProjectUnknownId = new("No project is registered with that identifier. See coquena project list.");
    public static readonly Text ProjectAmbiguousId = new("That beginning matches several projects. Write more characters of the identifier.");
    public static readonly Text ProjectIndexChanged = new("The project list changed since it was shown. Review it again before forgetting projects.");
    public static readonly Text ProjectChangedBeforeForget = new("One of the projects changed since it was shown. Review the list again before forgetting it.");
    public static readonly Text CleanupScopesChanged = new("The projects related to this cleanup changed. Check again what can be deleted.");
    public static readonly Text CleanupWrongScope = new("The list of files to delete belongs to another project or storage.");
    public static readonly Text CleanupFilesChanged = new("The files or their references changed. Check again what can be deleted.");
    public static readonly Text CleanupUnknownOperation = new("There is a download folder Coquena does not recognize. It is kept to avoid deleting files without knowing which download they belong to.");
    public static readonly Text CleanupTransferIdentityUnknown = new("The record of a download does not match the project. The files are kept; review the download before cleaning.");
    public static readonly Text CleanupOperationDirectoryMismatch = new("The download identifier does not match its folder. Its files are kept.");
    public static readonly Text CleanupInstallationLocationUnknown = new("The installation folder could not be verified. Its files are kept.");
    public static readonly Text CleanupNotRegularFile = new("The path is a folder or a link. It is not deleted as a Coquena file.");
    public static readonly Text CleanupProjectUnreadable = new("Cannot clean yet: Coquena could not read a project and does not know which files it still uses. Check coquena project list. If it is no longer used, review its removal with coquena project forget --inaccessible --dry-run.");
    public static readonly Text MaintenanceSelectors = new("[--global] [-f file] [--project id] [--model|--companion|--backend] [--select reference] [--downloads]");
    public static string UsageHelp => "usage " + MaintenanceSelectors + "\n" + UsageDescription;
    public static string ProjectHelp => "project list [--json]\nproject forget <id>... [--dry-run|--yes]\nproject forget --inaccessible [--dry-run|--yes]\n" + ProjectHelpDescription;
    public static string CleanupHelp => "clean " + MaintenanceSelectors + " [--dry-run|--yes]\n" + CleanupHelpText;
    internal static readonly Text CleanupHelpText = new("Alias: prune.\nWithout choosing a project or --global, the project of the current folder is cleaned.\nWithout filters, installations that are no longer used are chosen.");
    public static string UsageInvalidOptions => UsageInvalidOptionsText.Format(UsageHelp.Split('\n')[0]);
    internal static readonly Text UsageInvalidOptionsText = new("Those options are not recognized. Usage: {0}");
    public static string UsageFailed(string detail) => UsageFailedText.Format(detail);
    internal static readonly Text UsageFailedText = new("The space could not be measured: {0}");
    public static string UsageHeading(string title, long bytes, string folder) => $"{title}: {ByteSize.Format(bytes)}  ({folder})";
    public static string UsageResourceRow(string label, long bytes, long free) =>
        $"  {label,-12}{ByteSize.Format(bytes),12}" + (free > 0 ? UsageResourceFree.Format(ByteSize.Format(free)) : "");
    internal static readonly Text UsageResourceFree = new("   {0} can be deleted");
    public static string UsageDownloadsRow(long bytes) => $"  {UsageDownloads.ToString(),-12}{ByteSize.Format(bytes),12}   " + UsageDownloadsDetail;
    internal static readonly Text UsageDownloads = new("Downloads");
    internal static readonly Text UsageDownloadsDetail = new("temporary files and records");
    public static string UsageDownloadRow(string name, long bytes, Guid id, bool blocked) =>
        UsageDownloadRowText.Format(blocked ? UsageDownloadUncertain : UsageDownloadReclaimable, name, ByteSize.Format(bytes), id.ToString("D"));
    internal static readonly Text UsageDownloadRowText = new("    download ({0}): {1} ({2}) · {3}");
    public static string UsageLogsRow(long bytes) => $"  {"Logs",-12}{ByteSize.Format(bytes),12}";
    public static string UsageUnusedRow(string name, long bytes) => $"    {name} ({ByteSize.Format(bytes)})";
    public static readonly Text UsageRemovableResources = new("  To delete · without references or protection:");
    public static readonly Text UsageKeptHeading = new("  Kept:");
    public static string UsageKeptRow(string name, IEnumerable<string> reasons) => $"    {name} — {string.Join("; ", reasons)}";
    public static string UsageInstallationLabel(string name, string id) => UsageInstallationLabelText.Format(name, id[..Math.Min(8, id.Length)]);
    internal static readonly Text UsageInstallationLabelText = new("{0} [installation {1}]");
    public static string UsageShortReference(Projects.UsageReference reference, bool local) =>
        (reference.Kind switch { "combination-index" => ShortCombination, "update-plan" => ShortUpdate,
            "active" => ShortRun, "last-good" => ShortRecovery, "download" => ShortDownload, _ => ShortService }).ToString() +
        (local ? "" : $" ({reference.Scope})");
    internal static readonly Text ShortCombination = new("combination");
    internal static readonly Text ShortUpdate = new("pending update");
    internal static readonly Text ShortRun = new("run");
    internal static readonly Text ShortRecovery = new("recovery");
    internal static readonly Text ShortDownload = new("pending download");
    internal static readonly Text ShortService = new("service/publication");
    public static string UsageUnavailableSummary(int paths) => UsageUnavailableSummaryText.Format(paths);
    internal static readonly Text UsageUnavailableSummaryText = new("⚠ Inaccessible project paths: {0}. They do not block this cleanup.");
    public static readonly Text UsageUnavailableHint = new("  Review: coquena project list · Forget: coquena project forget --inaccessible");
    public static string UsageUncertainRow(string name, long bytes) => UsageUncertainRowText.Format(name, ByteSize.Format(bytes));
    internal static readonly Text UsageUncertainRowText = new("    could be in use by a project that could not be read: {0} ({1})");
    public static string UsageExternal(long bytes) => UsageExternalText.Format(ByteSize.Format(bytes));
    internal static readonly Text UsageExternalText = new("  Includes {0} of external files that Coquena does not delete.");
    public static string UsageReclaimable(long bytes, string command) => UsageReclaimableText.Format(ByteSize.Format(bytes), command);
    internal static readonly Text UsageReclaimableText = new("  {0} can be freed with {1}.");
    public static string ProjectForgetSummary(int count) => (count == 1 ? ProjectForgetOne.ToString() : ProjectForgetMany.Format(count)) + " " + ProjectForgetEffect;
    internal static readonly Text ProjectForgetOne = new("1 project will be removed from Coquena's list.");
    internal static readonly Text ProjectForgetMany = new("{0} projects will be removed from Coquena's list.");
    internal static readonly Text ProjectForgetEffect = new("Their files are not deleted. Once they are no longer considered, some global files could become available to clean. Files used by other projects or services are kept.");
    public static string CleanupReclaimable(long bytes) => bytes == 0 ? CleanupNothing : CleanupCanFree.Format(ByteSize.Format(bytes));
    internal static readonly Text CleanupNothing = new("There are no files to delete with these filters.");
    internal static readonly Text CleanupCanFree = new("{0} can be freed.");
    public static readonly Text MaintenanceWorking = new("Working… you can switch views while it continues.");
    public static readonly Text MaintenanceVerify = new("Verify");
    public static readonly Text MaintenanceVerifyHint = new("check the recipe and its files");
    public static readonly Text MaintenancePreview = new("Clean…");
    public static readonly Text MaintenanceCleanupHint = new("Coquena files no longer in use");
    public static readonly Text MaintenanceDownloads = new("Stopped downloads");
    public static readonly Text MaintenanceColumns = new("RESOURCE / INSTALLATION                 SIZE");
    public static readonly Text MaintenanceEmpty = new("There are no files to delete with these filters.");
    public static readonly Text MaintenanceDelete = new("Delete what is shown…");
    public static readonly Text MaintenanceForget = new("Forget…");
    public static string MaintenanceTitle(string scope) => MaintenanceTitleText.Format(scope);
    internal static readonly Text MaintenanceTitleText = new("Maintenance of {0}");
    public static string MaintenanceRecoverable(long bytes) => MaintenanceRecoverableText.Format(ByteSize.Format(bytes));
    internal static readonly Text MaintenanceRecoverableText = new("Can be freed: {0}");
    public static string MaintenanceDownloadName(string name) => MaintenanceDownloadNameText.Format(name);
    internal static readonly Text MaintenanceDownloadNameText = new("Download {0}");
    public static string MaintenancePlanReady(string scope, long bytes) => MaintenancePlanReadyText.Format(scope, ByteSize.Format(bytes));
    internal static readonly Text MaintenancePlanReadyText = new("Cleanup of {0}: {1} can be freed.");
    public static string MaintenanceDeleteQuestion(int count, long bytes) => MaintenanceDeleteQuestionText.Format(count, ByteSize.Format(bytes));
    internal static readonly Text MaintenanceDeleteQuestionText = new("The {0} items shown will be deleted to free {1}.\nBefore deleting, Coquena checks again that they are not in use.");
    public static readonly Text MaintenanceDeleteConfirm = new("Delete what is shown");
    public static readonly Text MaintenanceCancel = new("Cancel");
    public static string MaintenanceCleanupTitle(string scope) => MaintenanceCleanupTitleText.Format(scope);
    internal static readonly Text MaintenanceCleanupTitleText = new("Clean · {0}");
    public static string MaintenanceForgetTitle(string scope) => MaintenanceForgetTitleText.Format(scope);
    internal static readonly Text MaintenanceForgetTitleText = new("Forget project · {0}");
    public static readonly Text MaintenanceForgetErrorTitle = new("Forget project");
    public static readonly Text MaintenanceForgetConfirm = new("Forget");
    public static readonly Text MaintenanceAccept = new("OK");
    public static string MaintenanceLastCleanup(DateTimeOffset at) => MaintenanceLastCleanupText.Format(at.ToString("dd/MM HH:mm", CultureInfo.CurrentCulture));
    internal static readonly Text MaintenanceLastCleanupText = new("Last cleanup of this session · {0}");
    public static readonly Text CleanupDownloadChanged = new("The download changed since it was shown. Check again what can be deleted.");
    public static readonly Text CleanupDownloadSizeChanged = new("The download size changed since it was shown. Check again what can be deleted.");
    public static readonly Text CleanupInvalidOptions = new("Those options are not recognized. See coquena clean --help.");
    public static readonly Text CleanupSelectionChanged = new("The list of files to delete changed since it was shown. Repeat clean to review it.");
    public static readonly Text CleanupGlobalHeading = new("Global: ");
    public static readonly Text CleanupProjectHeading = new("Project: ");
    public static readonly Text ProjectCannotInitialize = new("This folder cannot be initialized: it already has a recipe or Coquena cannot access it.");

    // recipes and authoring
    public static readonly Text CombinationNotPrepared = new("The combination has not been prepared yet. Use it in Up or Start to be able to check for updates.");
    public static readonly Text RecipeValid = new("The combination is valid with the current drafts.");
    public static string AtLine(string file, int line) => AtLineText.Format(file, line);
    internal static readonly Text AtLineText = new("{0} · line {1} · ");
    public static string MoreRows(int count) => MoreRowsText.Format(count);
    internal static readonly Text MoreRowsText = new("… {0} more");
    public static readonly Text NoUpdatePlan = new("No update plan registered.");
    public static readonly Text ProjectPathGone = new("The project path can no longer be resolved.");
    public static readonly Text NoKnownProjects = new("There are no known projects.");
    public static readonly Text ForgetProjectHint = new("remove it from the known projects; no files are deleted");
    public static string ProjectForgotten(string folder) => ProjectForgottenText.Format(folder);
    internal static readonly Text ProjectForgottenText = new("Project forgotten: {0}. No file was deleted; if it is used again, it is registered again.");
    public static string ProjectState(string code) => code switch
    {
        "available" => ProjectAvailable,
        "no-recipe" => ProjectNoRecipe,
        _ => ProjectInaccessible
    };
    internal static readonly Text ProjectAvailable = new("available");
    internal static readonly Text ProjectNoRecipe = new("no recipe");
    internal static readonly Text ProjectInaccessible = new("inaccessible");
    public static string ReferencesToEntry(int count) => count == 1 ? ReferenceToEntryOne : ReferencesToEntryMany.Format(count);
    internal static readonly Text ReferenceToEntryOne = new(" (and 1 reference to this entry)");
    internal static readonly Text ReferencesToEntryMany = new(" (and {0} references to this entry)");
    public static readonly Text RestNotValidated = new(" The rest of the file could not be validated.");
    public static readonly Text SchemaReferenceUpdated = new("Schema reference updated");
    public static readonly Text SchemaReferenceAdded = new("Schema reference added");

    // updates view
    public static readonly Text UpdatePlanStale = new("▲ Something changed since the last check; press Check to update the plan.");
    public static readonly Text UpdateImpact = new("Active services stay the same until they are restarted.");
    public static string UsedByIntentions(int count) => UsedByIntentionsText.Format(count);
    internal static readonly Text UsedByIntentionsText = new("in {0} intentions");

    // directories
    public static readonly Text MergeOverwriteQuestion = new("Allow all the reported overwrites? [y/N]");
    public static string MergeOverwrite(int files) => MergeOverwriteText.Format(files, MergeOverwriteQuestion);
    internal static readonly Text MergeOverwriteText = new("The merge will replace the previous content of {0} file(s). {1}");

    // system and backends
    public static string BackendChoice(bool chosen) => chosen ? BackendExplicit : BackendAutomatic;
    internal static readonly Text BackendExplicit = new("Explicit selection");
    internal static readonly Text BackendAutomatic = new("Automatic choice");
    public static readonly Text BackendQueryOnly = new("Query only; it does not download, install or change the default selection.");
    public static readonly Text ProbeLocalOnly = new("Local probe; it does not validate a llama-server binary or guarantee inference.");
    public static string NativePlatform(string platform) => NativePlatformText.Format(platform);
    internal static readonly Text NativePlatformText = new("Native platform: {0}");
    public static string PhysicalRam(string? size) => PhysicalRamText.Format(size ?? Unknown);
    internal static readonly Text PhysicalRamText = new("Physical RAM: {0}");
    internal static readonly Text Unknown = new("unknown");
    public static string SavedCandidate(string build) => SavedCandidateText.Format(build);
    internal static readonly Text SavedCandidateText = new("Saved candidate: {0}");

    // configuration diagnostics, used inside longer lines
    public static readonly Text NotServable = new("not servable");
    public static readonly Text Unsupported = new("not supported");
    public static readonly Text BrokenReference = new("broken reference");
    public static readonly Text MissingModelFile = new("Model file missing");
    public static string MissingPreset(string preset) => MissingPresetText.Format(preset);
    internal static readonly Text MissingPresetText = new("Missing preset: {0}");
    public static readonly Text MissingLocalReference = new("missing local reference");
    public static readonly Text RemoveEffect = new("Remove");
    public static readonly Text AfterChange = new("After:");
    public static string KeepEffect(string emission) => KeepEffectText.Format(emission);
    internal static readonly Text KeepEffectText = new("Keep/{0}");

    // models
    public static readonly Text NotPaired = new("not paired");
    public static readonly Text NoUnits = new("No units registered in the requested scope.");
    public static readonly Text ExternalNotApplicable = new("not applicable: external");
    public static readonly Text NotSubstituted = new("Not substituted.");
    public static readonly Text PartialExploration = new("may be partial/inconclusive");
    public static string PendingUpgrade(bool transition) => transition ? PendingTransition : PendingUpgradeText;
    internal static readonly Text PendingTransition = new("Pending transition");
    internal static readonly Text PendingUpgradeText = new("Pending upgrade");
    public static readonly Text CleanupCompleted = new("and cleanup completed");
    public static readonly Text ReadmeTitle = new("Commit README");
    public static string DownloadVariant(string label) => DownloadVariantText.Format(label);
    internal static readonly Text DownloadVariantText = new("Download {0}");

    // logs and observation
    public static readonly Text LogUnavailable = new("Not available");
    public static readonly Text LogFileUnavailable = new("File not available; it will be retried without controlling the service.");
    public static readonly Text ChildChanged = new("The child changed during the query; sample discarded.");
    public static readonly Text NotAvailable = new("not available");
    public static readonly Text ModelNotLoaded = new("not loaded");
    public static readonly Text MetricsScope = new("average backend rates, not per task or instantaneous");
    public static readonly Text BucketUnknown = new("Bucket period not reported");
    public static readonly Text ObservationFrame = new("Observation");
    public static readonly Text NewInstanceSpan = new("New span");
    public static readonly Text NoProgressDenominator = new("no denominator");
    public static readonly Text RequestUnconfirmed = new("request result not confirmed");
    public static readonly Text ConnectionCut = new("the connection was cut");
    public static readonly Text TransferFailed = new("the download failed");
    public static readonly Text NoDownloads = new("There are no downloads.");
    public static readonly Text DownloadCancelled = new("The download was cancelled; nothing was installed.");
    public static readonly Text TransferCut = new("the download was cut");
    public static readonly Text ServerBusy = new("The server is serving requests. Applying the changes now can interrupt them.");
    public static readonly Text HookTimedOut = new("[Hook] The start failure notification did not complete; the service result is kept.");
    public static readonly Text ComputeUnconfirmed = new("not confirmed");

    // foreground servers
    public static readonly Text ForegroundHint = new("[Foreground] Ctrl+C: orderly stop (10 s); second Ctrl+C: immediate forced close.");
    public static readonly Text CtrlCReceived = new("[Ctrl+C received] Stopping the server; 10 s at most. Press Ctrl+C again to force an immediate close.");
    public static readonly Text SecondCtrlC = new("[Forced close] Second Ctrl+C received; closing immediately.");
    public static readonly Text NoActiveInstance = new("no active instance");
    public static readonly Text UncertainIdentity = new("uncertain identity");
    public static readonly Text FreedThisAttempt = new("Space actually freed in this attempt");
    public static readonly Text ExternalRemoved = new("External record removed; files kept on disk.");

    // service recovery
    public static readonly Text RecoveryCleared = new("The service started correctly; the record of the previous failure was discarded.");
    public static readonly Text RecoveryUnavailable = new("It was not possible to go back to a configuration that worked: there is no previous one or the operation was cancelled.");
    public static readonly Text RecoveryFellBack = new("It went back to the last configuration that worked; the failure of the new one is recorded.");
    public static string RecoveryKeptPrevious(string code) => RecoveryKeptPreviousText.Format(code);
    internal static readonly Text RecoveryKeptPreviousText = new("The new configuration did not start ({0}); the service keeps running with the previous one.");
    public static string RecoveryFailed(string code) => RecoveryFailedText.Format(code);
    internal static readonly Text RecoveryFailedText = new("The last configuration that worked did not start either ({0}); nothing else is attempted.");

    // verify
    public static readonly Text VerifyAllOkPrefix = new("✔ All in order");
    public static string VerifyAllOk(int resources, bool checksum) => (checksum ? VerifyAllOkChecksum : VerifyAllOkQuick).Format(VerifyAllOkPrefix, resources);
    internal static readonly Text VerifyAllOkChecksum = new("{0}: {1} resources checked with SHA-256.");
    internal static readonly Text VerifyAllOkQuick = new("{0}: {1} resources checked (without SHA-256; use --checksum to compare it).");
    public static string VerifyStatus(string status, bool checksum) => status switch
    {
        "verified" => VerifyVerified,
        "quick-ok" => VerifyQuickOk,
        "unavailable" => VerifyUnavailable,
        "missing-file" => VerifyMissingFile,
        "read-error" => VerifyReadError,
        "changed-during-read" => VerifyChangedDuringRead,
        "invalid-structure" => VerifyInvalidStructure,
        "no-reference" => VerifyNoReference,
        "mismatch" => checksum ? VerifyMismatchChecksum : VerifyMismatchSize,
        "known-integrity-failure" => VerifyKnownFailure,
        _ => status
    };
    internal static readonly Text VerifyVerified = new("SHA-256 matches");
    internal static readonly Text VerifyQuickOk = new("Quick audit OK");
    internal static readonly Text VerifyUnavailable = new("Resource not available. Check the reference in the recipe and prepare the installation.");
    internal static readonly Text VerifyMissingFile = new("The file is missing. Check the path in the recipe or download the managed resource again.");
    internal static readonly Text VerifyReadError = new("It could not be read. Check the permissions and whether another process has the file locked.");
    internal static readonly Text VerifyChangedDuringRead = new("The file changed while being read. Wait for the writing to finish and repeat verify.");
    internal static readonly Text VerifyInvalidStructure = new("Invalid or incomplete GGUF structure. Check the parts selected in the recipe.");
    internal static readonly Text VerifyNoReference = new("No SHA-256 reference. Check the origin and register a reference if appropriate.");
    internal static readonly Text VerifyMismatchChecksum = new("SHA-256 differs from the reference. Recover the original bytes.");
    internal static readonly Text VerifyMismatchSize = new("Size differs from the registered one. Check that the file is complete.");
    internal static readonly Text VerifyKnownFailure = new("Pending integrity failure. Recover the resource and check it with --checksum.");
    public static string BooleanNeedsValue(string argument, string? value) => BooleanNeedsValueText.Format(argument, value);
    internal static readonly Text BooleanNeedsValueText = new("The argument {0} received the boolean {1}, but here it requires an explicit value. ");
    public static string VerifyProblems(int resources, bool checksum) => (checksum ? VerifyProblemsChecksum : VerifyProblemsQuick).Format(resources);
    internal static readonly Text VerifyProblemsChecksum = new("⚠ There are resources to review ({0} checked with SHA-256).");
    internal static readonly Text VerifyProblemsQuick = new("⚠ There are resources to review ({0} checked).");

    // import
    public static string ImportDone(IReadOnlyCollection<string> categories)
    {
        var names = string.Join(", ", categories.Select(c => c switch
        {
            "service" => ImportService.ToString(), "presets" => ImportPresets.ToString(), "model" => ImportModels.ToString(),
            "params" => ImportParams.ToString(), var other => other
        }));
        return categories.Contains("presets") ? ImportDonePresets : ImportDoneText.Format(names);
    }
    internal static readonly Text ImportService = new("service");
    internal static readonly Text ImportPresets = new("presets");
    internal static readonly Text ImportModels = new("models");
    internal static readonly Text ImportParams = new("parameters");
    internal static readonly Text ImportDonePresets = new("Recipe imported. The presets were published or are ready for the next start; the service was not started.");
    internal static readonly Text ImportDoneText = new("Recipe imported: {0}. The service was not started.");

    // Results of resource commands shown without --json.
    public static string BackendReady(string? variant, string? build, string installation) =>
        BackendReadyText.Format(string.Join(" ", new[] { variant, build }.Where(p => !string.IsNullOrEmpty(p))), installation);
    internal static readonly Text BackendReadyText = new("✔ Backend ready: {0} (installation {1}).");
    public static string ModelReady(string name, string installation) => ModelReadyText.Format(name, installation);
    internal static readonly Text ModelReadyText = new("✔ Model ready: {0} (installation {1}).");
    public static string ExternalAdded(string name, string installation) => ExternalAddedText.Format(name, installation);
    internal static readonly Text ExternalAddedText = new("✔ Registered as external: {0} (installation {1}).");
    public static string ProtectionChanged(string installation, bool on) => (on ? ProtectionOn : ProtectionOff).Format(installation);
    internal static readonly Text ProtectionOn = new("Installation {0} protected: prune does not delete it.");
    internal static readonly Text ProtectionOff = new("Installation {0} unprotected: prune deletes it if nothing uses it.");
    public static string BaselineSet(string key) => BaselineSetText.Format(key);
    internal static readonly Text BaselineSetText = new("✔ Integrity baseline saved for {0}.");
    public static string RemovePending(string path) => RemovePendingText.Format(path);
    internal static readonly Text RemovePendingText = new("{0} could not be deleted; repeat remove later.");
    public static readonly Text NoCombinations = new("There are no prepared combinations.");
}
