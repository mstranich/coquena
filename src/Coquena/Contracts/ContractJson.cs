using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Coquena.Contracts;

public sealed class ContractException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class ContractJson
{
    public static string Write<T>(T value, JsonTypeInfo<T> type) => JsonSerializer.Serialize(value, type);

    public static T Read<T>(string json, JsonTypeInfo<T> type, Action<T> validate)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            Check(root.ValueKind == JsonValueKind.Object, Messages.ValidationExpectedAJSONObject.ToString());
            RejectDuplicates(root);
            if (root.TryGetProperty("schemaVersion", out var version))
            {
                Check(version.ValueKind == JsonValueKind.Number, Messages.ValidationSchemaVersionMustBeAnInteger.ToString());
                Check(version.TryGetInt32(out var number), Messages.ValidationSchemaVersionMustBeAnInteger.ToString());
                if (number != 1)
                    throw new ContractException("unknown-schema", Messages.ValidationUnsupportedSchemaVersion.Format(number));
            }
            var value = JsonSerializer.Deserialize(json, type);
            Check(value is not null, Messages.ValidationNullDocument.ToString());
            validate(value!);
            return value!;
        }
        catch (JsonException error)
        {
            throw new ContractException("invalid-document", Messages.ValidationInvalidJSON.Format(error.Message));
        }
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                Check(names.Add(property.Name), Messages.ValidationDuplicateField.Format(property.Name));
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicates(child);
    }

    internal static void Check(bool condition, string message)
    {
        if (!condition) throw new ContractException("invalid-document", message);
    }
}

// Shape checks only: no network, disk access, backend resolution or referential repair.
public static class ContractValidation
{
    public static void Models(ModelsDocument value)
    {
        Version(value.SchemaVersion);
        Catalog(value.Models, Model, x => x.Id, x => x.Name);
    }
    public static void Backends(BackendsDocument value)
    {
        Version(value.SchemaVersion);
        Catalog(value.Backends, Backend, x => x.Id, null);
        Require(value.Backends.Where(b => b.Name is not null).Select(b => b.Name!).Distinct(StringComparer.OrdinalIgnoreCase).Count() ==
            value.Backends.Count(b => b.Name is not null), Messages.ValidationDuplicateExternalName.ToString());
    }
    public static void Presets(PresetsDocument value)
    {
        Version(value.SchemaVersion);
        Catalog(value.Presets, Preset, x => x.Id, x => x.Name);
    }
    public static void BackendUpdates(BackendUpdatesDocument value)
    {
        Version(value.SchemaVersion);
        Require(value.Updates is not null, Messages.ValidationObservationsRequired.ToString());
        var filters = new HashSet<string>(StringComparer.Ordinal);
        foreach (var update in value.Updates!)
        {
            Require(update is not null, Messages.ValidationNullObservation.ToString());
            Require(update!.Origin == "ggml" && update.Platform == "win-x64" &&
                update.Variant is not null && Coquena.Backends.BackendCatalog.ValidFamily(update.Variant), Messages.ValidationInvalidOfficialFilter.ToString());
            Require(filters.Add($"{update.Origin}/{update.Variant}/{update.Platform}"), Messages.ValidationDuplicateTracking.ToString());
            Id(update.BackendId); OfficialBuild(update.InstalledBuild);
            Require(update.CheckedAt != default && update.CheckedAt.Offset == TimeSpan.Zero, Messages.ValidationUTCDateRequired.ToString());
            Diagnostic(update.Diagnostic);
            Require(update.State is "available" or "unchanged" or "failed", Messages.ValidationInvalidQueryState.ToString());
            Require((update.State == "available") == (update.Candidate is not null), Messages.ValidationCandidateOnlyAllowedForUpdates.ToString());
            if (update.Candidate is not { } candidate) continue;
            OfficialBuild(candidate.Build);
            Require(Coquena.Backends.BackendCatalog.BuildNumber(candidate.Build) >
                Coquena.Backends.BackendCatalog.BuildNumber(update.InstalledBuild), Messages.ValidationCandidateMustBeNewerThanInstalled.ToString());
            Require(candidate.Compatibility is "compatible" or "unknown" or "incompatible", Messages.ValidationInvalidCompatibility.ToString());
            Diagnostic(candidate.Diagnostic);
            Require(candidate.Artifacts is { Length: > 0 and <= 2 }, Messages.ValidationArtifactsRequired.ToString());
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var artifact in candidate.Artifacts!)
            {
                Require(artifact is not null && !string.IsNullOrWhiteSpace(artifact.Path) && artifact.Path.Length <= 256 &&
                    !artifact.Path.Any(c => char.IsControl(c) || c is '/' or '\\') &&
                    artifact.Size is >= 0 && artifact.Identity is null, Messages.ValidationInvalidOfficialArtifact.ToString());
                Require(names.Add(artifact!.Path), Messages.ValidationDuplicateArtifact.ToString());
            }
        }
    }
    private static void OfficialBuild(string build)
    {
        Text(build);
        try { _ = Coquena.Backends.BackendCatalog.BuildNumber(build); }
        catch (Coquena.Backends.BackendException) { Require(false, Messages.ValidationInvalidOfficialBuild.ToString()); }
    }
    private static void Catalog<T>(T[]? entries, Action<T> validate, Func<T, Guid> id, Func<T, string>? name) where T : class
    {
        Require(entries is not null, Messages.ValidationCatalogRequired.ToString());
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries!)
        {
            Require(entry is not null, Messages.ValidationNullRecord.ToString());
            validate(entry!);
            Require(ids.Add(id(entry!)), Messages.ValidationDuplicateID.ToString());
            if (name is not null) Require(names.Add(name(entry!)), Messages.ValidationDuplicateName.ToString());
        }
    }
    private static void Require(bool value, string message) => ContractJson.Check(value, message);
    private static void Id(Guid id) => Require(id != Guid.Empty, Messages.ValidationEmptyID.ToString());
    private static void Text(string? text) => Require(!string.IsNullOrWhiteSpace(text), Messages.ValidationTextRequired.ToString());
    private static void Diagnostic(MessageData? value, int depth = 0)
    {
        Require(value is not null && depth < 8, Messages.ValidationTextRequired.ToString());
        Require(!string.IsNullOrEmpty(value!.Code) && value.Code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'), Messages.ValidationTextRequired.ToString());
        Require(value.Arguments is not null && value.Arguments.All(a => a is not null), Messages.ValidationTextRequired.ToString());
        if (Localization.Find(value.Code) is { } text)
        {
            var markers = Localization.Placeholders(text.English);
            Require(value.Arguments!.Length == (markers.Count == 0 ? 0 : markers.Max() + 1), Messages.LanguageInvalidFormat);
        }
        if (value.Cause is not null) Diagnostic(value.Cause, depth + 1);
    }
    private static void Version(int version) => Require(version == 1, Messages.ValidationSchemaVersionRequired.ToString());
    private static void Ids(Guid[]? ids)
    {
        Require(ids is not null, Messages.ValidationIDListRequired.ToString());
        foreach (var id in ids!) Id(id);
        Require(ids.Distinct().Count() == ids.Length, Messages.ValidationDuplicateIDs.ToString());
    }
    public static void Selection(BackendSelection? selection)
    {
        if (selection is null) return; // server inherits; config may have no default
        if (selection.Kind == "latest")
        {
            Require(selection.BackendId is null, Messages.ValidationLatestDoesNotFixBackendId.ToString());
            Require(selection.Origin == "ggml", Messages.ValidationOnlyTheNormalizedGgmlOriginIsTracked.ToString());
            Text(selection.Variant);
        }
        else
        {
            Require(selection.Kind == "fixed", Messages.ValidationUnknownBackendSelection.ToString());
            Require(selection.BackendId.HasValue, Messages.ValidationFixedRequiresBackendId.ToString());
            Id(selection.BackendId!.Value);
            Require(selection.Origin is null && selection.Variant is null, Messages.ValidationFixedOnlyPreservesBackendId.ToString());
        }
    }
    public static void Servers(ServersDocument value)
    {
        Version(value.SchemaVersion);
        Require(value.Servers is not null, Messages.ValidationServersRequired.ToString());
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ports = new HashSet<int>();
        foreach (var server in value.Servers!)
        {
            Require(server is not null, Messages.ValidationNullServer.ToString());
            Id(server!.Id); Text(server.Name); Selection(server.Backend); Ids(server.PresetIds);
            Require(ids.Add(server.Id) && names.Add(server.Name), Messages.ValidationDuplicateServer.ToString());
            Require(server.LogRetention is null || server.LogRetention.MaxBytes > 0, Messages.ValidationInvalidLogRetention.ToString());
            Require(server.Parameters is not null, Messages.ValidationParametersRequired.ToString());
            Require(server.Parameters!.TryGetValue("port", out var port) &&
                port.ValueKind == JsonValueKind.Number && port.TryGetInt32(out var number) &&
                number is >= 1 and <= 65535 && ports.Add(number), Messages.ValidationPortMissingOutOfRangeOrDuplicated.ToString());
            if (server.Parameters.TryGetValue("host", out var host))
                Require(host.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(host.GetString()), Messages.ValidationInvalidHost.ToString());
            if (server.Parameters.TryGetValue("metrics", out var metrics))
                Require(metrics.ValueKind is JsonValueKind.True or JsonValueKind.False, Messages.ValidationMetricsMustBeBoolean.ToString());
            foreach (var pair in server.Parameters)
                Require(pair.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or
                    JsonValueKind.True or JsonValueKind.False, Messages.ValidationParameterMustBeAScalarLiteral.ToString());
        }
    }
    // cpu, ram or gpuN.use / gpuN.memory / gpuN.shared, as numbered in the TUI bar.
    internal static bool BarResource(string? key) => key is "cpu" or "ram" ||
        key is { Length: > 3 } && key.StartsWith("gpu", StringComparison.Ordinal) && key.IndexOf('.') is > 3 and var dot &&
        key[3..dot].All(char.IsAsciiDigit) && dot - 3 <= 2 && key[(dot + 1)..] is "use" or "memory" or "shared";
    public static void Config(ConfigDocument value)
    {
        Version(value.SchemaVersion);
        Require(value.Observer is null || value.Observer.RetryInitialSeconds >= 1 && value.Observer.RetryMaxSeconds >= value.Observer.RetryInitialSeconds && value.Observer.RetryMaxSeconds <= 3600, Messages.ValidationInvalidReconnectionInitialMaximumSeconds.ToString());
        if (value.BackendsDirectory is not null) Text(value.BackendsDirectory);
        if (value.ModelsDirectory is not null) Text(value.ModelsDirectory);
        Require(value.LogRetention is null || value.LogRetention.MaxBytes > 0, Messages.ValidationInvalidLogRetention.ToString());
        Require(value.HiddenBarResources is null || value.HiddenBarResources.Length <= 64 &&
            value.HiddenBarResources.Distinct(StringComparer.Ordinal).Count() == value.HiddenBarResources.Length &&
            value.HiddenBarResources.All(BarResource), Messages.ValidationInvalidHiddenBarResources.ToString());
        Require(value.Language is null || Localization.ValidCode(value.Language), Messages.ValidationInvalidLanguage.ToString());
        if (value.DefaultServerId.HasValue) Id(value.DefaultServerId.Value);
        Selection(value.DefaultBackend);
        Require(value.BackendRetention is not null && value.BackendRetention.MaxBuilds >= 1, Messages.ValidationInvalidRetention.ToString());
    }
    public static void Backend(BackendRecord value)
    {
        Id(value.Id); Text(value.Build); Text(value.Version); Text(value.Origin);
        if (value.InstallationDirectory is not null) Text(value.InstallationDirectory);
        Text(value.Repository); Text(value.Variant); Text(value.Family); Text(value.Platform); Text(value.ServerPath);
        Require(value.Ownership is "managed" or "external", Messages.ValidationInvalidBackendOwnership.ToString());
        Require(!value.RemovalPending || value.Ownership == "managed", Messages.ValidationPhysicalRemovalOnlyAllowedForManagedResources.ToString());
        if (value.OwnedFiles is { } owned)
        {
            Require(value.Ownership == "managed" && owned.Length > 0 && owned.Distinct(StringComparer.OrdinalIgnoreCase).Count() == owned.Length, Messages.ValidationInvalidBackendInventory.ToString());
            foreach (var file in owned)
                Require(!string.IsNullOrWhiteSpace(file) && !Path.IsPathRooted(file) && !file.Contains('\\') &&
                    !file.Contains(':') && !file.Any(char.IsControl) && file.Split('/').All(p => p.Length > 0 && p is not ("." or "..")), Messages.ValidationInvalidInventoryPath.ToString());
        }
        if (value.Ownership == "external")
        {
            Require(Coquena.Backends.BackendInstaller.ValidExternalName(value.Name), Messages.ValidationInvalidExternalName.ToString());
            Require(Coquena.Backends.BackendInstaller.ValidExternalFamily(value.Variant) && value.Family == value.Variant.Split('@')[0], Messages.ValidationInvalidExternalFamily.ToString());
            Require(Path.IsPathFullyQualified(value.ServerPath) && value.InstallationDirectory is null && !value.TracksLatest && !value.Pinned,
                Messages.ValidationExternalResourcesMustKeepAnAbsolutePath.ToString());
        }
        else Require(value.Name is null, Messages.ValidationNameReservedForExternalResources.ToString());
    }
    public static void Remote(RemoteSelection value)
    {
        Require(value is not null, Messages.ValidationRemoteSelectionRequired.ToString());
        Text(value!.Repository); Text(value.RequestedRevision); Text(value.Commit);
        Require(value.Files is { Length: > 0 }, Messages.ValidationSelectionWithoutFiles.ToString());
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in value.Files!)
        {
            Require(file is not null, Messages.ValidationNullFile.ToString()); Text(file!.Path);
            Require(paths.Add(file.Path), Messages.ValidationDuplicatePath.ToString());
            Require(file.Size is null or >= 0, Messages.ValidationNegativeSize.ToString());
            Identity(file.Identity);
        }
    }
    private static void Identity(ContentIdentity? value)
    {
        if (value is null) return;
        Text(value.Algorithm); Text(value.Domain); Text(value.Value);
    }
    public static void Model(ModelRecord value)
    {
        Id(value.Id); Text(value.Name);
        Require(value.Ownership is "managed" or "external", Messages.ValidationInvalidModelOwnership.ToString());
        Require(!value.RemovalPending || value.Ownership == "managed", Messages.ValidationPhysicalRemovalOnlyAllowedForManagedResources.ToString());
        Require(value.CompanionType is null or "mmproj" or "mtp", Messages.ValidationInvalidCompanionType.ToString());
        if (value.Ownership == "managed") Remote(value.Selection!);
        else Require(value.Selection is null && value.InstallationDirectory is null, Messages.ValidationExternalResourceWithManagedInstallationOrTracking.ToString());
        if (value.InstallationDirectory is not null) Text(value.InstallationDirectory);
        Require(value.Files is { Length: > 0 } && (value.Selection is null || value.Files.Length == value.Selection.Files.Length), Messages.ValidationIncompleteUnit.ToString());
        for (var i = 0; i < value.Files!.Length; i++)
        {
            var file = value.Files[i];
            Require(file is not null, Messages.ValidationNullInstalledFile.ToString());
            Require(value.Selection is null ? file!.RemotePath == "" && Path.IsPathFullyQualified(file.LocalPath)
                : file!.RemotePath == value.Selection.Files[i].Path, Messages.ValidationInvalidUnitOrderOrCorrespondence.ToString());
            Text(file.LocalPath); Require(file.Size >= 0, Messages.ValidationNegativeSize.ToString()); Identity(file.Integrity);
        }
        if (value.InformativeMatch is { } match)
        {
            Require(value.Ownership == "external" && match.SelectedAt != default && match.Reasons is { Length: > 0 }, Messages.ValidationInvalidInformativeEquivalence.ToString());
            Remote(match.Candidate);
            foreach (var reason in match.Reasons!) Text(reason);
        }

        if (value.Update is { } update)
        {
            Require(value.Ownership == "managed" && update.LastAttempt != default, Messages.ValidationQueryOnlyAllowedForManagedResources.ToString());
            Require(update.State is "unchanged" or "update-available" or "intervention-required" or "query-failed" or "orphaned", Messages.ValidationInvalidQueryState.ToString());
            Diagnostic(update.Diagnostic);
            Require(update.LastSuccess is null || update.LastSuccess != default, Messages.ValidationInvalidSuccessDate.ToString());
            Require(update.LastSuccess is null ? update.LastSuccessfulState is null :
                update.LastSuccessfulState is "unchanged" or "update-available" or "intervention-required" or "orphaned", Messages.ValidationInvalidPreviousObservation.ToString());
            if (update.LastSuccessfulDiagnostic is not null) Diagnostic(update.LastSuccessfulDiagnostic);
            void Pinned(RemoteSelection selection)
            {
                Remote(selection);
                Coquena.Models.HuggingFaceStore.ValidateRepository(selection.Repository);
                Require(Coquena.Models.HuggingFaceStore.Hex(selection.Commit, 40), Messages.ValidationSelectionWithoutFixedCommit.ToString());
                foreach (var file in selection.Files) Coquena.Models.HuggingFaceStore.ValidatePath(file.Path);
            }
            if (update.Followed is { } followed) Pinned(followed);
            if (update.FollowedName is { } followedName)
            {
                var tracking = Coquena.Models.ModelRequest.Parse(followedName);
                Require(update.Followed is not null && tracking.Repository == update.Followed.Repository &&
                    tracking.CompanionType == value.CompanionType && tracking.Quantization is not null, Messages.ValidationInvalidTrackedName.ToString());
            }
            if (update.Plan is { } plan)
            {
                Pinned(plan.Installed); Pinned(plan.Target); Text(plan.TargetName);
                var request = Coquena.Models.ModelRequest.Parse(plan.TargetName);
                Require(request.Repository == plan.Target.Repository && request.CompanionType == value.CompanionType && request.Quantization is not null,
                    Messages.ValidationTargetNameIncompatibleWithUnit.ToString());
                Require(ContractJson.Write(plan.Installed, ContractJsonContext.Default.RemoteSelection) ==
                    ContractJson.Write(value.Selection!, ContractJsonContext.Default.RemoteSelection), Messages.ValidationPlanBelongsToAnotherInstallation.ToString());
                Require(plan.Actions is not null && plan.Actions.Length == plan.Target.Files.Length, Messages.ValidationIncompletePlan.ToString());
                for (var i = 0; i < plan.Actions!.Length; i++)
                {
                    var action = plan.Actions[i];
                    Require(action is not null && action.TargetPath == plan.Target.Files[i].Path, Messages.ValidationActionWithoutCorrespondence.ToString());
                    Coquena.Models.HuggingFaceStore.ValidatePath(action!.TargetPath);
                    Require(action.Action is "reuse" or "download", Messages.ValidationInvalidAction.ToString());
                    Require(action.Action == "download" ? action.SourcePath is null :
                        plan.Installed.Files.Any(f => f.Path == action.SourcePath && Coquena.Models.ModelUpdateComparer.Same(f.Identity, plan.Target.Files[i].Identity)), Messages.ValidationReuseWithoutExactIdentity.ToString());
                }
            }
        }
        Parameters(value.Parameters);
        if (value.IntegrityCheck is { } check)
        {
            Require(check.CheckedAt != default && check.Files is not null && check.Files.Length == value.Files.Length,
                Messages.ValidationIncompleteIntegrityObservation.ToString());
            Require(!check.SetBaseline || value.Ownership == "external", Messages.ValidationLocalBaselineOnlyAllowedForExternalResources.ToString());
            Require(check.Status is "verified" or "baseline-set" or "no-reference" or "mismatch" or "read-error" or "missing-file" or "changed-during-read",
                Messages.ValidationInvalidIntegrityResult.ToString());
            for (var i = 0; i < check.Files!.Length; i++)
            {
                var file = check.Files[i];
                Require(file is not null, Messages.ValidationNullFileResult.ToString());
                Text(file!.LocalPath);
                if (file.Diagnostic is not null) Diagnostic(file.Diagnostic);
                Require(file!.Status is "verified" or "baseline-set" or "no-reference" or "mismatch" or "read-error" or "missing-file" or "changed-during-read",
                    Messages.ValidationInvalidPerFileResult.ToString());
                Require(file.Observed is null || Coquena.Models.FileIntegrity.Comparable(file.Observed), Messages.ValidationInvalidObservedHash.ToString());
                Require(file.Status is not ("verified" or "baseline-set" or "no-reference" or "mismatch") || file.Observed is not null,
                    Messages.ValidationResultWithoutCompleteReading.ToString());
            }
        }
    }
    private static void Parameters(Dictionary<string, string>? values)
    {
        Require(values is not null, Messages.ValidationInitialConfigurationRequired.ToString());
        foreach (var pair in values!) { Text(pair.Key); Require(pair.Value is not null, Messages.ValidationNullLiteral.ToString()); }
    }
    public static void Preset(PresetRecord value)
    {
        Id(value.Id); Text(value.Name); if (value.ModelId is { } model) Id(model); Parameters(value.Parameters);
        foreach (var binding in value.Bindings ?? [])
        {
            Text(binding.Key); Id(binding.Value);
            Require(binding.Key != "model" && !value.Parameters.ContainsKey(binding.Key), Messages.ValidationDuplicateLiteralReferenceAssignmentOrReservedModel.ToString());
        }
    }
    public static void Instance(InstanceDocument value)
    {
        Version(value.SchemaVersion); Id(value.InstanceId); Id(value.ServerId); Id(value.BackendId);
        Text(value.Build); Text(value.Host); Text(value.IniPath); Text(value.LogPath);
        Require(value.Pid > 0 && value.Port is >= 1 and <= 65535, Messages.ValidationInvalidPIDOrPort.ToString());
        Require(value.Mode is "foreground" or "detached", Messages.ValidationUnknownMode.ToString());
        Ids(value.PresetIds); Ids(value.ResourceIds);
        Require(value.PresetIds.Length > 0 && value.ResourceIds.Length > 0, Messages.ValidationExecutionWithoutResourcesOrPresets.ToString());
        Require(value.ProcessCreated is null || value.ProcessCreated > 0, Messages.ValidationInvalidProcessCreationDate.ToString());
        Require(value.ExecutablePath is null || Path.IsPathFullyQualified(value.ExecutablePath), Messages.ValidationExecutablePathIsNotAbsolute.ToString());
    }
    public static void Operation(OperationDocument value)
    {
        Version(value.SchemaVersion); Id(value.Id); Text(value.Name); Text(value.Phase);
        Require(value.CreatedAt is null || value.CreatedAt.Value != default, Messages.ValidationInvalidCreationDate.ToString());
        if (value.LastIncident is { } incident)
        {
            Text(incident.Reason);
            Require(incident.FileIndex >= 0 && incident.FileIndex < value.Files.Length && incident.Offset >= 0 &&
                incident.ConsecutiveFailures > 0 && incident.RetryLimit > 0 && incident.WaitMilliseconds >= 0 && incident.At != default,
                Messages.ValidationInvalidTransferIncident.ToString());
        }
        if (value.ScopePublication is { } scoped)
        {
            Id(scoped.ResourceId); Id(scoped.InstallationId);
            Require(value.ModelInstall is null && value.BackendInstall is null &&
                scoped.Origin is "explicit" or "automatic" && scoped.RequiredFiles is not null,
                Messages.ValidationInvalidScopedPublicationPlan.ToString());
            foreach (var required in scoped.RequiredFiles!) Text(required);
            if (scoped.ExternalPrevious is { } external)
                Require(value.ResourceKind is "model" or "companion" && scoped.Origin == "explicit" &&
                    external["ownership"]?.GetValue<string>() == "external" && external["resource-id"]?.GetValue<string>() == scoped.ResourceId.ToString("D") &&
                    external["logical-key"]?.GetValue<string>() == value.Name && external["kind"]?.GetValue<string>() == value.ResourceKind &&
                    external["installations"] is System.Text.Json.Nodes.JsonArray { Count: > 0 }, Messages.ValidationInvalidExternalTransitionSource.ToString());
            Require(value.ResourceKind != "backend" || scoped.RequiredFiles!.Any(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)),
                Messages.ValidationBackendRequiresADeclaredExecutable.ToString());
        }
        if (value.Executor is { } executor)
        {
            Require(value.ScopePublication is not null && executor.Pid > 0 && executor.StartedAtUtcTicks > 0,
                Messages.ValidationInvalidExecutorIdentity.ToString());
            Text(executor.Executable);
        }
        Require(value.Files is { Length: > 0 } && value.Files.All(f => f is not null), Messages.ValidationTransfersRequiredAndNonNull.ToString());
        if (value.ModelInstall is { } model)
        {
            Require((value.ResourceKind is "model" or "companion") && value.ModelSelection is not null && value.BackendInstall is null, Messages.ValidationModelPlanMustBeExclusive.ToString());
            var request = Coquena.Models.ModelRequest.Parse(value.Name);
            Require(request.Quantization is not null && request.Repository == value.ModelSelection!.Repository &&
                (value.ResourceKind == "companion") == (request.CompanionType is not null), Messages.ValidationUnitNameAndCategoryDoNotMatch.ToString());
            Id(model.ModelId);
            if (model.ExternalPrevious is { } external)
            {
                Model(external);
                Require(model.Previous is null && model.RollbackFiles is null && external.Ownership == "external" &&
                    external.Id == model.ModelId && external.Name.Equals(value.Name, StringComparison.OrdinalIgnoreCase) &&
                    external.CompanionType == request.CompanionType, Messages.ValidationIncompatibleExternalTransition.ToString());
            }
            if (model.Previous is { } previousModel)
            {
                Model(previousModel);
                Require(previousModel.Id == model.ModelId && previousModel.Ownership == "managed" &&
                    previousModel.Update is { State: "update-available", Plan: not null } &&
                    ContractJson.Write(previousModel.Update.Plan.Target, ContractJsonContext.Default.RemoteSelection) ==
                        ContractJson.Write(value.ModelSelection!, ContractJsonContext.Default.RemoteSelection) &&
                    previousModel.Update.Plan.TargetName == value.Name &&
                    previousModel.InstallationDirectory == model.InstallationDirectory,
                    Messages.ValidationUpgradeDoesNotMatchTheResolvedPlan.ToString());
                if (model.RollbackFiles is { } rollback)
                {
                    Require(rollback.Length == previousModel.Files.Length, Messages.ValidationBackupWithoutCompletePreviousUnit.ToString());
                    for (var i = 0; i < rollback.Length; i++)
                        Require(rollback[i] is not null && rollback[i].RemotePath == previousModel.Files[i].RemotePath &&
                            rollback[i].LocalPath == previousModel.Files[i].LocalPath && rollback[i].Size >= 0 &&
                            Coquena.Models.FileIntegrity.Comparable(rollback[i].Integrity) && rollback[i].LastWriteUtc is not null,
                            Messages.ValidationInvalidBackupReference.ToString());
                }
                if (value.Phase == "publishing" || value.Phase == "cleanup" && value.State != "cancelled")
                    Require(model.RollbackFiles is not null, Messages.ValidationPublishedUpgradeWithoutPreviousBackup.ToString());
            }
            else Require(model.RollbackFiles is null, Messages.ValidationBackupOnlyAllowedForUpgrade.ToString());
            if (model.InstallationDirectory is not null) Text(model.InstallationDirectory);
            Require(model.MovedFiles >= 0 && model.MovedFiles <= value.Files.Length &&
                (model.MovingFile == -1 || model.MovingFile == model.MovedFiles && model.MovingFile < value.Files.Length), Messages.ValidationInvalidPublicationProgress.ToString());
            if (model.VerifiedFiles is { } verified)
            {
                Require(verified.Length == value.Files.Length, Messages.ValidationPublicationWithoutCompleteUnit.ToString());
                for (var i = 0; i < verified.Length; i++)
                {
                    Require(verified[i] is not null, Messages.ValidationNullVerifiedFile.ToString());
                    Require(verified[i].RemotePath == value.Files[i].SourcePath && verified[i].LocalPath == value.Files[i].DestinationPath &&
                        verified[i].Size == value.Files[i].ConfirmedBytes && Coquena.Models.FileIntegrity.Comparable(verified[i].Integrity) &&
                        verified[i].Integrity == value.Files[i].LocalIntegrity && verified[i].LastWriteUtc is not null, Messages.ValidationInvalidVerifiedFile.ToString());
                }
            }
            if (value.Phase == "publishing" || value.Phase == "cleanup" && value.State != "cancelled")
                Require(model.VerifiedFiles is not null, Messages.ValidationPublicationWithoutVerifiedReferences.ToString());
        }
        if (value.BackendInstall is { } plan)
        {
            if (plan.InstallationDirectory is not null) Text(plan.InstallationDirectory);
            Require(value.ResourceKind == "backend" && value.BackendSelection is not null, Messages.ValidationPlanOnlyAllowedForBackend.ToString());
            Id(plan.BackendId); Text(plan.RequestedRelease);
            Require(plan.RequestedRelease == "latest" || plan.RequestedRelease == value.BackendSelection!.Release,
                Messages.ValidationIntentionAndFixedReleaseDoNotMatch.ToString());
            Require(plan.Slot == $"backends/{plan.BackendId:D}/{value.Id:D}", Messages.ValidationSlotDoesNotBelongToTheOperation.ToString());
            if (plan.Previous is { } previous)
            {
                Backend(previous);
                Require(previous.Id == plan.BackendId && previous.Build == value.BackendSelection!.Release &&
                    previous.Origin == "ggml" && previous.Variant == value.BackendSelection.Variant &&
                    previous.Platform == value.BackendSelection.Platform, Messages.ValidationPreviousRecordDoesNotMatch.ToString());
            }
            if (plan.PreviousLatest is { } latest)
            {
                Backend(latest);
                Require(latest.TracksLatest && latest.Origin == "ggml" && latest.Variant == value.BackendSelection!.Variant &&
                    latest.Platform == value.BackendSelection.Platform, Messages.ValidationPreviousTrackingDoesNotMatch.ToString());
            }
            if (plan.Upgrade is { } upgrade)
            {
                BackendUpdates(new(1, [upgrade]));
                Require(plan.RequestedRelease == "latest" && plan.Previous is null && plan.PreviousLatest is { } &&
                    upgrade.BackendId == plan.PreviousLatest.Id && upgrade.InstalledBuild == plan.PreviousLatest.Build &&
                    upgrade.Origin == value.BackendSelection!.Origin && upgrade.Variant == value.BackendSelection.Variant &&
                    upgrade.Platform == value.BackendSelection.Platform && upgrade.State == "available" &&
                    upgrade.Candidate?.Build == value.BackendSelection.Release &&
                    upgrade.Candidate.Artifacts.SequenceEqual(value.BackendSelection.Artifacts), Messages.ValidationUpgradePlanAndObservationDoNotMatch.ToString());
            }
            if (value.Phase == "publishing" || value.Phase == "cleanup" && value.State != "cancelled")
            {
                Text(plan.ServerRelativePath); Text(plan.Version);
                Require(plan.OwnedFiles is { Length: > 0 } && plan.OwnedFiles.Contains(plan.ServerRelativePath!), Messages.ValidationInstallationInventoryRequiredBeforePublication.ToString());
                Backend(new(plan.BackendId, value.BackendSelection!.Release, plan.Version!, "ggml", "ggml-org/llama.cpp",
                    value.BackendSelection.Variant, value.BackendSelection.Family, value.BackendSelection.Platform, plan.Slot + "/" + plan.ServerRelativePath, OwnedFiles: plan.OwnedFiles));
            }
        }
        Require(value.State is "pending" or "downloading" or "paused" or "verifying" or "failed" or
            "completed" or "cancelled", Messages.ValidationUnknownState.ToString());
        if ((value.ResourceKind is "model" or "companion"))
        {
            Require(value.ModelSelection is not null && value.BackendSelection is null, Messages.ValidationModelSelectionMustBeExclusive.ToString());
            Remote(value.ModelSelection!);
        }
        else
        {
            Require(value.ResourceKind == "backend" && value.ModelSelection is null && value.BackendSelection is not null,
                Messages.ValidationBackendSelectionMustBeExclusive.ToString());
            var selection = value.BackendSelection!;
            Require(selection.Origin == "ggml", Messages.ValidationSourceCannotBeDownloaded.ToString());
            Text(selection.Release); Require(selection.Release != "latest", Messages.ValidationOperationMustFixRelease.ToString());
            Text(selection.Family); Text(selection.Variant); Text(selection.Platform);
            Require(selection.Artifacts is { Length: > 0 }, Messages.ValidationBackendWithoutArtifacts.ToString());
            foreach (var artifact in selection.Artifacts!)
                Require(artifact is not null && (artifact.Identity is null || value.ScopePublication is not null) && (artifact.Size is null or >= 0),
                    Messages.ValidationBackendStoresNeitherDigestNorNegativeSize.ToString());
        }
        var expected = value.ModelSelection?.Files ?? value.BackendSelection!.Artifacts;
        Require(value.Files is not null && value.Files.Length == expected.Length, Messages.ValidationIncompleteTransfers.ToString());
        for (var i = 0; i < value.Files!.Length; i++)
        {
            var file = value.Files[i];
            Require(file is not null, Messages.ValidationNullTransfer.ToString());
            Require(file!.SourcePath == expected[i].Path && file.ExpectedSize == expected[i].Size,
                Messages.ValidationTransferDoesNotMatchFixedSelection.ToString());
            Text(file.PartialPath); Text(file.DestinationPath); Text(file.State);
            Require(file.ConfirmedBytes >= 0 && (file.ExpectedSize is null || file.ConfirmedBytes <= file.ExpectedSize), Messages.ValidationInvalidCheckpoint.ToString());
            Require(value.ResourceKind != "backend" || value.ScopePublication is not null || file.Identity is null, Messages.ValidationBackendDoesNotStoreDigest.ToString());
            Require(value.ResourceKind != "backend" || value.ScopePublication is not null || file.LocalIntegrity is null, Messages.ValidationBackendDoesNotCalculateLocalDigest.ToString());
            Identity(file.Identity);
            Identity(file.LocalIntegrity);
            Identity(file.CheckpointIntegrity);
            if (value.ModelInstall is not null || value.ScopePublication is not null)
            {
                Require(file.Identity == expected[i].Identity, Messages.ValidationRemoteReferenceDiffersFromFixedSelection.ToString());
                Require(file.State is "pending" or "paused" or "downloading" or "failed" or "transferred", Messages.ValidationUnknownTransferState.ToString());
                if (file.State == "transferred")
                    Require(Coquena.Models.FileIntegrity.Comparable(file.LocalIntegrity) && file.VerifiedWriteUtc is not null,
                        Messages.ValidationModelTransferWithoutCompleteSHAOrObserved.ToString());
            }
        }
    }
}
