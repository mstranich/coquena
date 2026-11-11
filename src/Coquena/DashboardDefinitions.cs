using System.Text.Json.Nodes;
using Coquena.Configuration;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Observation;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Storage;
using View = Coquena.Observation.DefinitionsPresentation;

namespace Coquena;

internal static partial class DashboardCli
{
    // Read-only queries behind the Definiciones tab. Each view uses the same reads as the
    // matching CLI list command; a failed read shows its message and nothing is written.
    internal sealed class DefinitionsSource(Home home, string directory)
    {
        internal sealed record Place(View.Scope Scope, ScopeContext? Context, RecipeSelection? Recipe, string? Folder = null);

        // Global, the folder where the TUI was opened and every project of the index.
        public List<Place> Places()
        {
            var places = new List<Place>();
            places.Add(new(new("Global", true, false, true, true), ScopeContext.Global(home), null));
            var here = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            places.Add(Project(here, true));
            string[] indexed = [];
            try { indexed = new ProjectIndex(home).Locations(); }
            catch (Exception error) when (ServiceObservation.ObservationError(error)) { }
            foreach (var location in indexed.Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
                         .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                if (!location.Equals(here, StringComparison.OrdinalIgnoreCase)) places.Add(Project(location, false));
            return places;
        }

        private static Place Project(string folder, bool here)
        {
            var file = Path.Combine(folder, "coquena.yaml");
            var name = Path.GetFileName(folder);
            if (!Directory.Exists(folder)) return new(new(name, false, here, false, false), null, null);
            if (!File.Exists(file)) return new(new(name, false, here, true, false), null, null, folder);
            RecipeSelection? selection = null;
            try
            {
                selection = new RecipeSelection(folder);
                name = RecipeReader.Read(selection).Name;
            }
            catch (Exception error) when (ServiceObservation.ObservationError(error) || error is RecipeException) { }
            try { return new(new(name, false, here, true, true), ScopeContext.Project(file), selection); }
            catch (Exception error) when (ServiceObservation.ObservationError(error)) { return new(new(name, false, here, false, true), null, null); }
        }

        public View.Page Read(Place place, string view, IReadOnlyList<Target> targets,
            IReadOnlyDictionary<int, ServiceSnapshot> snapshots, int rows, int width)
        {
            var scope = place.Scope;
            if (!scope.Accessible) return View.Inaccessible(scope.Name);
            if (view == "Receta") return View.NoRecipe(scope.Name);
            var context = place.Context!;
            try
            {
                return view switch
                {
                    "Recetas" => Recipes(place),
                    "Presets" => Presets(place, targets, snapshots),
                    "Servicios" or "Servicio" => Services(place, targets, snapshots),
                    "Modelos" => View.Models(scope.Name, Models(context)),
                    "Backends" => View.Backends(scope.Name, Backends(context)),
                    "Parámetros" => View.Parameters(scope.Name, new ParameterManager(home).List()
                        .Select(item => new View.Parameter(item.Name, item.Type, item.Application, item.Default)).ToArray(), rows),
                    "Actualizaciones" => Updates(place),
                    _ => View.Maintenance(scope.Name)
                };
            }
            catch (Exception error) when (ServiceObservation.ObservationError(error) || error is RecipeException or ConfigurationException)
            {
                return View.Problem(view + " de " + scope.Name, error.Message);
            }
        }

        private static View.Page Recipes(Place place)
        {
            var current = place.Recipe is null ? null : CombinationIdentity.FromSelection(place.Recipe).Id;
            var combinations = new CombinationPreparation(place.Context!).List().Select(item =>
            {
                var files = item.Value!["files"]!.AsArray().Select(file => Path.GetFileName(file!.GetValue<string>()))
                    .Concat(item.Value["env-files"]?.AsArray().Select(file => Path.GetFileName(file!.GetValue<string>())) ?? []);
                return new View.Combination(string.Join(" + ", files), item.Key[..Math.Min(6, item.Key.Length)],
                    item.Value["private"]?.GetValue<bool>() == true, item.Key == current);
            }).ToArray();
            string? destination = null;
            if (place.Recipe is not null)
            {
                try { destination = RecipeReader.Read(place.Recipe).Target; }
                catch (RecipeException) { }
            }
            return View.Recipes(place.Scope.Name, destination, combinations,
                place.Recipe?.Files.Select(Path.GetFileName).OfType<string>().ToArray() ?? []);
        }

        private static View.Page Presets(Place place, IReadOnlyList<Target> targets, IReadOnlyDictionary<int, ServiceSnapshot> snapshots)
        {
            // A project shows its recipe; Global, the recipe of each global service.
            var recipes = place.Recipe is not null ? [RecipeReader.Read(place.Recipe)]
                : new RecipeRegistry(new ScopeStore(place.Context!)).Services()
                    .Select(service =>
                    {
                        // One unreadable service recipe leaves out its presets, not the others.
                        try { return RecipeReader.Read(new RecipeImporter(place.Context!).ServiceSelection(service.ServiceId)); }
                        catch (Exception error) when (ServiceObservation.ObservationError(error) || error is RecipeException) { return null; }
                    }).OfType<EffectiveRecipe>().ToArray();
            var observed = snapshots.Values.SelectMany(snapshot => snapshot.Entries).ToArray();
            var presets = recipes.SelectMany(recipe => (recipe.Root.Get("presets")?.Fields ?? new Dictionary<string, RecipeValue>())
                .Where(item => item.Value.Kind != RecipeKind.Null).Select(item =>
                {
                    var model = item.Value.Get("args")?.Get("model")?.Text ?? "—";
                    if (model.StartsWith("model://", StringComparison.Ordinal)) model = model["model://".Length..];
                    var name = recipe.Name + "/" + item.Key;
                    var live = observed.FirstOrDefault(entry => entry.Name.Equals(name, StringComparison.Ordinal) ||
                        place.Scope.Global && entry.Name.Equals(item.Key, StringComparison.Ordinal))?.State;
                    return new View.Preset(item.Key, model, live);
                })).ToArray();
            return View.Presets(place.Scope.Name, presets);
        }

        private static View.Page Services(Place place, IReadOnlyList<Target> targets, IReadOnlyDictionary<int, ServiceSnapshot> snapshots)
        {
            var context = place.Context!;
            var store = new ScopeStore(context);
            var defaultId = context.IsGlobal ? store.Read().State?["data"]?["aliases"]?["default"]?.GetValue<string>() : null;
            var services = new RecipeRegistry(store).Services().Select(service =>
            {
                var index = targets.ToList().FindIndex(target => target.Service?.ServiceId == service.ServiceId &&
                    target.Store.Context.Base.Equals(context.Base, StringComparison.OrdinalIgnoreCase));
                string? state = null, @class = null;
                if (index >= 0 && snapshots.TryGetValue(index, out var snapshot))
                {
                    var (glyph, tone, text) = LivePresentation.ServiceState(snapshot);
                    (state, @class) = (glyph + " " + text, tone);
                }
                return new View.Service(service.Name, service.ServiceId == defaultId, state, @class);
            }).ToArray();
            return View.Services(place.Scope.Name, place.Scope.Global, services);
        }

        private static View.Model[] Models(ScopeContext context)
        {
            var models = new ScopedModels(context);
            return models.List().Select(item => Model(item!, false)).Concat(models.List(true).Select(item => Model(item!, true))).ToArray();

            static View.Model Model(JsonNode resource, bool companion)
            {
                var installations = resource["installations"]!.AsArray();
                return new(resource["identity"]?.GetValue<string>() ?? resource["logical-key"]!.GetValue<string>(), companion,
                    resource["ownership"]?.GetValue<string>() == "external", Bytes(installations),
                    installations.Any(installation => installation?["protected"]?.GetValue<bool>() == true));
            }
        }

        private static View.Backend[] Backends(ScopeContext context) =>
            new Coquena.Backends.ScopedBackends(context).List().SelectMany(resource => resource!["installations"]!.AsArray().Select(installation =>
            {
                var external = resource["ownership"]?.GetValue<string>() == "external";
                var selection = installation!["selection"]?["backendSelection"];
                var variant = selection?["variant"]?.GetValue<string>() ?? resource["logical-key"]?.GetValue<string>() ?? "—";
                return new View.Backend(variant, selection?["release"]?.GetValue<string>() ?? "—", external,
                    external ? null : Bytes(new JsonArray(installation.DeepClone())), installation["protected"]?.GetValue<bool>() == true);
            })).ToArray();

        private static long? Bytes(JsonArray installations)
        {
            long total = 0;
            var any = false;
            foreach (var file in installations.SelectMany(installation => installation?["files"] as JsonArray ?? []))
                if (file?["size"]?.GetValue<long>() is { } size) { total += size; any = true; }
            return any ? total : null;
        }

        // The last recorded plan, as `update` reported it. It is not revalidated here.
        private static View.Page Updates(Place place)
        {
            var plans = new ScopeStore(place.Context!).Read().Documents.GetValueOrDefault("local/update-plan.json")?["data"]?["plans"] as JsonObject;
            var latest = plans?.Select(item => item.Value as JsonObject).OfType<JsonObject>()
                .OrderByDescending(plan => plan["created-at"]?.GetValue<string>(), StringComparer.Ordinal).FirstOrDefault();
            if (latest is null) return View.Updates(place.Scope.Name, null, []);
            var report = RecipeUpdatePlans.Report(latest);
            var items = report["resources"]!.AsArray().Select(resource => new View.Update(
                Text(resource?["key"]) is { } key ? (Text(resource!["kind"]) is { } kind && kind != key ? kind + " " : "") + key : "—",
                Text(resource?["current"]) ?? "—", Text(resource?["proposed"]) ?? "—", Text(resource?["diagnostic"]))).ToArray();
            return View.Updates(place.Scope.Name, DateTimeOffset.TryParse(Text(report["created-at"]), out var at) ? at : null, items);
        }

        private static string? Text(JsonNode? node) => node switch
        {
            null => null,
            JsonValue value when value.TryGetValue<string>(out var text) => text,
            JsonObject item => Text(item["revision"]) ?? Text(item["build"]) ?? Text(item["release"]) ?? item.ToJsonString(),
            _ => node.ToJsonString()
        };
    }
}
