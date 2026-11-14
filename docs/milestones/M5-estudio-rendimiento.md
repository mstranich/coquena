# M5 — Estudio conceptual de referencias

## Estado del estudio tras la retirada de calibración

El usuario confirmó que la calibración resultó inviable y decidió conservar sólo
Chat en M5. Las antiguas tarjetas 095–099 se eliminaron; Chat pasó de 100 a 095. Las asociaciones de números anteriores a la renumeración y
propuestas que aparecen abajo son históricas: no describen tareas vigentes ni
reglas del producto. La lectura estática de este estudio no es evidencia del
experimento de inviabilidad ni permite atribuirle una causa técnica.
El plan vigente está en [M5](M5-rendimiento.md).

## Alcance y evidencia

Lectura estática de los clones de referencia, sin ejecutar benchmarks, iniciar
servidores ni modificar esos repositorios. No acredita disponibilidad en los
backends instalados, compatibilidad de un port a C# ni rendimiento de Coquena.
Alimenta los borradores de [M5](M5-rendimiento.md); no autoriza implementar todos
los mecanismos encontrados. Las reglas funcionales están en
[benchmarking](../design/benchmarking.md).

Versiones examinadas:

- llama.cpp: `dd6b945be2188da875ab05a259ce94c6b2736e64`.
- llama-throughput-lab: `60bf3e88170ca97c4f4a0dd2e57e64ea200e5599`.

## Qué hace cada referencia y qué aporta

| Referencia | Procedimiento observado | Aplicación al borrador |
| --- | --- | --- |
| `tools/llama-bench/llama-bench.cpp` | Carga modelo/contexto; ejecuta pp, tg o pg con tokens sintéticos, calentamiento y repeticiones; barre productos de parámetros; emite muestras, medias y desviación en JSON/JSONL, entre otros formatos. | 096: separar tipos de prueba, profundidad de contexto, muestras y condiciones. Invocar la herramienta del backend en lugar de migrar su motor C++. |
| `tools/batched-bench/batched-bench.cpp` | Evalúa longitudes de prompt/salida y número de secuencias con opciones de prompt compartido y generación separada; emite tabla o JSONL de tiempos y tasas. | 096: candidato para estudiar paralelismo interno sin HTTP. Su JSONL y su canal de salida tienen contrato propio; no asumir el de llama-bench. |
| `scripts/server-bench.py` | Genera carga sintética o usa preguntas MMLU; aplica plantilla/tokeniza cuando corresponde; envía peticiones concurrentes con streaming, recoge tiempos de llegada y resume tasas/latencia. Puede conectarse por URL o iniciar proceso. | 097: separar fuente de carga, concurrencia y métricas del cliente. Portar el procedimiento seleccionado, no su dependencia de datasets, gráficos o SQLite por defecto. |
| `scripts/compare-llama-bench.py` | Agrupa resultados por propiedades de prueba, empareja builds y calcula ratios. Admite varios formatos; promedia tasas de corridas sin ponderar por repeticiones. | 098: baseline explícita, condiciones y muestras sin pareja visibles. La política de agregación de Coquena requiere definición propia. |
| `scripts/moe-cache-tune.py` | Calibra dos tamaños de caché por tres proporciones CPU; recoge timings, logs y trazas; ajusta un modelo de tiempo y reproduce el enrutamiento con lógica de caché para predecir otras configuraciones. | 099: tratarlo como calibración más simulación, conservar error del ajuste y requisitos de compatibilidad. |
| throughput-lab: prueba simple y concurrente | Envía peticiones no streaming a `/completion`; extrae tokens/timings y distingue tasa interna por petición de tokens totales divididos por duración del lote. | 097: prueba secuencial y barrido de concurrencia sobre una carga explícita, con errores y límites. |
| throughput-lab: barrido de hilos | Arranca un proceso por variante de `--threads`/`--threads-http` y compara throughput de peticiones concurrentes. | 097: diferenciar un barrido de configuración, con gestión de procesos, de un barrido de carga. Su ciclo no se copia sobre el servicio activo. |
| throughput-lab: round-robin/full sweep | Inicia varios servidores, los pone detrás de nginx y barre instancias, paralelismo, concurrencia y tamaños de lote. Guarda filas incrementalmente. | 097: conservar matriz y resultados parciales para pruebas directas. El montaje con balanceador no se incorpora al mapa de producto. |

`tool_bench.py` evalúa éxito de llamadas a herramientas con distintas temperaturas
y backends; `server-test-model.py` prueba respuestas, streaming y llamadas a
herramientas. Sus objetivos incluyen comportamiento funcional. No se incorporan
como un benchmark de velocidad ni como evaluación automática de calidad del chat.

## Round-robin: referencia descartada como topología de producto

En throughput-lab, nginx expone un endpoint delante de varios procesos
llama-server y distribuye las peticiones por turnos entre sus upstreams. Cada
proceso se inicia con el modelo y sus propios argumentos; no es selección de
presets dentro de un único router. El estudio compara capacidad de una topología
de procesos frente a otra, variando instancias, slots y concurrencia de clientes.

Puede revelar si distribuir trabajo mejora la utilización o reduce una limitación
por proceso. También puede multiplicar contextos, buffers y asignaciones de GPU,
y provocar competencia por recursos. El consumo físico no se deduce multiplicando
el tamaño del GGUF: memoria de archivo puede compartirse según plataforma/modo,
mientras otras asignaciones pertenecen a cada proceso. No hay evidencia de mejora
real sobre el equipo del usuario porque no se ejecutó el experimento.

Por decisión del usuario, nginx queda fuera del mapa de producto, sin reserva para
otra entrega. La descripción anterior explica la fuente; no es una propuesta ni
una decisión abierta. Tampoco se adopta un balanceador propio desde C# como sustituto.

Para 097, se conserva la parte conceptual independiente: carga secuencial y
concurrente contra un servicio, matrices de configuración y resultados incrementales.
El número de réplicas detrás de un proxy deja de ser una dimensión del barrido.
Comparar por separado corridas de servicios distintos sigue siendo comparación
de resultados, sin requerir balanceo entre destinos. Esto evita introducir instalación,
configuración y ciclo de vida de un proxy y un conjunto de procesos réplicas.

## Límites que cambian el diseño

### Pruebas conservadas y pruebas que se pierden

| Experimento de referencia | Tratamiento sin balanceador |
| --- | --- |
| Petición simple y peticiones concurrentes | Se conserva el procedimiento contra el servicio seleccionado. |
| Barrido de hilos de inferencia y HTTP | Se conserva como referencia de configuración; exige definir preparación y ciclo de las variantes. |
| Round-robin entre varios llama-server | Se descarta el experimento de reparto por proxy y su throughput agregado. |
| Round-robin sweep: longitud de salida por concurrencia | Se conservan esos ejes con destino directo; se pierde su evaluación sobre réplicas balanceadas. |
| Full sweep: instancias por paralelismo, lote, microlote y concurrencia | Se conservan los ejes de un servicio; se retiran número de réplicas y medición del conjunto detrás del proxy. No es una ejecución equivalente del script original. |

Se pierde la comparación de escalabilidad horizontal mediante réplicas balanceadas,
incluidos sus costes de proxy y contención entre procesos. Se puede estudiar la
saturación de un servicio con carga concurrente y comparar variantes por separado;
eso no acredita cómo rendirían esas variantes funcionando juntas. Las mediciones
sin servidor, el ajuste MoE y el laboratorio Chat no necesitan ese balanceador.

### Las pruebas sintéticas no reproducen un chat

En llama-bench tanto el prompt como los siguientes tokens de la prueba de generación
se construyen con identificadores aleatorios. Permite aislar coste del motor, pero
no ejecuta una conversación con muestreo y plantilla representativos. Batched-bench
añade secuencias dentro del contexto, no clientes HTTP ni un router. Sus cifras
no deben presentarse como capacidad medida del laboratorio interactivo (095/100).

### Streaming: eventos y tokens son entidades diferentes

`server-bench.py` acumula una fecha por línea `data:` y elimina eventos finales
según su estructura antes de calcular cantidades y tiempos. Eso depende del contrato
de emisión. En Coquena se debe identificar primer contenido, metadatos y cierre,
sin inferir un token por fragmento. Un total de usage al terminar no acredita la
distribución temporal de tokens. Esta restricción afecta a 097 y al chat de 100.

### El ganador depende del objetivo y de los fallos

El full sweep registra errores, pero selecciona el mejor por throughput sin exigir
cero errores. Las funciones de extracción retornan cero cuando faltan tokens o
timings, y sus lanzadores descartan stdout/stderr del servidor. Son mecanismos de
experimento, no políticas a trasladar: Coquena ya distingue ausencia de datos,
fallo y cero medido. 097/098 deben mostrar cobertura y errores; quedan por acordar
ranking, agregación entre repeticiones y umbrales de diferencia.

### La recomendación MoE tiene una carga y una política concretas

El script usa un prompt fijo, genera dos respuestas por calibración, aprovecha
trazas de expertos y estadísticas al salir, y simula caché caliente. Selecciona la
menor proporción CPU dentro del 5% de la más rápida por tamaño y recomienda el mayor
tamaño evaluado permitido. Rechaza el ajuste si no es físico o su error excede 10%.
Estos umbrales y preferencias son de la referencia, no decisiones aprobadas para
Coquena. Falta definir carga representativa, límite de memoria, aceptación del modelo
y validación real de la propuesta antes de convertirlos en criterios de 099.

La caché exige expertos en memoria host; su comportamiento depende del modo de
carga, distribución de expertos y memoria disponible. Soportar los flags no acredita
compatibilidad con la traza binaria `MCT1`, el texto de logs ni la lógica de caché
reproducida. El script inicia procesos y usa SIGINT para obtener estadísticas;
Windows requiere diseñar y acreditar finalización y recuperación propias.

### Un laboratorio conoce su petición, pero no todos sus slots

En Coquena, `InferenceTasks` identifica tareas por servicio, instancia, entrada,
proceso, slot y tarea observados. La salida del slot no acredita el resultado HTTP;
`InferencePresentation` distingue intervalos observados de latencia completa.
El cliente del laboratorio puede medir su petición y mostrar su respuesta, pero
atribuirle una tarea del monitor exige correlación demostrable. CPU/GPU de máquina
y acumulados de entrada siguen siendo contexto compartido. 100 debe probar también
el caso con tráfico ajeno y con fuentes de observación ausentes.

## Fuentes comprobadas

- [llama-bench: código](https://github.com/ggml-org/llama.cpp/blob/dd6b945be2188da875ab05a259ce94c6b2736e64/tools/llama-bench/llama-bench.cpp) y [guía](https://github.com/ggml-org/llama.cpp/blob/dd6b945be2188da875ab05a259ce94c6b2736e64/tools/llama-bench/README.md).
- [batched-bench](https://github.com/ggml-org/llama.cpp/blob/dd6b945be2188da875ab05a259ce94c6b2736e64/tools/batched-bench/batched-bench.cpp).
- [server-bench](https://github.com/ggml-org/llama.cpp/blob/dd6b945be2188da875ab05a259ce94c6b2736e64/scripts/server-bench.py).
- [Comparador](https://github.com/ggml-org/llama.cpp/blob/dd6b945be2188da875ab05a259ce94c6b2736e64/scripts/compare-llama-bench.py).
- [Ajuste MoE](https://github.com/ggml-org/llama.cpp/blob/dd6b945be2188da875ab05a259ce94c6b2736e64/scripts/moe-cache-tune.py) y [contrato de caché](https://github.com/ggml-org/llama.cpp/blob/dd6b945be2188da875ab05a259ce94c6b2736e64/docs/moe-cache.md).
- [Pruebas de throughput-lab](https://github.com/alexziskind1/llama-throughput-lab/tree/60bf3e88170ca97c4f4a0dd2e57e64ea200e5599/tests) y [full sweep](https://github.com/alexziskind1/llama-throughput-lab/blob/60bf3e88170ca97c4f4a0dd2e57e64ea200e5599/scripts/full_sweep.py).
