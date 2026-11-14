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

En CLI, `chat --preset <service>/<preset>` inicia la prueba con el servicio
y el nombre publicado del preset indicados. El componente de preset identifica
la entrada completa dentro del servicio, incluida la identidad publicada del
proyecto cuando corresponda; no es sólo un nombre corto ambiguo entre proyectos.
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
implícitamente. Una entrada no disponible produce un diagnóstico. Las tasas
conservan fuente y alcance; una conversación libre no acredita una comparación
controlada ni evalúa automáticamente la calidad de las respuestas.
