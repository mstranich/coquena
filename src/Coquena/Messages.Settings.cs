namespace Coquena;

// Configuración view of the TUI.
public static partial class Messages
{
    public static readonly Text SettingsDirectories = new("DIRECTORIES");
    public static readonly Text SettingsModels = new("Models");
    public static readonly Text SettingsBackends = new("Backends");
    public static readonly Text SettingsObservation = new("OBSERVATION");
    public static readonly Text SettingsRetryInitial = new("Initial reconnection");
    public static readonly Text SettingsRetryMaximum = new("Maximum reconnection");
    public static readonly Text SettingsInterval = new("Reading interval");
    public static readonly Text SettingsLogs = new("LOGS");
    public static readonly Text SettingsRetention = new("Retention");
    public static readonly Text SettingsParameters = new("PARAMETERS");
    public static readonly Text SettingsGlobalCatalog = new("Global catalog");
    public static readonly Text SettingsSimulateReset = new(" Simulate reset ");
    public static readonly Text SettingsReset = new(" Reset ");
    public static readonly Text SettingsSave = new(" Save ");
    public static readonly Text SettingsReload = new(" Reload ");
    public static readonly Text SettingsLog = new("LOG");
    public static readonly Text SettingsHideMonitoring = new("Hide Coquena's monitoring requests");
    public static readonly Text SettingsBottomBar = new("BOTTOM BAR");
    public static readonly Text SettingsLanguage = new("LANGUAGE");
    public static readonly Text SettingsPreferenceNotSaved = new("The preference could not be saved: {0}");
    public static readonly Text SettingsReloaded = new("Values reloaded from {0}.");
    public static readonly Text SettingsNotRead = new("The configuration could not be read: {0}");
    public static readonly Text SettingsGpuNoData = new("GPU · no data");
    public static readonly Text SettingsDefaultsLoaded = new("Default values loaded; Save applies them.");
    public static readonly Text SettingsMoveRunning = new("A directory move is in progress.");
    public static readonly Text SettingsReconnection = new("reconnection");
    public static readonly Text SettingsIntervalDone = new("reading interval");
    public static readonly Text SettingsRetentionDone = new("retention");
    public static readonly Text SettingsNotSaved = new("Not saved: {0}");
    public static readonly Text SettingsNoChanges = new("No changes.");
    public static readonly Text SettingsSaved = new("Saved: {0}.");
    public static readonly Text SettingsDirectoryOf = new("Directory of {0}");
    public static readonly Text SettingsCancel = new("Cancel");
    public static readonly Text SettingsKeepAtOrigin = new("Keep at origin");
    public static readonly Text SettingsMove = new("Move");
    public static readonly Text SettingsOverwriteFiles = new("Overwrite files");
    public static readonly Text SettingsOverwrite = new("Overwrite");
    public static readonly Text SettingsDirectoryNotChanged = new("The directory of {0} was not changed: {1}");
    public static readonly Text SettingsFieldMessage = new("{0}: {1}");
    public static readonly Text SettingsSimulateTitle = new("Simulate reset · global catalog");
    public static readonly Text SettingsResetTitle = new("Reset global catalog");
    public static readonly Text SettingsResetButton = new("Reset");
    public static readonly Text SettingsCatalogReset = new("Global catalog reset.");
    public static readonly Text SettingsCatalogKept = new("Cancelled; catalog kept.");
    public static readonly Text SettingsCatalogNotReset = new("The catalog was not reset: {0}");
    public static readonly Text SettingsInteger = new("{0}: integer between {1} and {2}.");
    public static readonly Text SettingsEdited = new("edited");
    public static readonly Text SettingsOwn = new("own");
    public static readonly Text SettingsDefault = new("default");
    public static readonly Text SettingsLanguageAuto = new("Automatic ({0})");
    public static readonly Text SettingsLanguageSaved = new("Language: {0}. It applies the next time Coquena starts.");
    public static readonly Text SettingsGeneral = new("General");
}
