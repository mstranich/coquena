# Pantalla Servicios de la TUI

La pestaña Servicios muestra el consumo de hardware de la máquina, la actividad
de los servicios globales y de proyecto, y los logs del servicio seleccionado.

## Distribución vertical

1. Hardware de la máquina: CPU, RAM y cada dispositivo GPU con su VRAM.
2. Actividad: resumen por entrada/modelo y detalle de slots/tareas, distinguidos por servicio.
3. Logs: selector de servicio y salida que ocupa el resto del espacio disponible.

El hardware aparece primero y conserva su alcance global al cambiar el servicio
cuyo log se consulta. No se repiten ni suman totales de GPU como si cada servicio
fuese propietario de ellos. Cada serie muestra valor, unidad y dispositivo/fuente;
una medición ausente se indica como no disponible.

La tabla de actividad muestra fase y cifras con suficiente presencia para consultar
el estado de un vistazo. Al reducir la terminal se simplifican gráficos y columnas
secundarias antes de perder servicio, entrada, actividad y progreso disponible.
Los logs aprovechan el espacio restante. La fase y el progreso de las tareas
permanecen visibles al ajustar el tamaño del panel.

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

[Pantalla Servicios](tui-servicio.salt.puml).

## Logs y navegación

La selección de servicio cambia únicamente el panel de logs; la vista de hardware
y la tabla general mantienen su alcance. El selector identifica el servicio y el
alias default cuando corresponde. El panel sigue al servicio entre reinicios y
cambios de archivo, señalando la transición sin exigir reconexión manual.

Scroll y pausa son de visualización: no detienen inferencia ni alteran el servicio.
El acceso al historial de archivos es independiente del seguimiento del servicio.
Abrir, cerrar o navegar la pantalla no inicia, detiene ni reconfigura servicios.
