# Pantalla Servicios de la TUI

En vivo muestra un árbol lateral con los servicios globales conocidos y la
receta local. El proyecto aparece antes de su primer inicio o publicación,
también si sólo consume un servicio global. Su identidad y acciones siguen
[interfaces visuales](interfaces-visuales.md#panel-del-proyecto).
Cada servicio conserva su identidad, controles y logs. Sus entradas observadas
se agrupan por prefijo de nombre publicado; un prefijo no identifica al cliente
que envió una petición. Una entrada externa sin prefijo permanece visible.

## Distribución

El contenido del servicio enfocado se organiza en estas zonas:

1. Cabecera: nombre, puerto, estado, salud y CPU/RAM privada del proceso
   verificado, con controles de ciclo. La receta local conserva un bloque
   separado con Up/Down, destino y Preparación privada.
2. Entrada enfocada: estado y fase, acumulados y medias del backend separados
   de las curvas y tasas observadas por tarea. La lista conserva tareas activas
   y recientes y permite consultar su detalle.
3. Log del servicio, con Pausar, Seguir e Histórico. En terminal baja se amplía
   como vista hermana del monitor; volver al monitor conserva la captura.

El hardware es compartido por la máquina. Sus curvas y fuentes se consultan en
Hardware desde la barra inferior, separadas del consumo del proceso del servicio.
Los [wireframes](tui/A-en-vivo.salt.puml) y la
[distribución compacta](tui-servicio-compacto.salt.puml) ilustran esta jerarquía.

Las entradas activas tienen prioridad visual. La fase, la cola y la vigencia de
las muestras permiten distinguir avance, espera y falta de observación. Al
reducir el ancho se simplifican gráficos y columnas secundarias antes de perder
entrada, fase y progreso disponible. Si falta altura para consultar el log
junto al monitor, se accede desde la subpestaña Logs del mismo servidor.

El hardware conserva su alcance global al cambiar de pestaña. Cada serie
identifica dispositivo o fuente, valor y unidad. Las mediciones ausentes se
indican como no disponibles.

Los gráficos y la barra de GPU/VRAM incluyen los adaptadores cuya identidad
puede correlacionarse con compatibilidad CUDA o Vulkan confirmada. Sistema
permite consultar también el inventario y los motivos de falta de confirmación.

Las series de recursos y la fase de la tarea enfocada comparten columnas de
dos segundos, ubicadas por la fecha de cada lectura. Cada dispositivo conserva
sus huecos aunque otros sigan respondiendo. La leyenda identifica la resolución,
la hora y las fases observadas. La coincidencia temporal permite comparar
actividad y recursos compartidos sin atribuir causalidad ni consumo exclusivo.
El histórico conserva hasta 24 columnas durante la sesión; al reducir el ancho
se muestra su tramo más reciente.

Una lectura con más de cuatro segundos de antigüedad se marca como antigua.
El detalle conserva la cifra y su fecha. Esta marca afecta la presentación del
valor actual; el gráfico conserva cada lectura en su instante original y los
intervalos sin lectura permanecen visibles como huecos.

El panel Recursos muestra fuente, unidad, alcance, fecha, ventana de medición
y motivo de indisponibilidad de cada contador. Distingue el consumo de máquina,
los totales por dispositivo y las mediciones de instancia o proceso verificado.
Muestra por separado memoria privada comprometida, RAM residente por proceso,
VRAM dedicada y memoria compartida de GPU. Permite consultar duración de captura
e intervalo efectivo entre capturas junto al refresco visual y la cadencia mínima.

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

[Monitor con log](tui/A-en-vivo.salt.puml) ·
[Monitor compacto](tui-servicio-compacto.salt.puml) ·
[Logs en terminal baja](tui-logs.salt.puml).

## Logs y navegación

La pestaña de servidor determina el alcance de actividad y logs. El encabezado
identifica el servicio y su alias default cuando corresponde. El log sigue al
servicio entre reinicios y cambios de archivo, con una indicación de transición.

Configuración › TUI ofrece «Filtrar logging de monitoreo». Sistema es otra
categoría de Configuración e identifica también la versión del ejecutable. La preferencia global se guarda al cambiar la casilla y viene
activada por defecto. Oculta en la vista de logs los avisos de reenvío del
router antes de seleccionar las líneas recientes. Esos avisos también pueden
corresponder a inferencias, porque el mensaje no identifica la ruta HTTP.
Los archivos de log conservan todas las líneas.

El scroll y la pausa afectan la visualización. Se puede volver al seguimiento
y consultar el historial de archivos. Abrir, cerrar o navegar la pantalla
conserva el ciclo de vida del servidor y su configuración.

El seguimiento mantiene un buffer acotado por servicio visitado. La lectura se
ordena desde las líneas anteriores hacia las nuevas; en seguimiento la vista
avanza al final. Seleccionar una fila o desplazar la lectura suspende ese avance,
sin detener la captura. «Pausar log» congela sólo el texto del log; la pausa del
monitor es independiente. «Reanudar log» vuelve al avance y «Actual / seguir»
abandona un histórico y vuelve inequívocamente al servicio observado.

Los históricos se eligen por instancia, fecha y archivo registrado del mismo
servicio. Su lectura tiene un tamaño final fijo, incluso para un archivo activo,
y se muestra como histórico. La captura actual continúa en segundo plano. Si
el archivo ya no está disponible, se informa el fallo sin elegir otro archivo.
Eliminar y recrear el nombre del servicio desconecta la identidad anterior.

La memoria del observador no sustituye la retención en disco: se conservan hasta
2000 filas completas y 256 Ki caracteres por buffer, más una línea parcial de
hasta 8193 unidades UTF-16. Las líneas largas se segmentan sin romper pares
sustitutos y se pueden recorrer horizontalmente. Se informa cuántas filas
salieron del buffer; una vista pausada conserva su lectura hasta reanudar, pero
no garantiza conservar las líneas que llegaron durante la pausa. El filtro se
aplica sobre los datos capturados antes de poblar la vista; no amplía el buffer.

Con altura suficiente, el log ocupa el espacio que queda bajo el monitor.
En terminal baja, Monitor conserva fases y progreso y Logs se abre como
subpestaña del mismo servicio. El encabezado del servidor y el hardware global
permanecen visibles. El estado distingue seguimiento, pausa visual, histórico,
espera con servicio detenido y desconexión. Los cambios de instancia, truncados
y fallos de acceso se anuncian sin modificar el ciclo del backend.
