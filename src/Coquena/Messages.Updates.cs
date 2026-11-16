namespace Coquena;

// update and upgrade reports without --json.
public static partial class Messages
{
    public static readonly Text UpdateNoResources = new("The recipe declares no resources to query.");
    public static readonly Text UpdateAllCurrent = new("Everything is up to date.");
    public static readonly Text UpdateOneAvailable = new("There is 1 update available.");
    public static readonly Text UpdateManyAvailable = new("There are {0} updates available.");
    public static readonly Text UpdateHowToApply = new("To apply them: coquena upgrade.");
    public static readonly Text UpgradeNothing = new("There was nothing to update.");
    public static readonly Text UpgradeNoneApplied = new("No update was applied.");
    public static readonly Text UpgradeOneApplied = new("1 update was applied.");
    public static readonly Text UpgradeManyApplied = new("{0} updates were applied.");
    public static readonly Text UpgradeRestartNotice = new("Running services keep the previous version until they are restarted.");
    public static readonly Text UpdateCompanion = new("Companion {0}");
    public static readonly Text UpdateModel = new("Model {0}");
    public static readonly Text UpdateNewVersion = new("new version");
    public static readonly Text UpdateRevision = new("revision {0} → {1}");
    public static readonly Text UpdateNextStart = new("{0}; it will be used on the next start");
    public static readonly Text UpdateSkipped = new("no changes");
    public static readonly Text UpdateFixedVersion = new("the recipe pins the version; to update, change it to latest");
    public static readonly Text UpdateUnchanged = new("up to date");
    public static readonly Text UpdateNotApplicable = new("external file; Coquena does not update it");
    public static readonly Text UpdateNotPrepared = new("not downloaded yet; it is prepared with coquena up");
    public static readonly Text UpdateInconclusive = new("no compatible version was found to compare");
    public static readonly Text UpdateQueryFailed = new("the origin could not be queried; check the connection and repeat");
    public static readonly Text UpdateIntervention = new("the origin changed and the files must be chosen; repeat update in an interactive terminal");
    public static readonly Text UpdateOrphaned = new("no longer in the origin");
}
