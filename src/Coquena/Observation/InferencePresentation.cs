using System.Diagnostics;
using System.Globalization;

namespace Coquena.Observation;

// Formatting shared by the live list and detail; never substitutes entry means for task rates.
internal static class InferencePresentation
{
    public static IReadOnlyList<InferenceTask> Rows(InferenceTasks tasks, string scope, string service) =>
        tasks.Active.Where(task => task.Identity.Scope == scope && task.Identity.Service == service).OrderBy(task => task.Identity.Entry, StringComparer.Ordinal)
            .ThenBy(task => task.Identity.Slot)
            .Concat(tasks.History.Where(task => task.Identity.Scope == scope && task.Identity.Service == service).Reverse()).ToArray();

    // Scope and service remain part of the row identity across router replacements.
    private static string Number(double? value) => value?.ToString("0.#", CultureInfo.CurrentCulture) ?? "n/d";

    public static string Phase(InferenceTask task) => task.End switch
    {
        InferenceTaskEnd.LeftSlot => "fuera del slot",
        InferenceTaskEnd.ChildReplaced => "hijo reemplazado",
        InferenceTaskEnd.ObservationLost => "señal perdida",
        _ => task.Phase switch
        {
            InferenceTaskPhase.Prompt => "PROMPT",
            InferenceTaskPhase.Generation => "GENERACIÓN",
            _ => "fase incierta"
        }
    };

    public static string Row(InferenceTask task, int width)
    {
        var id = task.Identity;
        var rate = task.Phase == InferenceTaskPhase.Generation ? task.OutputRate : task.PromptRate;
        var tokens = task.Phase == InferenceTaskPhase.Generation ? $"G {task.Last.Decoded}" : $"P {task.Last.Prompt}";
        var text = $"{id.Entry} · s{id.Slot}/t{id.Task} · {Phase(task)} · {tokens} tk";
        if (width >= 75) text += $" · {Number(rate)} tk/s";
        if (width >= 115) text += $" · caché {task.Last.Cached} · hijo {id.Pid}";
        return text;
    }

    public static string Detail(InferenceTask task)
    {
        var id = task.Identity;
        var at = DateTimeOffset.UtcNow - Stopwatch.GetElapsedTime(task.ObservedAt);
        return $"Entrada {id.Entry} · modelo {task.Model ?? "n/d"}\n" +
            $"Ámbito {id.Scope} · servicio {id.Service}\nInstancia {id.Instance}\n" +
            $"Hijo {id.Pid}/{id.Creation} · slot {id.Slot} · tarea {id.Task} · tramo {id.FirstObserved}\n" +
            $"{Phase(task)} · última lectura {at.ToLocalTime():HH:mm:ss.fff}\n" +
            (task.End is null ? "Tarea observada activa" : "Últimos valores observados; resultado de la petición sin confirmar") + "\n" +
            $"Prompt {task.Last.Prompt} tk · caché {task.Last.Cached} tk · salida {task.Last.Decoded} tk\n" +
            $"Contexto del slot {task.Last.Context?.ToString() ?? "n/d"} tk · especulación {task.Last.Speculative?.ToString() ?? "n/d"}\n" +
            $"Límite de predicción {task.Last.PredictLimit?.ToString() ?? "n/d"} · no predice longitud final\n" +
            $"Prompt: {Number(task.PromptRate)} tk/s · media {Number(task.PromptMean)} · pico observado {Number(task.PromptPeak)} · {task.PromptSeconds:0.###} s\n" +
            $"Generación: {Number(task.OutputRate)} tk/s · media {Number(task.OutputMean)} · pico observado {Number(task.OutputPeak)} · {task.OutputSeconds:0.###} s\n" +
            "Fuente /slots?model&autoload=false · build b12058-3f8af15a7\n" +
            "Tasa: delta de tokens / segundos entre lecturas continuas (hasta 3 s).\n" +
            "Media: suma de deltas / tiempo de los intervalos medibles de cada fase.\n" +
            "Pico observado: máximo de esas tasas; excluye huecos y transiciones.\n" +
            "Tiempo: suma de intervalos medibles, no latencia completa de la petición.\n" +
            "Progreso %, ETA y TTFT: n/d · fuente sin denominador/tiempo inicial acreditado.\n" +
            "Salida restante y separación razonamiento/respuesta: n/d · fuente no disponible.\n" +
            "Curvas de sesión: hasta 24 columnas de 2 s; · sin lectura, sin interpolación.";
    }
}
