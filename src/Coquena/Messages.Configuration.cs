namespace Coquena;

// config, preset, directories, log retention and the parameter catalog.
public static partial class Messages
{
    // Generic CLI error with a pointer to the help.
    public static readonly Text ErrorSeeHelp = new("Error: {0} See coquena --help.");
    public static readonly Text InvalidArguments = new("Invalid arguments.");

    // config
    public static readonly Text ConfigReconnection = new("Reconnection: initial {0} s; maximum {1} s; exponential backoff x2.");
    public static readonly Text ConfigDirectory = new("{0} | {1} | location: {2}");
    public static string OwnOrDefault(bool own) => own ? OwnValue : DefaultValue;
    internal static readonly Text OwnValue = new("own");
    internal static readonly Text DefaultValue = new("default");

    // log retention: origin codes own, global and initial
    public static readonly Text RetentionPositive = new("The retention must be greater than 0 B.");
    public static readonly Text RetentionShow = new("Retention: {0} | origin: {1}.");
    public static string RetentionOrigin(string code) => code switch { "own" => OwnValue, "global" => RetentionGlobal, _ => RetentionInitial };
    internal static readonly Text RetentionGlobal = new("global");
    internal static readonly Text RetentionInitial = new("initial");

    // preset
    public static readonly Text PresetBindingUpdated = new("Preset binding updated by ID; path resolved at run time.");
    public static readonly Text PresetValuesUpdated = new("Own preset values updated.");
    public static readonly Text PresetCreated = new("Preset {0} | {1} | modelId {2}");
    public static readonly Text PresetCloned = new("Preset {0} | {1} | modelId {2}; independent clone.");
    public static readonly Text PresetModelUpdated = new("Preset model updated; own values kept.");
    public static readonly Text PresetRemoved = new("Preset removed; existing references keep its ID.");
    public static readonly Text PresetNone = new("No presets.");
    public static readonly Text PresetRow = new("{0} | {1} | model {2} | modelId {3}");
    public static readonly Text PresetBindingRow = new("  {0} -> {1} | {2} | binding by ID");
    public static readonly Text PresetValueRow = new("  {0} = {1} | {2} | {3}");
    public static readonly Text PresetNoValues = new("  No values.");
    public static readonly Text Missing = new("missing");
    public static string OwnOrInherited(bool own) => own ? OwnValue : Inherited;
    internal static readonly Text Inherited = new("inherited");

    // parameter availability: codes supported, unsupported and unknown
    public static string Availability(string code) => code switch { "supported" => Supported, "unsupported" => Unsupported, _ => AvailabilityUnknown };
    internal static readonly Text Supported = new("supported");
    public static readonly Text AvailabilityUnknown = new("unknown");

    // directories
    public static readonly Text DirectoryTarget = new("Target: {0} | {1}");
    public static readonly Text DirectoryMoveQuestion = new("Move all managed directories? [y] move / [n] keep at origin / [c] cancel");
    internal static readonly Text NoAnswers = new("n,no");
    public static bool IsNo(string? answer) => answer?.Trim() is { Length: > 0 } text &&
        NoAnswers.ToString().Split(',').Contains(text, StringComparer.OrdinalIgnoreCase);
    // The first accepted answer of each kind, for callers that answer on behalf of a person.
    public static string YesAnswer => YesAnswers.ToString().Split(',')[0];
    public static string NoAnswer => NoAnswers.ToString().Split(',')[0];
    public static readonly Text DirectoryCancelled = new("Cancelled; location and files kept.");
    public static readonly Text DirectoryLocationUpdated = new("Location updated; existing installations kept at origin.");
    public static readonly Text DirectoryCollision = new("Collision: {0} -> {1}");
    public static readonly Text DirectoryMoved = new("Location and managed directories updated; identities and references kept.");
    public static readonly Text DirectoryNested = new("The origin and target of an installation cannot contain each other.");
    public static readonly Text DirectoryOverlap = new("The installations have overlapping directories; they were not moved.");
    public static readonly Text DirectoryStructuralCollision = new("Structural collision in {0}; no files were moved.");
    public static readonly Text DirectoryBlockerBackendInstance = new("backend {0}, instance {1}, PID {2}");
    public static readonly Text DirectoryBlockerBackendUncertain = new("uncertain identity: backend {0}, instance {1}");
    public static readonly Text DirectoryBlockerModelInstance = new("models {0}, instance {1}, PID {2}");
    public static readonly Text DirectoryBlockerModelUncertain = new("uncertain identity: models {0}, instance {1}");
    public static readonly Text DirectoryUnknownDownload = new("Download directory not recognized; the move cannot be made safe.");
    public static readonly Text DirectoryBlockerPendingDownload = new("pending download {0}: {1} ({2}/{3})");
    public static readonly Text DirectoryBlockerTargetDownload = new("download in target {0}: {1}");
    public static readonly Text DirectoryBlockerExternal = new("protected external reference {0}: {1}");
    public static readonly Text DirectoryBlockerModel = new("protected model {0}: {1}");
    public static readonly Text DirectoryBlockerBackend = new("protected backend {0}");
    public static readonly Text DirectoryLocationForm = new("The location must be absolute or relative to the home, without a partial root.");
    public static readonly Text DirectoryInvadesHome = new("The target invades another managed area of the home.");
    public static readonly Text DirectoryBackendsHome = new("The backends directory cannot be the whole home.");
    public static readonly Text DirectoryModelsHome = new("The models directory cannot be the whole home.");
    public static readonly Text DirectoryTargetIsFile = new("The target or one of its parents is a file: {0}.");
    public static readonly Text DirectoryStale = new("The catalog, configuration or files changed during the choice; repeat the command to review the new report.");
    public static readonly Text DirectoryUnitsOverlap = new("Overlapping unit files; they were not moved.");
    public static readonly Text DirectoryModelCollision = new("Structural collision in {0}.");
    public static readonly Text DirectoryExternalPaths = new("The move affects registered external paths.");

    // presets and composition (ConfigurationManager)
    public static readonly Text MissingReference = new("Missing reference: {0}.");
    public static readonly Text DefaultBackendAmbiguous = new("Ambiguous default backend selection.");
    public static readonly Text BackendMissing = new("Missing backend: {0}.");
    public static readonly Text DuplicatedAliases = new("Duplicated aliases for {0}; fix the document without losing values.");
    public static readonly Text BindingNeedsText = new("{0}: the binding requires a text parameter of model scope or without definition.");
    public static readonly Text DuplicatedAssignments = new("Duplicated assignments for {0}; literal and binding are exclusive.");
    public static readonly Text PositiveInteger = new("A positive integer is required.");
    public static readonly Text PresetExists = new("Preset already exists: {0}.");
    public static readonly Text ModelNoPresetReferences = new("The model definition does not accept preset references; reset the catalog before assigning a model.");
    public static readonly Text PresetWithoutModel = new("Preset without model assignment.");
    public static readonly Text ModelMissingBroken = new("Missing model: {0}; {1}.");
    public static readonly Text ResourceMissingBroken = new("{0}: resource {1} missing; {2}.");
    public static readonly Text PartialRemovalPending = new("{0}: {1}: partial removal pending; {2}.");
    public static readonly Text ResourceInspectionNotServable = new("{0}: {1}: {2}; {3}.");
    public static readonly Text PresetListRequired = new("An explicit, non-empty list of presets without duplicates is required.");
    public static readonly Text PresetMissingBroken = new("{0}; {1}.");
    public static readonly Text ModelMissingIn = new("Missing model: {0}; broken reference in {1}.");
    public static readonly Text CompanionNotMain = new("A companion cannot be the main model of a preset.");
    public static readonly Text ModelNotOmitted = new("Model {0}: {1}; the preset {2} is not omitted.");
    public static readonly Text BindingUnsupported = new("{0}: {1} {2}; omitted with a warning and the binding is kept.");
    public static readonly Text BindingResourceMissing = new("{0}: {1}: resource {2} missing; broken reference.");
    public static readonly Text BindingNotOmitted = new("{0}: {1}: {2}: {3}; the preset is not omitted.");
    public static readonly Text ValueUnsupported = new("{0}: {1} not supported for {2}/{3}; omitted with a warning and the assignment is kept.");
    public static readonly Text ValueUnknown = new("{0}: {1} unknown for {2}/{3}; kept.");
    public static readonly Text ModelUnsupported = new("{0}: model not supported; omitted with a warning and the reference is kept.");
    public static readonly Text ModelValueUnknown = new("{0}: model unknown for {1}/{2}; kept.");
    public static readonly Text CliValuesDiffer = new("{0}: model CLI values differ between presets; the run cannot be represented without changing their values.");
    public static readonly Text PresetSectionDuplicated = new("Duplicated preset section.");
    public static readonly Text PresetNameNotIni = new("Preset name cannot be represented in INI; * and default are reserved by llama.cpp.");

    // parameter catalog
    public static readonly Text ParameterNameInvalid = new("Invalid parameter name or reserved by the INI.");
    public static readonly Text ParameterServerOwned = new("{0} belongs to the server/router; it is configured in its definition.");
    public static readonly Text ParameterModelByPreset = new("The model is selected with preset create or preset model set, with a stable ID.");
    public static readonly Text ParameterIniLossy = new("The b11482 INI cannot represent control characters, comments (; or #) or outer spaces without loss.");
    public static readonly Text CatalogInvalid = new("Invalid parameter catalog.");
    public static readonly Text DefinitionMetadataInvalid = new("Invalid definition metadata.");
    public static readonly Text VersionCliOnly = new("version only describes a CLI option of the executable.");
    public static readonly Text NameAliasDuplicated = new("Duplicated name/alias: {0}.");
    public static readonly Text AliasInvalid = new("Invalid or duplicated alias: {0}.");
    public static readonly Text NegativeAliasInvalid = new("Invalid or duplicated negative alias: {0}.");
    public static readonly Text NegativeNeedsBoolean = new("Negative forms require a boolean parameter.");
    public static readonly Text EnumInvalid = new("Invalid or empty enumeration.");
    public static readonly Text AssociationDuplicated = new("Duplicated association.");
    public static readonly Text DefinitionProtected = new("Protected definition: {0}.");
    public static readonly Text SupportSelectorInvalid = new("Invalid support selector.");
    public static readonly Text BuildRangeInverted = new("Inverted build range.");
    public static readonly Text InitialEvidenceInvalid = new("Invalid initial evidence.");
    public static readonly Text ValueInvalid = new("Invalid value.");
    public static readonly Text ValueIncompatible = new("{0}: value incompatible with {1}.");
    public static readonly Text DefinitionExists = new("Definition already exists; use param edit.");
    public static readonly Text BindingsIncompatible = new("{0}: existing bindings incompatible or duplicated for {1}.");
    public static readonly Text AssignmentsIncompatible = new("{0}: existing assignments incompatible or duplicated for {1}; fix them before defining the parameter.");
    public static readonly Text OfficialBackendsOnly = new("Only official CPU, CUDA and Vulkan backends are accepted.");
    public static readonly Text FactoryCatalog = new("factory catalog");
    public static readonly Text ResetGlobalDefinitions = new("reset only the definitions of the global catalog");
    public static readonly Text GlobalCatalog = new("global catalog");
    public static readonly Text GlobalCatalogReset = new("Global catalog reset.");
    public static readonly Text CatalogUpdated = new("Catalog and assignments updated.");
    public static readonly Text OperationNeedsConfirmation = new("Cancelled; state kept. Interactive confirmation is required to apply the operation.");
    public static readonly Text StateChangedRecalculating = new("The state changed during the review; the impact is recalculated.");
    public static readonly Text StateKeepsChanging = new("The state keeps changing; review the operation again.");
    public static readonly Text ResetSuppliedDefinitions = new("reset definitions supplied by this version of Coquena");
    public static readonly Text DefinitionMissing = new("Definition does not exist.");
    public static readonly Text CanonicalNameFixed = new("The canonical name is not changed.");
    public static readonly Text EffectBindingRemoved = new("{0} | preset {1} | {2} | {3} -> {4} | saved binding; definition removed/incompatible.");
    public static readonly Text BindingsDuplicatedByAlias = new("Bindings duplicated by alias.");
    public static readonly Text EffectModelRemoved = new("{0} | preset {1} | {2} | model = {3} | saved ModelId reference; definition removed/incompatible.");
    public static readonly Text EffectServerBinding = new("Affected server | {0} | {1} | preset {2} | {3} | {4} -> {5} | the binding is removed; resource kept.");
    public static readonly Text EffectServerValue = new("Affected server | {0} | {1} | preset {2} | {3} | {4} = {5} | the preset assignment is removed; the server association is kept.");
    public static readonly Text EffectServerModel = new("Affected server | {0} | {1} | preset {2} | {3} | model = {4} | preset without model for next runs; resource kept.");
    public static string Emission(string availability) => availability switch { "unsupported" => EmissionOmit, "supported" => EmissionEmit, _ => EmissionUnknown };
    internal static readonly Text EmissionOmit = new("omit with warning");
    internal static readonly Text EmissionEmit = new("emit");
    internal static readonly Text EmissionUnknown = new("unknown availability");
    public static readonly Text EffectKeepPresetBinding = new("{0} | preset {1} | {2} | {3} -> {4} | server {5} | {6} | backend {7}/{8}.");
    public static readonly Text EffectKeepPresetValue = new("{0} | preset {1} | {2} | {3} = {4} | server {5} | {6} | backend {7}/{8}.");
    public static readonly Text EffectKeepPresetModel = new("{0} | preset {1} | {2} | model = {3} | server {4} | {5} | backend {6}/{7}.");
    public static readonly Text EffectKeepServerValue = new("{0} | server {1} | {2} | {3} = {4} | backend {5}/{6}.");
    public static readonly Text Before = new("Before:");
    public static readonly Text DefinitionDeleted = new("definition deleted");
    public static readonly Text EffectRemoveValue = new("{0} | {1} {2} | {3} | {4} = {5} | definition {6}.");
    public static string EntityKind(string kind) => kind == "preset" ? "preset" : EntityServer;
    internal static readonly Text EntityServer = new("server");
    public static string RemovedOrIncompatible(bool removed) => removed ? Removed : Incompatible;
    internal static readonly Text Removed = new("removed");
    internal static readonly Text Incompatible = new("incompatible");
    public static readonly Text AssignmentsDuplicatedByAlias = new("Assignments duplicated by alias; fix the document before editing.");
    public static readonly Text Impact = new("Impact: {0} | {1}");
    public static readonly Text ImpactNone = new("No assignments affected; the existing ones are kept.");
    public static readonly Text ImpactCascade = new("Warning: the operation removes assignments in cascade. Entities, resources and active instances are kept.");
    public static readonly Text ImpactResetConfirmation = new("Confirmation is required to reset the catalog; the assignments are kept.");
    public static readonly Text DefinitionDescription = new("{0} | {1} | {2}/{3} | description: {4} | default: {5} | alias: {6} | values: {7} | support: ");
    public static readonly Text NoDefault = new("no default");
    public static readonly Text GeneralOfficial = new("general official");
    public static readonly Text Open = new("open");
    public static readonly Text InitialEvidence = new(" initial evidence");
    public static readonly Text Recover = new("Recover: {0}");
    public static readonly Text Withdraw = new("Remove: {0}");
    public static readonly Text ResetDefinition = new("Reset: {0}");
    public static readonly Text ResetAction = new("reset");

    // param
    public static readonly Text ParamNegativeForms = new("  negative forms: {0}");
    public static readonly Text ParamUpstreamSyntax = new("  upstream syntax: {0}");
    public static readonly Text ParamSupport = new("  support: {0} | from {1} | until {2}");
    public static readonly Text ParamEvidenceOnly = new(" | initial evidence; other builds unknown");
    public static readonly Text ParamEffectiveAssociation = new("  effective association for the query.");
    public static readonly Text ParamMissingLocal = new("  {0}: {1}; not reassigned by name.");
    public static readonly Text ParamNoDefinitions = new("No definitions.");
    public static readonly Text ParamUnknownLocal = new("{0} | unknown; without a local definition, which does not mean rejection.");
    public static readonly Text ParamNotAlias = new("«{0}» is not an alias of {1}; no changes.");
    public static readonly Text ParamDefined = new("Definition created; no values are assigned.");
    public static readonly Text ParamRemoveSummary = new("remove definition");
    public static readonly Text ParamEditSummary = new("edit {0}");
    public static readonly Text ParamOptionValueMissing = new("Option value missing.");
    public static readonly Text ParamBackendIdInvalid = new("Invalid backend ID.");
    public static readonly Text ParamInteractiveRequired = new("Interactive input is required to confirm the operation; state kept.");
    public static readonly Text ParamApplyQuestion = new("Apply the operation? [y/N]: ");
}
