namespace Coquena;

public static partial class Messages
{
    public static readonly Text LogFollowing = new("Following");
    public static readonly Text WatchProcessDisconnected = new("disconnected: {0}");
    public static readonly Text ParameterHostDefault = new("Coquena default: 127.0.0.1");
    public static readonly Text ParameterPortDefault = new("Coquena default: 9931");
    public static readonly Text ParameterMetricsDefault = new("Coquena default: true");
    public static readonly Text ParameterDefinitionRow = new("{0} | {1} | {2}/{3} | {4} | {5} | alias: {6} | default: {7} | values: {8}");
    public static string ParameterDescription(Configuration.ParameterDefinition definition) => definition.Description.Length > 0
        ? definition.Description : definition.DescriptionKey is { } key ? Localization.Find(key)?.ToString() ?? key : "";
    public static string MatchReason(string code) => code switch
    {
        "declared-repository-quantization-role" => ModelDeclaredRepoAndQuantization,
        "matching-files-sizes-header" => ModelSameFileCountAndPerPartSizes,
        _ => code
    };
    public static readonly Text HealthNoResponse = new("no response");
    public static readonly Text HealthListenerUnverified = new("listener does not belong to the instance group");
    public static readonly Text HealthHttp = new("HTTP {0}");
    public static readonly Text HealthConnectionUnavailable = new("connection unavailable");
    public static readonly Text HealthTimeout = new("query timed out");
    public static MessageData FileReadFailure(Exception error) => FileReadFailed.Data(error.GetType().Name,
        error.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
    public static readonly Text FileReadFailed = new("Could not read file ({0}, 0x{1}).");
}
