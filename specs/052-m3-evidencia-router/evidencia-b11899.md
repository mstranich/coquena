# Matriz ejecutada del router b11899

## Identificación y reproducción

Prueba del 2026-10-27, entorno anonimizado, Windows win-x64. Instalación oficial
local ggml-org/llama.cpp, release **b11899**, commit **c6fb3b5be**, versión
**0.7.0-dev**, variante **CUDA 12**. Las peticiones usan CPU explícitamente con
`n-gpu-layers=0`, dos threads y un slot por entrada: no se acredita offload CUDA,
compatibilidad de otras variantes ni un rendimiento mínimo de hardware.

SHA-256 del ejecutable ensayado:
`cf890aa27af7fcaae72870ef0dad32ed49a387dbe392b459ca40d456fd0c1b50`.
Identifica ese archivo local; no sustituye una firma o la verificación del paquete.

Recursos locales acordados: `unsloth/gemma-4-E4B-it-qat-GGUF`, archivo
`gemma-4-E4B-it-qat-UD-Q4_K_XL.gguf`, y `ornith-ai/Ornith-1.5-9B-GGUF`, archivo
`Ornith-1.5-9B-Q4_K_M.gguf`. Se usan in situ sin adopción, modificación ni descarga.
Los nombres publicados son `consumer-a.chat` y `consumer-b.chat`.

El [protocolo opt-in](../../tests/Coquena.Tests/RouterEvidenceTests.cs) crea routers
independientes en puertos loopback libres y registra identidad de cada proceso,
HTTP con inicio/fin UTC, snapshots, eventos y logs con timestamp de recepción.
No se conecta a servicios existentes. El directorio de evidencia debe estar vacío;
los archivos crudos quedan fuera de Git porque contienen rutas y datos del equipo.
Los timestamps de recepción no sustituyen el momento interno del evento.

Desde la raíz del repositorio, usar el runner Native AOT publicado según la
[tarjeta](README.md), definir rutas locales en `$BackendExe`, `$ModelA`, `$ModelB`
y `$EvidenceOutput`, y ejecutar:

```powershell
& (Join-Path $TestsOutput 'Coquena.Tests.exe') --router-evidence $BackendExe $EvidenceOutput $ModelA $ModelB
```

El prompt es exclusivamente sintético: 40 repeticiones de
`Synthetic router evidence: red green blue one two three. ` seguidas de `Continue:`.
Peticiones `/completion`, seed 52, temperature 0, `ignore_eos=true`, cache habilitada,
salidas de 96 tokens, repetición de 16 y candidata interrumpida de hasta 512.
Contextos de 1024, 1536 y 2048 según escenario. Se muestrean slots/métricas cada
200 ms después de completar las consultas, sin prometer esa cadencia al producto.

## Control, publicación y fallos

| Escenario ejecutado | Evidencia y límite |
| --- | --- |
| Arranque vacío | INI `version=1`: health 200 y listado vacío. No se exige inferencia ni un preset. |
| Codec | Secciones con punto/guion; ruta absoluta y string con espacios internos, `=`, comillas simples/dobles, backslash y Unicode conservados en tokens del hijo. No se añaden comillas de shell. |
| Valores no representables | El codec rechaza `;`, `#`, espacios exteriores, salto de línea y NUL antes de publicar. No se afirma soporte universal de strings o claves de forks. |
| Precedencia | Preset 1024 prevalece sobre global 768 e hereda threads; CLI 2048 prevalece sobre preset 1024. Se contrasta la configuración cargada con `n_ctx` en slots. |
| Dos consumidores | Alta, cambio y retirada mediante reescritura/recarga conservan las definiciones no afectadas. Publicar deja entradas `unloaded`. |
| Observación sin carga | `/metrics`, `/slots`, `/props` con `model` y `autoload=false` devuelven 400 para la entrada no cargada; el listado confirma que no se cargó. |
| Warmup explícito | `/models/load` acepta y se espera `loaded` antes de inferir. Ese primitive permite solicitar warmup; no implementa todavía lectura del campo YAML ni publicación de producto. |
| Warmup y recarga | Añadir en recarga una entrada con `load-on-startup=true` la deja `unloaded`. Esa opción de INI no sustituye la carga explícita al publicar. |
| Fallo tardío | Archivo inexistente: carga responde 200 con `success=true`; posteriormente `unloaded`, `failed=true`, exit 1. No se confunde publicación/aceptación con carga o inferencia. |
| Pérdida de resultado | El fixture envía una recarga y deliberadamente no lee su respuesta; una consulta `/models` confirma el cambio sin reenviar la mutación. Acredita este caso controlado, no toda falla de red. |
| Concurrencia | Dos peticiones a entradas de modelos distintos se superponen; snapshots muestran ambos slots activos y ambas respuestas finalizan 200 con 96 tokens cada una. |
| Cola real | Tres peticiones a una entrada con un slot: métricas exponen peticiones diferidas y todas finalizan 200. No se establece una política propia de espera o forzado. |
| Recarga en curso | Cambiar la entrada activa interrumpe su petición con HTTP 500 (`proxy error`); la entrada del otro consumidor sigue cargada. No se acredita drenaje ni atomicidad remota. |
| Restauración funcional acotada | Tras publicar una ruta inválida y observar fallo de carga, restaurar sólo la entrada afectada, recargar y cargar explícitamente permite inferencia 200 sin reiniciar router. La entrada ajena permanece. |
| Reinicio e identidades | Nueva instancia explícita reutiliza slot 0; en el primer recorrido completo se observó también tarea 1 antes y después del reinicio. En la repetición final la primera tarea muestreada fue 2: el número depende también de las consultas, no es una secuencia exclusiva de inferencias. Recargar una entrada reemplaza su hijo sin reemplazar el router y puede volver a usar tareas desde 0. |
| Opción fijada por CLI | Un `ctx-size=3072` en INI no cambia el 2048 impuesto por el proceso. Se diagnostica necesidad de reinicio explícito si se pretende cambiar ese valor; el ensayo no lo ejecuta automáticamente. |
| Métricas/slots desactivados | Ambos responden 501 `not_supported_error`, mientras health del router sigue en 200. No se presenta ausencia como cero ni caída. |

### Comprobación adicional de stop-timeout

Ante la consulta sobre encolar recargas, se contrastó el cierre del hijo en el
código b11899 y se ejecutaron dos pruebas aisladas adicionales con Gemma 4 E4B,
CPU, dos threads y un slot. En cada prueba el INI inicial fija `stop-timeout` a
10 o 60 segundos; se carga explícitamente la entrada, se pide una generación
sintética de 128 tokens con `ignore_eos=true` y, tras observar dos tokens de salida
en `/slots`, se cambia `ctx-size` de 1024 a 1536 y se solicita `/models?reload=1`.

| stop-timeout inicial | Recarga | Petición de generación | Logs |
| --- | --- | --- | --- |
| 10 segundos, default documentado | 200 | 500, `proxy error: Failed to read connection` | Hijo recibe exit e inicia limpieza; router fuerza terminación al vencer plazo. |
| 60 segundos | 200 | 500, mismo error | Mismo cierre y terminación al vencer el plazo ampliado. |

El plazo permite salir al proceso antes de terminarlo por la fuerza; no encola
la recarga detrás de la generación. En estas pruebas ampliarlo prolongó la espera
sin completar la respuesta. `unload()` solicita detener el hijo y la recarga
espera su estado unloaded; el shutdown del hijo termina la cola de ejecución.
No se acredita un parámetro de drenaje ni preservación de peticiones en curso.
La cola de inferencias cuando faltan slots no demuestra cola de operaciones de
recarga. Scripts y logs crudos de estas pruebas permanecen como evidencia local.

Fuente adicional:
[cierre del servidor b11899](https://github.com/ggml-org/llama.cpp/blob/b11899/tools/server/server.cpp).
No se cambió código de producto ni se eligió una política no interactiva a partir
de este resultado.

## Matriz de fuentes para el dashboard

Todas las filas de backend corresponden al build/plataforma y fixtures anteriores.
La granularidad de métricas es **proceso hijo de una entrada**, no servicio entero
ni tarea individual. Una recarga que reemplaza ese hijo reinicia sus acumulados.
Las consultas usan siempre `model=<entrada>&autoload=false`.

| Cifra o fase | Fuente pública/campo reconocido | Unidad, ventana y granularidad | Resultado y representación |
| --- | --- | --- | --- |
| Salud del servicio | GET `/health`, estado HTTP | Instantánea del router | 200 incluso con entrada fallida. Mostrar salud y fallo de entrada separadamente. |
| Publicación/carga | GET `/models`, `id`, `status.value`, `failed`, `exit_code`, `args` | Instantánea por entrada y generación del hijo | `unloaded`, `loading`, `loaded` y fallo observados. Publicado no acredita inferencia. |
| Eventos de modelos | GET `/models/sse`, `event`, `model`, `data.status`, `data.progress` | Evento por entrada; progreso 0..1 de etapa de carga | Se observaron `model_status`, `status_change`, `models_reload`. No sólo el ejemplo `model_status` del README. No aparecen contadores de tokens ni tasas de tarea en este flujo. |
| Slot y tarea | GET `/slots`, `id`, `id_task`, `is_processing` | Instantánea por hijo/slot/tarea | Ambos modelos usan slot 0; se observó reutilización de tarea tras reinicio, sin garantizar un primer número fijo. Vincular instancia de router, entrada y generación del hijo; el número solo no identifica una ejecución. |
| Espera/cola | `/metrics`, `llamacpp:requests_deferred` | Peticiones diferidas agregadas al instante por hijo | Se observaron 1 y 2. No proporciona identidad individual de cada petición en espera; no fabricar filas de tareas a partir del contador. |
| Actividad | `/metrics`, `llamacpp:requests_processing` | Slots procesando al instante por hijo | 1 durante las peticiones. No distingue por sí solo prompt y generación. |
| Prompt procesado | `/slots`, `n_prompt_tokens_processed` | Tokens realmente procesados por tarea, hasta snapshot | Se observa antes y durante generación; al finalizar el primer prompt, 443. No incluye tokens reutilizados. |
| Caché del prompt | `/slots`, `n_prompt_tokens_cache`; respuesta `timings.cache_n` | Tokens reutilizados por tarea | Repetición: 438 cacheados y 5 procesados. La respuesta `tokens_cached` contiene estado de caché del slot al terminar y no es el mismo concepto. |
| Tamaño interno de secuencia | `/slots`, `n_prompt_tokens` | Tokens de `prompt.tokens`, instantáneo por slot | Cambia mientras se genera. No utilizar como denominador fijo del prompt ni deducir progreso por su nombre. |
| Progreso/fase de prompt | Log `slot print_timing`, `id … | task … | prompt processing, n_tokens, progress, t` | Fracción redondeada, segundos y tokens/s hasta ese punto, por tarea | Se observó `progress=0.99` durante el primer prompt. Es un reporte por batches, no una serie garantizada ni una ETA. Sin muestra válida: progreso no disponible. |
| Tokens de salida | `/slots`, `next_token[0].n_decoded` | Tokens generados por tarea al instante | Aumenta durante generación; 96 en respuesta final `timings.predicted_n`. No requiere leer contenido de conversaciones. |
| Tasa de prompt agregada | `/metrics`, `llamacpp:prompt_tokens_seconds` | Tokens/s sobre bucket acumulado del hijo desde inicio/último reset | Disponible durante ejecución; no es tasa instantánea ni de una tarea arbitraria. El protocolo no solicita reset. |
| Tasa de salida agregada | `/metrics`, `llamacpp:predicted_tokens_seconds` | Pasos de decode/s sobre bucket del hijo | Disponible durante ejecución; excluye el primer token obtenido de logits del prompt. No equivale a `tokens_predicted_total / segundos` sin atender esa definición. |
| Acumulados de prompt/caché | `/metrics`, `llamacpp:prompt_tokens_total`, `prompt_tokens_cached_total`, `prompt_seconds_total` | Tokens procesados/reutilizados y segundos desde inicio del hijo | Campos presentes en snapshots; diferenciar procesados y cacheados. No sumar a respuestas por tarea como otra actividad adicional. |
| Acumulados de salida | `/metrics`, `llamacpp:tokens_predicted_total`, `tokens_predicted_seconds_total` | Tokens y segundos acumulados desde inicio del hijo | Campos presentes; pueden actualizarse por batch/etapa. No identifican petición ni cliente. |
| Tiempos/tasas por tarea terminada | Respuesta `/completion`, `timings.prompt_ms`, `predicted_ms`, `prompt_n`, `predicted_n`, `prompt_per_second`, `predicted_per_second`; logs `prompt eval time`, `eval time`, `total time` | Milisegundos, tokens y tokens/s de esa tarea/fase | Contraste entre respuesta y logs. Los valores finales no están expuestos como timings de tarea en `/models/sse`. No se obtienen leyendo peticiones de clientes ajenos. |
| Finalización | `/slots.is_processing=false` y log `release … stop processing`; resultado de petición sintética | Transición y último estado por tarea | Polling puede perder tareas cortas y el slot conserva datos de la anterior. La finalización del slot no acredita éxito HTTP ni causa de parada; conservar incertidumbre sin respuesta/log suficiente. |
| Tiempo hasta primer token | No hay timestamp público específico en `/slots` o `/models/sse` observado | No disponible como medida exacta por tarea ajena | Timestamp del primer snapshot con salida sería una observación tardía, no TTFT exacto. No mostrarlo como medida del backend. |
| Tiempo restante | Sin fuente acreditada | Estimación no acreditada | Mostrar no disponible. No extrapolar ETA como tiempo medido. |
| CPU/RAM por servicio | Sin campos acreditados en estos endpoints | Medición del SO separada, por procesos verificados | No disponible desde este backend. El probe no valida instrumentación de hardware del dashboard ni atribuye toda memoria al router padre. |
| GPU/VRAM por dispositivo/proceso | Sin campos acreditados en estos endpoints/SSE | Fuente de sistema separada, alcance dispositivo/proceso | No disponible en esta evidencia. No inventar cero ni atribuir totales de GPU a cada entrada. |

## Correlación y límites para las siguientes entregas

El router reenvía los logs de cada hijo con prefijo de **puerto**, no PID. Se
contrasta con `status.args` del mismo período para asociar puerto a entrada y con
la identidad del router registrada por el protocolo. Un puerto puede reutilizarse:
la asociación no se conserva indefinidamente ni convierte la tarea en identidad
global. El prefijo del nombre publicado tampoco identifica al cliente que pidió
inferencia. La evidencia local conserva los snapshots para contrastar esas épocas.

Hay progreso y salida observables en vivo, con granularidad y huecos propios de
cada fuente. Los logs de prompt son discretos; un prompt corto puede terminar sin
una muestra intermedia. Las tasas Prometheus son buckets del hijo, mientras que
los timings finales son de una petición. Mezclarlos produciría cifras engañosas,
especialmente con concurrencia o después de recargar un preset.

Diagnósticos visibles propuestos para los consumidores de esta evidencia:
`no cargado`, `métricas desactivadas`, `slots desactivados`, `fuente no disponible`,
`carga aceptada, resultado pendiente`, `carga fallida`, `resultado de recarga
incierto`, `reinicio explícito necesario` y `correlación insuficiente` según la
prueba/observación disponible. Son representaciones de los límites encontrados;
esta tarjeta no implementa la UI, el ciclo de servicios ni un adaptador nuevo.

No se prueba un dashboard integrado o una terminal visual. No se adoptan paquetes,
no se cambia el contrato funcional y no se garantiza soporte de forks. Esta
evidencia condiciona correlación/contadores de M3 y evita que editores/dashboards
de M4 presupongan eventos completos, IDs globales o recarga sin interrupciones.

## Fuentes fijadas

- [API, presets y routing b11899](https://github.com/ggml-org/llama.cpp/blob/b11899/tools/server/README.md).
- [Router, recarga y prefijos de logs b11899](https://github.com/ggml-org/llama.cpp/blob/b11899/tools/server/server-models.cpp).
- [Parser INI b11899](https://github.com/ggml-org/llama.cpp/blob/b11899/common/preset.cpp).
- [Slots, progreso y métricas por batch b11899](https://github.com/ggml-org/llama.cpp/blob/b11899/tools/server/server-context.cpp).
- [Serialización de métricas y respuestas b11899](https://github.com/ggml-org/llama.cpp/blob/b11899/tools/server/server-task.cpp).
- [Buckets y timings b11899](https://github.com/ggml-org/llama.cpp/blob/b11899/tools/server/server-common.h).

La lectura de estas fuentes interpreta las muestras ejecutadas; no sustituye esas
pruebas. Se omiten hardware concreto y cifras de rendimiento del equipo: no es
evidencia comparativa de capacidad ni benchmarking reproducible entre máquinas.
