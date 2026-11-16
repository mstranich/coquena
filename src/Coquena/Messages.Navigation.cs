namespace Coquena;

public static partial class Messages
{
    public static string ViewLabel(string code) => code switch
    {
        "recipe" => NavigationRecipe, "recipes" => NavigationRecipes,
        "backends" => NavigationBackends, "service" => NavigationService,
        "services" => NavigationServices, "models" => NavigationModels,
        "parameters" => NavigationParameters, "presets" => NavigationPresets,
        "updates" => NavigationUpdates, "maintenance" => NavigationMaintenance,
        _ => code
    };
    public static string VerbLabel(string code) => code switch
    {
        "start" => NavigationStart, "stop" => NavigationStop, "restart" => NavigationRestart, _ => code
    };
    internal static readonly Text NavigationRecipe = new("Recipe");
    internal static readonly Text NavigationRecipes = new("Recipes");
    internal static readonly Text NavigationBackends = new("Backends");
    internal static readonly Text NavigationService = new("Service");
    internal static readonly Text NavigationServices = new("Services");
    internal static readonly Text NavigationModels = new("Models");
    internal static readonly Text NavigationParameters = new("Parameters");
    internal static readonly Text NavigationPresets = new("Presets");
    internal static readonly Text NavigationUpdates = new("Updates");
    internal static readonly Text NavigationMaintenance = new("Maintenance");
    internal static readonly Text NavigationStart = new("Start");
    internal static readonly Text NavigationStop = new("Stop");
    internal static readonly Text NavigationRestart = new("Restart");
}
