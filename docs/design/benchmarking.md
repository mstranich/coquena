# Chat de prueba de presets

## Propósito

Chat permite comprobar rápidamente respuesta y telemetría de un preset publicado.
La observación ayuda a revisar su comportamiento sin certificar calidad semántica
ni una configuración óptima para cualquier equipo o carga.

## Laboratorio interactivo

El chat es un banco de pruebas rápido para observar el comportamiento de un
preset mientras se envían unos pocos prompts, como comprobación práctica de que
responde y de cómo evoluciona su telemetría. No sustituye la comprobación técnica
de salud del servicio. Su recorrido se orienta a uno, dos o tres intentos rápidos;
esto no impone un límite artificial de mensajes por sesión.
Se elige desde la jerarquía
servicio → proyecto → preset usada por En vivo. La selección identifica el servidor,
el proyecto y su entrada publicada; el modelo se muestra como información del
preset y de lo que realmente informa el backend, no como un selector independiente
de archivos GGUF. Un mismo modelo puede usarse con configuraciones distintas.
La selección del experimento se distingue de la combinación YAML elegida para editar.

Chat dispone de una vista propia en la TUI. El botón Chat de En vivo abre
esa vista con el preset enfocado y su servicio ya seleccionados. El acceso
rápido conserva la identidad de la entrada; no crea otra pantalla de chat ni elige
otro preset por coincidencia de modelo. Requiere una selección de preset.
En la TUI, Chat es la categoría de la pestaña Herramientas; el destino se elige en
un selector servicio › preset que lista los servicios conocidos y sus presets
publicados. Elegir otro destino, o Nueva sesión, empieza una conversación vacía:
el contexto de un preset no se envía a otro. Mientras llega una respuesta, Enviar
pasa a Detener, que interrumpe sólo la petición propia.

En CLI, `chat <preset> [--service <nombre|default|self> | --global] [--load]`
inicia la prueba; `--global` equivale a `--service global`. El preset se nombra por su nombre publicado (`proyecto/clave`); dentro de
la carpeta de un proyecto basta su clave. Sin `--service`, el servicio es el
destino que declara la receta del proyecto, como en `up`; `--service` elige otro
servicio o resuelve un preset sin receta conocida. También se acepta
`servicio/proyecto/clave`. Si la receta no determina el destino, se busca el
nombre entre los servicios activos: una sola coincidencia se usa; varias se
listan con su servicio para elegir, sin elegir una por coincidencia. Un nombre
no publicado lista los presets publicados de los servicios activos. `--load` pide
al servidor cargar el preset, con la misma operación que el precalentamiento de
un preset, y abre el chat sin esperar. Mientras carga, la línea de telemetría lo
indica con el tiempo transcurrido, sin límite de espera; se puede escribir, pero
Enter no envía hasta que el modelo esté cargado: lo informa en esa línea y
conserva el texto para enviarlo después, editarlo o borrarlo. `/exit` y Ctrl+C
salen en cualquier momento; salir no cancela la carga en el servidor. Una carga
fallida se informa en la misma línea. Así el primer mensaje no incluye la carga
del modelo. Sin `--load`, el primer mensaje carga el preset si el servidor no lo
tiene cargado.
La jerarquía de la TUI y la referencia CLI seleccionan el mismo destino.

El comando abre una sesión interactiva: el usuario escribe un mensaje, lo envía
y consulta la respuesta antes de continuar la conversación. `/exit` en una línea
propia sale del chat; Ctrl+C también solicita salir, tanto durante la
edición como durante la espera de respuesta. La ayuda inicial muestra estas salidas.

Una línea informativa de telemetría permanece debajo del área de escritura y se
actualiza mientras el usuario escribe, espera o recibe la respuesta. El refresco
conserva el texto y la posición de edición; no añade una línea al historial por
cada muestra. Al ocupar varias filas el mensaje, la telemetría sigue debajo del
área de entrada. Identifica destino, estado y métricas disponibles con su alcance;
sin datos conserva la línea con una indicación de indisponibilidad.

Salir cierra la sesión y solicita cancelar la petición propia en curso, sin
detener el servicio ni cancelar peticiones ajenas. Cerrar la conexión del cliente
no acredita que el backend haya detenido la inferencia; se informa esa limitación
si no existe confirmación de cancelación.

La TUI reorganiza las métricas de [En vivo](ui-servicio.md) alrededor de la prueba:
conversación y entrada de mensajes junto a actividad, fases, tokens, tasas y curvas
disponibles; recursos del proceso y hardware compartido con sus respectivos alcances.
El foco está en tiempo y tasa de procesamiento del prompt, tiempo y tasa de
generación, tokens reutilizados de caché y aceptación de especulación cuando
existan fuentes válidas. La caché de prompt/KV se distingue de la caché de expertos
MoE; una cifra de la primera no acredita el rendimiento de la segunda.
La aceptación especulativa identifica tokens propuestos y aceptados, denominador
y período; métricas agregadas del modelo no se presentan como exclusivas del mensaje.
Permite observarlos durante la respuesta sin cambiar de pantalla para seguir el
experimento. La CLI ofrece la misma operación con presentación textual.

Las métricas de la petición enviada por el laboratorio se distinguen de las tareas
observadas, los acumulados de la entrada y los totales del servicio. Se correlacionan
sólo con evidencia de identidad; coincidir en modelo, entrada o intervalo temporal
no demuestra que un slot pertenezca al mensaje. Sin correlación, la actividad se
muestra como contexto del servicio, no como medición exclusiva de esa petición.
Las curvas de CPU/GPU y memoria compartida tampoco se atribuyen íntegramente al chat.

La conversación conserva contexto durante la sesión, sin
historial persistente de conversación. No guarda el diálogo como resultado de
benchmark. Esto no cambia la política de logs del backend ni elimina registros ajenos.

Enviar mensajes no modifica la receta, publica presets ni inicia servicios
implícitamente. Una entrada no disponible produce un diagnóstico. Si el servicio
elegido no está en ejecución, Chat pregunta si iniciarlo (un modal en la TUI, una
pregunta en la CLI) y sólo lo inicia con esa confirmación, con la misma operación
que Iniciar.

Las métricas de la petición salen de la telemetría que llama-server adjunta a la
propia respuesta en streaming: progreso del prompt, tokens reutilizados de caché,
tiempos y tasas de prompt y generación, y borradores especulativos propuestos y
aceptados. Al pertenecer a la respuesta, no dependen de correlacionar un slot con
el mensaje. Los parámetros de generación son los del preset; el chat no los envía. Las tasas
conservan fuente y alcance; una conversación libre no acredita una comparación
controlada ni evalúa automáticamente la calidad de las respuestas.
