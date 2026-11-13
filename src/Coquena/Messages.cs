namespace Coquena;

// Texts shown to people, kept apart from the logic that decides them. Code decides with
// codes and data; this catalog turns them into words. Tests compare codes, data or these
// entries, never literal wording, so the texts can change or be translated later.
// Style: neutral Spanish, infinitive or impersonal forms, no defensive disclaimers.
public static class Messages
{
    // Errors keep their code at the end, for scripts and reports: «Error: texto (código).»
    public static string Error(string code, string text) => $"Error: {text.TrimEnd().TrimEnd('.')} ({code}).";

    public static string UnknownOptions(string command) => Error("invalid-selection", $"No se reconocen esas opciones. Ver: coquena {command} --help");
    public const string UnknownCommand = "No se reconoce ese comando. Ver: coquena --help";

    public static string Kind(string kind) => kind switch
    {
        "backend" => "Backend", "companion" => "Auxiliar", _ => "Modelo"
    };

    // prune
    public static string PruneItem(string kind, string name, long bytes) => $"{Kind(kind)} {name}  {ByteSize.Format(bytes)}";
    public static string PruneReclaimable(long bytes, bool global) => bytes == 0 ? "No hay nada para liberar." :
        $"Se pueden liberar {ByteSize.Format(bytes)}. Para borrarlos: coquena prune{(global ? " --global" : "")} --yes.";
    public static string PruneKept(string name, string code) => code switch
    {
        "file-ownership-uncertain-or-external" or "file-ownership-uncertain" => $"Se conserva {name}: no se pudo comprobar que el archivo sea de Coquena.",
        "file-inaccessible" => $"Se conserva {name}: no se pudo leer el archivo.",
        _ => $"Se conserva {name} ({code})."
    };
    public static string PruneConfirmationRequired => Error("confirmation-required", "Falta confirmar. Repetir con --yes para borrar lo mostrado");
    public const string PruneAsk = "¿Borrar lo mostrado? [s/N] ";
    public static string PruneFreed(long bytes) => $"✔ Se liberaron {ByteSize.Format(bytes)}.";
    public static string PruneFailed(string name, string code) => Error(code, $"No se pudo borrar {name}");
    public const string CombinationForgotten = "Combinación olvidada. Se conservan el YAML, los pesos y las publicaciones.";

    // recipes and authoring
    public const string CombinationNotPrepared = "La combinación todavía no se preparó. Usarla en Up o Iniciar para poder comprobar novedades.";
    public const string RecipeValid = "La combinación es válida con los borradores actuales.";
    public static string AtLine(string file, int line) => $"{file} · línea {line} · ";
    public static string MoreRows(int count) => $"… {count} más";
    public const string NoUpdatePlan = "Sin plan de actualización registrado.";
    public const string ProjectPathGone = "La ruta del proyecto ya no se puede resolver.";
    public static string ReferencesToEntry(int count) => count == 1 ? " (y 1 referencia a esta entrada)" : $" (y {count} referencias a esta entrada)";
    public const string RestNotValidated = " El resto del archivo no se pudo validar.";
    public const string SchemaReferenceUpdated = "Referencia al schema actualizada";
    public const string SchemaReferenceAdded = "Referencia al schema agregada";

    // updates view
    public const string UpdatePlanStale = "▲ Algo cambió desde la última comprobación; pulsar Comprobar para actualizar el plan.";
    public const string UpdateImpact = "Los servicios activos siguen igual hasta reiniciarlos.";
    public static string UsedByIntentions(int count) => $"en {count} intenciones";

    // directories
    public const string MergeOverwriteQuestion = "¿Autorizar todas las sobrescrituras informadas? [s/N]";
    public static string MergeOverwrite(int files) => $"La fusión reemplazará el contenido anterior de {files} archivo(s). {MergeOverwriteQuestion}";

    // system and backends
    public static string BackendChoice(bool chosen) => chosen ? "Selección explícita" : "Elección automática";
    public const string BackendQueryOnly = "Sólo consulta; no descarga, instala ni modifica la selección predeterminada.";
    public const string ProbeLocalOnly = "Sondeo local; no valida un binario llama-server ni garantiza inferencia.";
    public static string NativePlatform(string platform) => $"Plataforma nativa: {platform}";
    public static string PhysicalRam(string? size) => $"RAM física: {size ?? "desconocida"}";
    public static string SavedCandidate(string build) => $"Candidato guardado: {build}";

    // configuration diagnostics, used inside longer lines
    public const string NotServable = "no servible";
    public const string Unsupported = "no admitido";
    public const string BrokenReference = "referencia rota";
    public const string MissingModelFile = "Archivo de modelo ausente";
    public static string MissingPreset(string preset) => $"Preset ausente: {preset}";
    public const string MissingLocalReference = "referencia local ausente";
    public const string RemoveEffect = "Eliminar";
    public const string AfterChange = "Después:";
    public static string KeepEffect(string emission) => $"Conservar/{emission}";

    // models
    public const string NotPaired = "sin emparejamiento";
    public const string NoUnits = "Sin unidades registradas en el ámbito solicitado.";
    public const string ExternalNotApplicable = "no aplica: externo";
    public const string NotSubstituted = "No se sustituye.";
    public const string PartialExploration = "puede ser parcial/no concluyente";
    public static string PendingUpgrade(bool transition) => transition ? "Transición pendiente" : "Upgrade pendiente";
    public const string CleanupCompleted = "y limpieza completadas";
    public const string ReadmeTitle = "README del commit";
    public static string DownloadVariant(string label) => "Descargar " + label;

    // logs and observation
    public const string LogUnavailable = "No disponible";
    public const string LogFileUnavailable = "Archivo no disponible; se reintentará sin controlar el servicio.";
    public const string ChildChanged = "El hijo cambió durante la consulta; muestra descartada.";
    public const string NotAvailable = "no disponibles";
    public const string ModelNotLoaded = "no cargado";
    public const string MetricsScope = "tasas medias del backend, no por tarea ni instantáneas";
    public const string BucketUnknown = "Período del bucket no informado";
    public const string ObservationFrame = "Observación";
    public const string NewInstanceSpan = "Tramo nuevo";
    public const string NoProgressDenominator = "sin denominador";
    public const string RequestUnconfirmed = "resultado de la petición sin confirmar";
    public const string ConnectionCut = "se cortó la conexión";
    public const string TransferFailed = "falló la descarga";
    public const string NoDownloads = "No hay descargas.";
    public const string DownloadCancelled = "La descarga fue cancelada; no se instaló nada.";
    public const string TransferCut = "se cortó la descarga";
    public const string ServerBusy = "El servidor está atendiendo peticiones. Aplicar los cambios ahora puede interrumpirlas.";
    public const string HookTimedOut = "[Hook] No se completó la notificación de fallo de inicio; el resultado del servicio se conserva.";
    public const string ComputeUnconfirmed = "sin confirmar";

    // foreground servers
    public const string ForegroundHint = "[Primer plano] Ctrl+C: parada ordenada (10 s); segundo Ctrl+C: cierre forzado inmediato.";
    public const string CtrlCReceived = "[Ctrl+C recibido] Deteniendo servidor; máximo 10 s. Pulsar Ctrl+C otra vez para forzar el cierre inmediato.";
    public const string SecondCtrlC = "[Cierre forzado] Segundo Ctrl+C recibido; cerrando inmediatamente.";
    public const string NoActiveInstance = "sin instancia activa";
    public const string UncertainIdentity = "identidad incierta";
    public const string FreedThisAttempt = "Espacio efectivamente liberado en este intento";
    public const string ExternalRemoved = "Registro externo retirado; archivos conservados en disco.";

    // service recovery
    public const string RecoveryCleared = "El servicio arrancó correctamente; se descartó el registro del fallo anterior.";
    public const string RecoveryUnavailable = "No se pudo volver a una configuración que funcionara: no hay una anterior o se canceló la operación.";
    public const string RecoveryFellBack = "Se volvió a la última configuración que funcionaba; el fallo de la nueva queda registrado.";
    public static string RecoveryKeptPrevious(string code) => $"La nueva configuración no arrancó ({code}); el servicio sigue funcionando con la anterior.";
    public static string RecoveryFailed(string code) => $"Tampoco arrancó la última configuración que funcionaba ({code}); no se intenta nada más.";

    // verify
    public const string VerifyAllOkPrefix = "✔ Todo en orden";
    public static string VerifyAllOk(int resources, bool checksum) => checksum
        ? $"{VerifyAllOkPrefix}: {resources} recursos revisados con SHA-256."
        : $"{VerifyAllOkPrefix}: {resources} recursos revisados (sin SHA-256; usar --checksum para compararlo).";
    public static string VerifyStatus(string status, bool checksum) => status switch
    {
        "verified" => "SHA-256 coincide",
        "quick-ok" => "Auditoría rápida OK",
        "unavailable" => "Recurso no disponible. Revisar la referencia en la receta y preparar la instalación.",
        "missing-file" => "Falta el archivo. Revisar la ruta en la receta o volver a descargar el recurso gestionado.",
        "read-error" => "No se pudo leer. Revisar los permisos y si otro proceso tiene bloqueado el archivo.",
        "changed-during-read" => "El archivo cambió durante la lectura. Esperar a que termine la escritura y repetir verify.",
        "invalid-structure" => "Estructura GGUF inválida o incompleta. Revisar las partes seleccionadas en la receta.",
        "no-reference" => "Sin referencia SHA-256. Revisar el origen y registrar una referencia si corresponde.",
        "mismatch" => checksum ? "SHA-256 distinto de la referencia. Recuperar los bytes originales."
            : "Tamaño distinto del registrado. Revisar que el archivo esté completo.",
        "known-integrity-failure" => "Fallo de integridad pendiente. Recuperar el recurso y comprobarlo con --checksum.",
        _ => status
    };
    public static string BooleanNeedsValue(string argument, string? value) =>
        $"El argumento {argument} recibió el booleano {value}, pero aquí requiere un valor explícito. ";
    public static string VerifyProblems(int resources, bool checksum) =>
        $"⚠ Hay recursos para revisar ({resources} revisados{(checksum ? " con SHA-256" : "")}).";

    // import
    public static string ImportDone(IReadOnlyCollection<string> categories)
    {
        var presets = categories.Contains("presets");
        var names = string.Join(", ", categories.Select(c => c switch
        {
            "service" => "servicio", "presets" => "presets", "model" => "modelos", "params" => "parámetros", var other => other
        }));
        return presets
            ? "Receta importada. Los presets quedaron publicados o listos para el próximo inicio; el servicio no se inició."
            : $"Receta importada: {names}. El servicio no se inició.";
    }

    // Results of resource commands shown without --json.
    public static string BackendReady(string? variant, string? build, string installation) => $"✔ Backend listo: {string.Join(" ", new[] { variant, build }.Where(p => !string.IsNullOrEmpty(p)))} (instalación {installation}).";
    public static string ModelReady(string name, string installation) => $"✔ Modelo listo: {name} (instalación {installation}).";
    public static string ExternalAdded(string name, string installation) => $"✔ Registrado como externo: {name} (instalación {installation}).";
    public static string ProtectionChanged(string installation, bool on) => on
        ? $"Instalación {installation} protegida: prune no la borra."
        : $"Instalación {installation} sin protección: prune la borra si nada la usa.";
    public static string BaselineSet(string key) => $"✔ Base de integridad guardada para {key}.";
    public static string RemovePending(string path) => $"No se pudo borrar {path}; repetir remove más tarde.";
    public const string NoCombinations = "No hay combinaciones preparadas.";
}
