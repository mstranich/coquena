# Ajuste MoE y prueba rápida de presets

## Propósito

Coquena facilita obtener una recomendación de caché MoE para un preset, conservar
su evidencia y guardar los parámetros elegidos en una receta distribuible.
Chat permite comprobar rápidamente respuesta y telemetría de un preset publicado.
La observación y la calibración ayudan a decidir; no certifican calidad semántica
ni garantizan una configuración óptima para cualquier equipo o carga.

## Calibración de caché MoE

`tool moe-cache-tune` y Ajuste fino MoE en Herramientas usan la misma operación.
El procedimiento toma como referencia `scripts/moe-cache-tune.py`: ejecuciones de
calibración, trazas de expertos, simulación de caché y ajuste de un modelo de tiempos.
Identifica backend, modelo/archivos y configuración usados, errores del ajuste,
valores medidos, predicciones y propuesta de tamaño de caché y proporción CPU.
No presenta una configuración simulada como una ejecución realmente medida.

Parte del preset de la combinación YAML seleccionada, aunque no esté publicado.
Puede usar un backend alternativo sólo para calibrar, identificado en la evidencia;
no modifica la receta ni cambia el backend del servicio activo. Expertos en host,
modo de carga, contexto y otros argumentos del preset condicionan la calibración.
La compatibilidad requiere evidencia de flags, trazas y logs, no sólo un nombre de build.

La preparación, calentamiento y calibración se distinguen. Se informa avance real,
se permite cancelar y se preservan errores de procesos y lectura. La carga sobre
el equipo y cualquier interferencia con servicios en uso requieren autorización
explícita; obtener una recomendación no autoriza detener procesos ajenos.

## Evidencia conservada

La calibración conserva configuración de origen, backend y modelo identificados,
mediciones, trazas/logs necesarios y recomendación o diagnóstico. La evidencia puede
residir en ámbito local de proyecto o global explícito; ese ámbito no importa
recursos ni determina por sí solo qué receta se edita. Se conserva localmente,
sujeta a [privacidad](privacidad.md) al compartirla, sin credenciales.
Una cancelación o fallo distingue datos parciales de una recomendación válida.
No se deduce compatibilidad ni rendimiento universal de una ejecución exitosa.

## Recomendaciones y receta

Una recomendación identifica el preset, la configuración de partida, los parámetros
propuestos y su evidencia. Calcularla no modifica la receta ni publica cambios.
Una recomendación conservada puede usarse después para aplicar una propuesta
al preset. El destino de la edición se identifica por separado del ámbito donde
se guardó la medición; guardar globalmente no autoriza editar cualquier proyecto.
La operación muestra los parámetros aplicables y la diferencia frente a la receta
actual antes de confirmar, sin reemplazarla íntegramente por el snapshot de la corrida.
Si no hay una propuesta válida aplicable, se informa sin convertir métricas en
parámetros ni generar cambios implícitos.
El ajuste de caché MoE propone tamaño y proporción CPU cuando el backend admite
esas capacidades. Su simulación no se presenta como rendimiento ya obtenido.

Si existe un proyecto editable, el usuario puede confirmar la actualización del
preset en la receta. Se muestra la combinación seleccionada, el archivo de destino
y la diferencia antes de guardar. Se respetan las reglas de
[edición por capas](definiciones-declarativas.md#combinación-y-rutas) y
[borradores y guardado](interfaces-visuales.md#borradores-y-guardado): la CLI escribe
en el último YAML seleccionado y la interfaz puede crear un override de diferencias.
No se escribe automáticamente en el archivo que originalmente aportó cada valor.
La composición resultante debe ser válida y reflejar la propuesta; los cambios
externos se resuelven antes de sobrescribir.

Sin proyecto editable, la recomendación sigue siendo consultable sin crear una
receta implícitamente. Guardar no publica el preset ni reinicia el servicio.
Aplicar al servidor requiere una acción explícita con las advertencias de
[interrupción](servidores.md#plazos-y-control). Se distinguen la configuración
medida, la recomendada, la guardada y la publicada.

## Selección para ajuste MoE

La categoría de la TUI se llama Ajuste fino MoE y se describe como «Calibrar la
caché de expertos y recomendar parámetros para el preset». Su operación CLI es
`tool moe-cache-tune`.

La categoría Ajuste fino MoE enumera presets, conservando la identidad de proyecto y
servicio cuando corresponda. Prioriza los que usan modelos MoE confirmados y
permite comprobar los que tengan evidencia inspeccionable todavía pendiente.
Cada comprobación actualiza el [índice de evidencia del modelo](modelos-y-companions.md#evidencia-de-arquitectura-y-expertos)
y refresca el estado de los presets que lo referencian, sin duplicar una
clasificación por cada preset.

También enumera y permite seleccionar los demás presets, incluidos modelos de
arquitectura desconocida o clasificados como densos. Muestra la clasificación y
su evidencia, pero no exige MoE confirmado para intentar el ajuste: una arquitectura
que Coquena todavía no interpreta puede ser compatible. La posibilidad de intentar
no certifica que el backend, la colocación de expertos o las trazas sean compatibles.

Los errores de preparación, ejecución, lectura de trazas y calibración se capturan
y se muestran en pantalla con su causa disponible y la etapa afectada. El fallo
no cierra la interfaz ni impide seleccionar otro preset. No se produce una
recomendación válida ni se modifica la receta a partir de una calibración fallida.
La compatibilidad del intento se registra separada de la clasificación del modelo;
un error de memoria, por ejemplo, no demuestra que el modelo sea denso.

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

Chat es una categoría de la pestaña Herramientas. El botón Chat de En vivo abre
esa categoría con el preset enfocado y su servicio ya seleccionados. El acceso
rápido conserva la identidad de la entrada; no crea otra pantalla de chat ni elige
otro preset por coincidencia de modelo. Requiere una selección de preset.

En CLI, `tool chat --preset <service>/<preset>` inicia la prueba con el servicio
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

## Referencia técnica

[moe-cache-tune.py](https://github.com/ggml-org/llama.cpp/blob/dd6b945be2188da875ab05a259ce94c6b2736e64/scripts/moe-cache-tune.py)
y [caché MoE](https://github.com/ggml-org/llama.cpp/blob/dd6b945be2188da875ab05a259ce94c6b2736e64/docs/moe-cache.md)
describen el procedimiento de referencia. Sus umbrales y formatos internos deben
identificarse y comprobarse al integrar cada backend; una simulación no sustituye
la evidencia de calibración ni acredita mejora en cualquier trabajo de agente.
