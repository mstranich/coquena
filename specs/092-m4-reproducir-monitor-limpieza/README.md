---
status: complete
created: 2026-11-11
priority: medium
tags:
- m4
created_at: 2026-11-11T13:06:39.637477700Z
updated_at: 2026-11-14T03:18:43.385227Z
completed_at: 2026-11-14T03:18:43.385227Z
transitions:
- status: draft
  at: 2026-11-11T13:07:22.726586700Z
- status: planned
  at: 2026-11-13T20:31:13.793536800Z
- status: in-progress
  at: 2026-11-13T20:31:13.869783300Z
- status: complete
  at: 2026-11-14T03:18:43.385227Z
---
# Resolver problemas de concurrencia y coordinación

## Objetivo

Encontrar e implementar soluciones definitivas a las carreras, esperas sin fin e
interbloqueos entre tareas y procesos de Coquena, empezando por la carrera entre
el monitor interno de controles de descarga y la retirada del área temporal de la
operación. Las soluciones deben preservar el resultado de cada operación y los
diagnósticos reales. Entrega de [M4](../../docs/milestones/M4-experiencia-completa.md).

## Problemas

### Monitor de descargas y limpieza

El [hallazgo 002](../../docs/findings/002-coordinacion-monitor-limpieza.md)
documenta una carrera reproducida en código anterior a 054: el finalizador retira
el directorio temporal mientras el monitor todavía consulta los controles de
pausa/cancelación. La excepción del monitor puede convertir una finalización
exitosa en error u ocultar otro diagnóstico.

La revisión del código actual confirma que la carrera sigue vigente:
`DownloadManager.RunAsync` espera al monitor recién en su `finally`, después de
que el finalizador o la cancelación ejecutaron `Cleanup`, y el monitor resuelve
las rutas de control mediante `ScopeContext.PathFor`, que comprueba existencia y
atributos en dos pasos.

### Traslado de directorios desde la TUI

El traslado de los directorios de modelos o backends que inicia la pestaña
Configuración se ejecuta en segundo plano, espera respuestas del usuario sin
cancelación y no se espera al cerrar la TUI. Al salir durante una pregunta, el
hilo queda bloqueado; al salir durante la copia, el proceso termina y el traslado
se interrumpe.

### Reintentos ante archivos en uso

El reemplazo de documentos JSON (`LocalStore`, `ScopeStore`) y el movimiento del
directorio preparado al publicar (`ScopeTransfers`) reintentan unas pocas veces
cuando Windows informa que el archivo está en uso. Los reintentos comprueban el
contenido antes de continuar, pero no se conoce quién mantiene el archivo
abierto. Puede tratarse de lectores de Coquena que no comparten el borrado o de
procesos externos, como el antivirus o el indexador.

### Orden de los locks

El lease del ejecutor de una descarga y el lock del escritor del almacén se
toman en distintos caminos. Debe comprobarse que su orden sea siempre el mismo;
de lo contrario, el límite de espera del escritor convierte un interbloqueo en
errores `writer-busy` espurios.

## Alcance

- Para cada problema, determinar la causa vigente sobre el código actual y
  resolverla. Si el código ya impide el problema, identificar el mecanismo y
  acreditarlo sin modificarlo.
- Monitor y limpieza: impedir lecturas del monitor sobre el área temporal una vez
  retirada. Evaluar la barrera de parada propuesta en el hallazgo sin fijarla
  como implementación obligatoria. Cubrir finalización normal, cancelación y
  reanudación en fase de limpieza, preservando los controles de pausa/cancelación
  durante las fases que los admiten y la exclusión del ejecutor.
- Traslado de directorios: que el cierre de la TUI no deje hilos bloqueados ni
  interrumpa un traslado sin pasar por su recuperación.
- Archivos en uso: identificar qué mantiene abiertos los archivos. Corregir los
  lectores propios que lo causen; conservar un reintento acotado sólo frente a
  causas externas comprobadas o razonadas.
- Orden de locks: documentar el orden y corregir los caminos que lo inviertan.
- Incorporar regresiones que controlen el intercalado conflictivo y validar en
  administrado y mediante publicación y ejecución Native AOT win-x64.

La limpieza tratada es la del área temporal de una descarga, no el comando
`clean` de instalaciones. No incluye rediseñar la TUI ni cambiar políticas de
propiedad, publicación o limpieza de recursos. Aplican los contratos de
[descargas](../../docs/design/descargas.md) y
[persistencia](../../docs/design/persistencia-proyectos.md).

## Criterios de aceptación

- [x] Causa y solución de cada problema documentadas sobre el código actual,
  con evidencia del orden entre el cese de lecturas del monitor y la retirada
  del área temporal.
- [x] Una publicación exitosa conserva su resultado sin errores espurios del
  monitor; los errores reales mantienen su diagnóstico.
- [x] Finalización normal, cancelación y reanudación de limpieza verificadas,
  conservando los controles y la exclusión correspondientes.
- [x] Cerrar la TUI durante un traslado de directorios no deja hilos bloqueados
  ni un traslado interrumpido fuera de su recuperación.
- [x] Se conoce el origen de los bloqueos por archivo en uso; los propios están
  corregidos y los reintentos restantes tienen fundamento.
- [x] El orden entre el lease del ejecutor y el lock del escritor es único y
  está verificado.
- [x] Regresiones automatizadas con intercalado controlado en ámbitos aislados,
  sin depender únicamente de repetir ejecuciones o esperas probabilísticas.
- [x] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 registradas.
- [x] No se ocultan indiscriminadamente errores de E/S, seguridad o permisos,
  ni se afectan recursos publicados, vecinos o externos.

## Causas y soluciones

### Monitor y limpieza

- **Causa:** `RunAsync` detenía el monitor recién en su `finally`, después de que el
  finalizador o la cancelación borraran el área con `Cleanup`. Mientras tanto el monitor
  seguía resolviendo rutas con `PathFor`, que comprueba existencia y atributos en dos pasos.
- **Solución:** el monitor tiene su propia señal de parada, separada del token de
  transferencia y publicación, y corre fuera del contexto del llamador. Las tres rutinas
  que borran el área (`DownloadManager.Cleanup`, `ModelInstaller.Cleanup` y
  `BackendInstaller.Cleanup`) llaman antes a `DownloadManager.StopMonitor`, que espera a
  que el monitor deje de leer. Un fallo real del monitor se sigue informando en `RunAsync`.

### Traslado de directorios desde la TUI

- **Solución:** la espera de respuesta (`SettingsForm.AnswerOrCancel`) responde «Cancelar»
  cuando la TUI se cierra. Al salir, la TUI espera a que termine un traslado en curso
  (`SettingsForm.Pending`) en vez de cortarlo.

### Archivos en uso

- **Causa comprobada:** un lector que abre un documento sin compartir el borrado, como
  hace `File.ReadAllBytes`, hace fallar `File.Replace` con el error 32. Se confirmó
  experimentalmente: con `FileShare.Read` falla; con `FileShare.Delete` el reemplazo funciona.
  Los lectores sin bloqueo de proyectos, recetas y planes usaban `File.ReadAllBytes`.
- **Solución:** `SharedFile` lee compartiendo el borrado, y se usa en los lectores de
  documentos que pueden reemplazarse (`Projects`, `RecipeReader`, `RecipeYaml`,
  `ScopeTransfers`, `ScopedServicePlan`, `PublicationCli`). Los reintentos acotados
  se conservan frente a procesos externos (antivirus, indexador), ya que validan el
  contenido antes de seguir.
- El reintento de `Directory.Move` en `ScopeTransfers` se conserva sin cambios. Coquena
  cierra sus propios handles antes de mover, así que lo que queda son lectores externos.
  Si los reintentos se agotan, la operación sigue en `publishing` y se puede reanudar.

### Orden de los locks

- **Sin cambios:** `Lease` intenta abrir `executor.lock` dentro del lock de escritor y, si
  está ocupado, falla de inmediato con `executor-busy`, sin esperar. El único orden con
  espera es lease → escritor, así que no puede formarse un ciclo.

## Evidencia

- Regresión `CleanupWaitsForMonitor` (`DownloadTests`): retiene al monitor dentro de su
  ventana de lectura mientras el finalizador limpia, en finalización normal y en
  reanudación de una limpieza pendiente. Al quitar la barrera la prueba falla
  ("Download assertion 6 failed"); con la barrera pasa.
- `ManagementTests`: una pregunta encolada en una TUI cerrada se resuelve como Cancelar;
  reemplazo con un lector compartido abierto; un lector sin compartir el borrado bloquea
  el reemplazo sin alterar el documento.
- Native AOT win-x64 (app y runner publicados con `TreatWarningsAsErrors`): pasan
  `--download-tests` (68), `--dashboard-tests` (265), `--contention-tests` y
  `--backend-publication-tests`.
- Suite administrada completa (`Coquena.Tests` sin argumentos, desde la raíz del
  repositorio): termina con código 0 y sin excepciones.
