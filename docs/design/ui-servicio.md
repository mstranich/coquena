# Pantalla Servicios de la TUI

La TUI muestra una pestaña por servidor conocido, con los globales primero y
los del proyecto después. Cada pestaña identifica ámbito, servicio y estado.
Las herramientas del proyecto y las preferencias se ubican al final.

## Distribución vertical

1. Actividad y rendimiento del servidor seleccionado: entradas activas, fases,
   avance disponible y series de prompt y generación con alcance y unidad.
2. Series de hardware compartido por dispositivo, como detalle de la barra de estado común.
3. Logs del servidor seleccionado, en el espacio restante.

Las entradas activas tienen prioridad visual. La fase, la cola y la vigencia de
las muestras permiten distinguir avance, espera y falta de observación. Al
reducir el ancho se simplifican gráficos y columnas secundarias antes de perder
entrada, fase y progreso disponible. Si falta altura para consultar el log
junto al monitor, se accede desde la subpestaña Logs del mismo servidor.

El hardware conserva su alcance global al cambiar de pestaña. Cada serie
identifica dispositivo o fuente, valor y unidad. Las mediciones ausentes se
indican como no disponibles.

## Slots, tareas y entradas

Cada fila identifica servicio, slot, tarea y entrada cuando el backend permite
correlacionarlos. La identidad observada incluye instancia y entrada: los números
de slot o tarea no son identificadores globales y pueden repetirse entre procesos
o después de un reinicio. No se atribuye una tarea a un proyecto o cliente sólo
por coincidencia de números; el prefijo de una entrada aporta su nombre publicado,
no demuestra quién envió la petición.

La actividad distingue espera en cola, procesamiento de prompt, generación y
finalización cuando hay evidencia. Una tarea en cola puede no tener slot asignado.
Un contador agregado de peticiones en espera no permite inventar filas individuales.
Los datos disponibles incluyen tokens procesados y cacheados, progreso del prompt,
tiempo de fase, tokens de salida y tasas con sus unidades/ventanas de medida.
La semántica de fuentes, estimaciones y datos no disponibles sigue
[observabilidad](observabilidad.md) e [interfaces visuales](interfaces-visuales.md).

Sobre el detalle de tareas, un resumen por entrada muestra acumulados de prompt,
caché y salida, tasas medias del backend, actividad y cola agregadas. Conserva
fuente, período y generación del proceso. Un contador de cola sin identidades se
presenta sólo en ese resumen, no como una tarea inventada. Los totales del servicio
siguen las reglas de agregación de [observabilidad](observabilidad.md#cifras-por-tarea-entrada-y-servicio).

Las filas individuales muestran cifras de la tarea, no tasas agregadas de entrada
copiadas en cada fila. Identifican tiempo de prompt o generación, unidad y alcance
de la tasa. Sin evidencia se muestra no disponible; el progreso porcentual, TTFT,
ETA y picos no se suponen por tener un gráfico. Una tarea finalizada conserva
valores confirmados o los últimos observados, distinguiéndolos.

La identidad de la fila incluye también la generación del hijo: reemplazar el
proceso de un modelo separa tareas aunque el servicio y router sigan activos.

## Histórico visual

Una preferencia del observador configura cuántas filas de tareas finalizadas se
conservan visibles en el histórico. Las filas activas se distinguen de las finalizadas;
el límite histórico no limita la ejecución ni elimina actividad actual. El histórico
es acotado a la sesión y se puede recorrer cuando no cabe en pantalla.

El límite configura exclusivamente las filas históricas del observador. La capacidad
de ejecución del backend y la retención de logs se configuran por separado.
Una fila finalizada conserva sus últimos valores observados. El historial distingue
instancias aunque sus tareas tengan el mismo número.

## Disposición

[Monitor con log](tui-servicio.salt.puml) ·
[Monitor compacto](tui-servicio-compacto.salt.puml) ·
[Logs en terminal baja](tui-logs.salt.puml).

## Logs y navegación

La pestaña de servidor determina el alcance de actividad y logs. El encabezado
identifica el servicio y su alias default cuando corresponde. El log sigue al
servicio entre reinicios y cambios de archivo, con una indicación de transición.

El scroll y la pausa afectan la visualización. Se puede volver al seguimiento
y consultar el historial de archivos. Abrir, cerrar o navegar la pantalla
conserva el ciclo de vida del servidor y su configuración.
