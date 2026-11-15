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
        "backend" => "Backend", "companion" => "Auxiliar", "download" => "Descarga", _ => "Modelo"
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
    public static string PruneKept(Projects.CleanupNote note) => note.Reference is { } reference
        ? $"Se conserva {note.Name}: {CleanupReference(reference)}." : PruneKept(note.Name, note.Code);
    public static string CleanupReference(Projects.UsageReference reference) => reference.Kind switch
    {
        "combination-index" => $"lo usa una combinación registrada en {reference.Scope}",
        "update-plan" => $"lo necesita una actualización pendiente en {reference.Scope}",
        "active" => $"está incluido en una ejecución registrada en {reference.Scope}",
        "last-good" => $"se conserva para recuperación en {reference.Scope}",
        "download" => $"lo necesita una descarga pendiente en {reference.Scope}",
        _ => $"tiene una referencia de servicio o publicación registrada en {reference.Scope}"
    };
    public static string UsageReferencedRow(string name, Projects.UsageReference reference) => $"    se conserva {name}: {CleanupReference(reference)}.";
    public static string PruneConfirmationRequired => Error("confirmation-required", "Falta confirmar. Repetir con --yes para borrar lo mostrado");
    public const string PruneAsk = "¿Borrar lo mostrado? [s/N] ";
    public static string PruneFreed(long bytes) => $"✔ Se liberaron {ByteSize.Format(bytes)}.";
    public static string PruneFailed(string name, string code) => Error(code, $"No se pudo borrar {name}");
    public const string CombinationForgotten = "Combinación olvidada. Se conservan el YAML, los pesos y las publicaciones.";

    // Maintenance: codes and persisted data remain independent of these texts.
    public const string UsageDescription = "Muestra el espacio usado y los archivos que se pueden borrar en cada proyecto y en el almacenamiento global. Alias: df.";
    public const string UsageEmptySelection = "No hay archivos de Coquena en los proyectos o en el almacenamiento global seleccionados.";
    public const string UsageNoProject = "No hay un proyecto en esta carpeta.";
    public const string UsageProjectEmpty = "Este proyecto todavía no tiene archivos guardados por Coquena.";
    public const string UsageGlobalEmpty = "Todavía no hay archivos en el almacenamiento global.";
    public const string UsageHere = "Este proyecto";
    public const string UsageGlobal = "Global";
    public const string UsageProject = "Proyecto";
    public const string UsageOtherProject = "Otro proyecto";
    public const string UsageBlockedDetails = "⚠ Coquena no pudo abrir estos proyectos registrados. Esto no impide limpiar los recursos sin referencias vigentes:";
    public const string UsageForgetHint = "  Ver la lista con coquena project list. Si esos proyectos ya no se usan, revisar su retirada con coquena project forget --inaccessible --dry-run.";
    public const string UsageBlocked = "  No se puede limpiar todavía: Coquena no pudo leer algunos proyectos y no sabe qué archivos siguen usando.";
    public const string UsageNothingToClean = "  No hay archivos que se puedan borrar con los filtros elegidos.";
    public const string MaintenanceModels = "Modelos";
    public const string MaintenanceCompanions = "Auxiliares";
    public const string MaintenanceBackends = "Backends";
    public const string UsageDownloadUncertain = "pendiente de revisar";
    public const string UsageDownloadReclaimable = "se puede borrar";
    public const string ProjectHelpDescription = "Lista los proyectos registrados en Coquena o retira los elegidos de esa lista. Se puede usar un identificador completo o su inicio, si identifica un único proyecto.";
    public const string ProjectInvalidArguments = "No se reconocen esos argumentos. Ver coquena project --help.";
    public const string ProjectForgetConfirmationRequired = "Revisar los proyectos con --dry-run. Para retirarlos de la lista sin una pregunta interactiva, usar --yes.";
    public const string ProjectForgetAsk = "¿Olvidar los proyectos mostrados? [s/N] ";
    public const string CleanupTypeMismatch = "El recurso indicado no pertenece a ninguno de los tipos elegidos.";
    public const string ProjectUniqueIdRequired = "Usar un identificador de coquena project list. Si se escribe sólo el inicio, debe identificar un único proyecto.";
    public const string ProjectUnreachableForCleanup = "No se pudo abrir ese proyecto. Revisar su ubicación y permisos con coquena project list. Si ya no se usa, se puede retirar con coquena project forget.";
    public const string ProjectForgetSelectionRequired = "Indicar los proyectos que se quieren olvidar, o usar --inaccessible para elegir todos los que Coquena no puede abrir.";
    public const string ProjectIndexEmpty = "No hay proyectos conocidos.";
    public const string ProjectUnknownId = "No hay ningún proyecto registrado con ese identificador. Ver coquena project list.";
    public const string ProjectAmbiguousId = "Ese inicio coincide con varios proyectos. Escribir más caracteres del identificador.";
    public const string ProjectIndexChanged = "La lista de proyectos cambió desde que se mostró. Revisarla de nuevo antes de olvidar proyectos.";
    public const string ProjectChangedBeforeForget = "Uno de los proyectos cambió desde que se mostró. Revisar la lista de nuevo antes de olvidarlo.";
    public const string CleanupScopesChanged = "Los proyectos relacionados con esta limpieza cambiaron. Volver a consultar qué se puede borrar.";
    public const string CleanupWrongScope = "La lista de archivos para borrar corresponde a otro proyecto o almacenamiento.";
    public const string CleanupFilesChanged = "Los archivos o sus referencias cambiaron. Volver a consultar qué se puede borrar.";
    public const string CleanupUnknownOperation = "Hay una carpeta de descarga que Coquena no reconoce. Se conserva para evitar borrar archivos sin saber a qué descarga pertenecen.";
    public const string CleanupTransferIdentityUnknown = "El registro de una descarga no coincide con el proyecto. Se conservan los archivos; revisar la descarga antes de limpiar.";
    public const string CleanupOperationDirectoryMismatch = "El identificador de la descarga no coincide con su carpeta. Se conservan sus archivos.";
    public const string CleanupInstallationLocationUnknown = "No se pudo verificar la carpeta de instalación. Se conservan sus archivos.";
    public const string CleanupNotRegularFile = "La ruta corresponde a una carpeta o un enlace. No se borra como si fuera un archivo de Coquena.";
    public const string CleanupProjectUnreadable = "No se puede limpiar todavía: Coquena no pudo leer un proyecto y no sabe qué archivos sigue usando. Revisar coquena project list. Si ya no se usa, revisar su retirada con coquena project forget --inaccessible --dry-run.";
    public const string MaintenanceSelectors = "[--global] [-f archivo] [--project id] [--model|--companion|--backend] [--select referencia] [--downloads]";
    public const string UsageHelp = "usage " + MaintenanceSelectors + "\n" + UsageDescription;
    public const string ProjectHelp = "project list [--json]\nproject forget <id>... [--dry-run|--yes]\nproject forget --inaccessible [--dry-run|--yes]\n" + ProjectHelpDescription;
    public const string CleanupHelp = "clean " + MaintenanceSelectors + " [--dry-run|--yes]\nAlias: prune.\nSin elegir un proyecto o --global, se limpia el proyecto de la carpeta actual.\nSin filtros, se eligen instalaciones que ya no se usan.";
    public static string UsageInvalidOptions => "No se reconocen esas opciones. Uso: " + UsageHelp.Split('\n')[0];
    public static string UsageFailed(string detail) => "No se pudo medir el espacio: " + detail;
    public static string UsageHeading(string title, long bytes, string folder) => $"{title}: {ByteSize.Format(bytes)}  ({folder})";
    public static string UsageResourceRow(string label, long bytes, long free) =>
        $"  {label,-12}{ByteSize.Format(bytes),12}" + (free > 0 ? $"   se pueden borrar {ByteSize.Format(free)}" : "");
    public static string UsageDownloadsRow(long bytes) => $"  {"Descargas",-12}{ByteSize.Format(bytes),12}   archivos temporales y registros";
    public static string UsageDownloadRow(string name, long bytes, Guid id, bool blocked) =>
        $"    descarga ({(blocked ? UsageDownloadUncertain : UsageDownloadReclaimable)}): {name} ({ByteSize.Format(bytes)}) · {id:D}";
    public static string UsageLogsRow(long bytes) => $"  {"Logs",-12}{ByteSize.Format(bytes),12}";
    public static string UsageUnusedRow(string name, long bytes) => $"    {name} ({ByteSize.Format(bytes)})";
    public const string UsageRemovableResources = "  Para borrar · sin referencias ni protección:";
    public const string UsageKeptHeading = "  Se conservan:";
    public static string UsageKeptRow(string name, IEnumerable<string> reasons) => $"    {name} — {string.Join("; ", reasons)}";
    public static string UsageInstallationLabel(string name, string id) => $"{name} [instalación {id[..Math.Min(8, id.Length)]}]";
    public static string UsageShortReference(Projects.UsageReference reference, bool local) =>
        (reference.Kind switch { "combination-index" => "combinación", "update-plan" => "actualización pendiente",
            "active" => "ejecución", "last-good" => "recuperación", "download" => "descarga pendiente", _ => "servicio/publicación" }) +
        (local ? "" : $" ({reference.Scope})");
    public static string UsageUnavailableSummary(int paths) => $"⚠ Rutas de proyectos inaccesibles: {paths}. No bloquean esta limpieza.";
    public const string UsageUnavailableHint = "  Revisar: coquena project list · Olvidar: coquena project forget --inaccessible";
    public static string UsageUncertainRow(string name, long bytes) => $"    podría estar en uso por un proyecto que no se pudo leer: {name} ({ByteSize.Format(bytes)})";
    public static string UsageExternal(long bytes) => $"  Incluye {ByteSize.Format(bytes)} de archivos externos que Coquena no borra.";
    public static string UsageReclaimable(long bytes, string command) => $"  Se pueden liberar {ByteSize.Format(bytes)} con {command}.";
    public static string ProjectForgetSummary(int count) => (count == 1 ? "Se retirará 1 proyecto de la lista de Coquena." : $"Se retirarán {count} proyectos de la lista de Coquena.") +
        " Sus archivos no se borran. Al dejar de tenerlos en cuenta, algunos archivos globales podrían quedar disponibles para limpiar. Los archivos que usen otros proyectos o servicios se conservan.";
    public static string CleanupReclaimable(long bytes) => bytes == 0 ? "No hay archivos para borrar con estos filtros." : $"Se pueden liberar {ByteSize.Format(bytes)}.";
    public const string MaintenanceWorking = "Trabajando… se puede cambiar de vista mientras sigue.";
    public const string MaintenanceVerify = "Verificar";
    public const string MaintenanceVerifyHint = "revisar la receta y sus archivos";
    public const string MaintenancePreview = "Limpiar…";
    public const string MaintenanceCleanupHint = "archivos de Coquena que ya no se usan";
    public const string MaintenanceDownloads = "Descargas detenidas";
    public const string MaintenanceColumns = "RECURSO / INSTALACIÓN                 TAMAÑO";
    public const string MaintenanceEmpty = "No hay archivos para borrar con estos filtros.";
    public const string MaintenanceDelete = "Borrar lo mostrado…";
    public const string MaintenanceForget = "Olvidar…";
    public static string MaintenanceTitle(string scope) => "Mantenimiento de " + scope;
    public static string MaintenanceRecoverable(long bytes) => "Se pueden liberar: " + ByteSize.Format(bytes);
    public static string MaintenanceDownloadName(string name) => "Descarga " + name;
    public static string MaintenancePlanReady(string scope, long bytes) => $"Limpieza de {scope}: se pueden liberar {ByteSize.Format(bytes)}.";
    public static string MaintenanceDeleteQuestion(int count, long bytes) => $"Se borrarán los {count} elementos mostrados para liberar {ByteSize.Format(bytes)}.\nAntes de borrar, Coquena vuelve a comprobar que no estén en uso.";
    public const string MaintenanceDeleteConfirm = "Borrar lo mostrado";
    public const string MaintenanceCancel = "Cancelar";
    public static string MaintenanceCleanupTitle(string scope) => "Limpiar · " + scope;
    public static string MaintenanceForgetTitle(string scope) => "Olvidar proyecto · " + scope;
    public const string MaintenanceForgetErrorTitle = "Olvidar proyecto";
    public const string MaintenanceForgetConfirm = "Olvidar";
    public const string MaintenanceAccept = "Aceptar";
    public static string MaintenanceLastCleanup(DateTimeOffset at) => "Última limpieza de esta sesión · " + at.ToString("dd/MM HH:mm");
    public const string CleanupDownloadChanged = "La descarga cambió desde que se mostró. Volver a consultar qué se puede borrar.";
    public const string CleanupDownloadSizeChanged = "El tamaño de la descarga cambió desde que se mostró. Volver a consultar qué se puede borrar.";
    public const string CleanupInvalidOptions = "No se reconocen esas opciones. Ver coquena clean --help.";
    public const string CleanupSelectionChanged = "La lista de archivos para borrar cambió desde que se mostró. Repetir clean para revisarla.";
    public const string CleanupGlobalHeading = "Global: ";
    public const string CleanupProjectHeading = "Proyecto: ";
    public const string ProjectCannotInitialize = "No se puede inicializar esta carpeta: ya tiene una receta o Coquena no puede acceder a ella.";

    // recipes and authoring
    public const string CombinationNotPrepared = "La combinación todavía no se preparó. Usarla en Up o Iniciar para poder comprobar novedades.";
    public const string RecipeValid = "La combinación es válida con los borradores actuales.";
    public static string AtLine(string file, int line) => $"{file} · línea {line} · ";
    public static string MoreRows(int count) => $"… {count} más";
    public const string NoUpdatePlan = "Sin plan de actualización registrado.";
    public const string ProjectPathGone = "La ruta del proyecto ya no se puede resolver.";
    public const string NoKnownProjects = "No hay proyectos conocidos.";
    public const string ForgetProjectHint = "quitarlo de los proyectos conocidos; no borra archivos";
    public static string ProjectForgotten(string folder) =>
        $"Proyecto olvidado: {folder}. No se borró ningún archivo; si se vuelve a usar, se registra de nuevo.";
    public static string ProjectState(string code) => code switch
    {
        "available" => "disponible",
        "no-recipe" => "sin receta",
        _ => "inaccesible"
    };
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
