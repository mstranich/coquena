namespace Coquena;

// init, import, verify, update, project, combination and resource removal commands.
public static partial class Messages
{
    // init
    public static readonly Text InitUsage = new("Usage: coquena init [<directory>]");
    public static readonly Text InitExpectedFile = new("A file was expected at {0}.");
    public static readonly Text InitSchemaUpdated = new("Schema updated: {0}");
    public static readonly Text InitSchemaCreated = new("Schema created: {0}");
    public static readonly Text InitRecipeKept = new("Existing recipe kept: {0}");
    public static readonly Text InitRecipeCreated = new("Recipe created: {0}");
    public static readonly Text InitFailed = new("Error while initializing: {0}");

    // combination
    public static readonly Text CombinationUsage = new("combination list|forget <combination-id> [--global|--project-directory path]");

    // import
    public static readonly Text ImportIncomplete = new("The operation did not complete; check the local state that was kept.");
    public static readonly Text ImportOptionRepeated = new("Invalid or repeated option.");
    public static readonly Text ImportRepeated = new("Repeated option.");
    public static readonly Text ImportUnknownOption = new("Unknown import option.");
    public static readonly Text ImportUsage = new("Usage: import <recipe.yaml> [--select categories] [--replace entity] [--as-default] [--force].");
    public static readonly Text ImportNoReloadableIni = new("The instance does not offer a reloadable INI; it is not restarted on import.");
    public static readonly Text ImportApplyQuestion = new("Apply the changes now? [a] Apply / [c] Cancel");
    public static readonly Text ImportReconciledOnly = new("The previous operation was observed without sending reload again. Repeat import to apply a new candidate or complete the remainders.");
    public static readonly Text ImportPublicationIncomplete = new("Intent imported; publication incomplete. Pending: {0}");
    public static readonly Text ImportRemaindersKept = new("Previous remainders not selected are kept: {0}");

    // verify
    public static readonly Text VerifyRecipeError = new("Error in the recipe: {0}");
    public static readonly Text VerifyNotCompleted = new("Error: {0}. The verification did not complete.");
    public static readonly Text VerifyChecksumSkipped = new("SHA-256 skipped. Fix the catalog problems and repeat verify --checksum.");
    public static readonly Text VerifyPendingFailure = new("    Pending failure ({0}). Review the resource and repeat with --checksum.");
    public static string VerifyFailureKind(string key) => key switch { "$unit" => VerifyStructure, "$integrity" => VerifyIntegrity, _ => key };
    internal static readonly Text VerifyStructure = new("structure");
    internal static readonly Text VerifyIntegrity = new("integrity");
    public static readonly Text VerifyUnusedNeedsGlobal = new("--include-unused requires --global.");
    public static readonly Text UnknownSelector = new("Unknown selector.");

    // update
    public static readonly Text Cancelled = new("It was cancelled");

    // service command
    public static readonly Text ServiceInvalidArguments = new("Invalid service arguments. See coquena --help.");

    // resource removal
    public static readonly Text RemovalPartialPending = new("Partial removal pending; retry remove/uninstall before using the resource.");
    public static readonly Text RemovalStopInstances = new("Stop these instances first: {0}");
    public static readonly Text RemovalOperationsPending = new("Resume or cancel these operations first: {0}");
    public static readonly Text RemovalUnitNotRegistered = new("Unit not registered in the requested scope.");
    public static readonly Text RemovalFileShared = new("File referenced by another unit; it was not deleted.");
    public static readonly Text RemovalBackendNotRegistered = new("Backend not registered.");
    public static readonly Text RemovalInventoryRequired = new("Installation inventory required.");
    public static readonly Text RemovalDirectoryShared = new("Directory referenced by another backend; it was not deleted.");
    public static readonly Text RemovalRecordPending = new("Removal of the record pending | {0}");
    public static readonly Text RemovalPathIsDirectory = new("The file path is a directory.");
    public static readonly Text RemovalInvalidArguments = new("Error: invalid arguments. See coquena --help.");
    public static readonly Text RemovalFreed = new("{0}: {1}.");
    public static readonly Text RemovalCompleted = new("Removal completed.");
    public static readonly Text RemovalPartial = new("Partial removal; ownership kept. Repeat the same command.");
}
