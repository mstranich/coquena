# Prueba de uso personal y publicación 0.2.0

Las referencias Git de Coquena de este documento apuntan a sus equivalentes en el historial saneado. Los hashes de modelos y backends externos se conservan.

El hardware del entorno de prueba se ha anonimizado. Los resultados conservados describen esa ejecución y no garantizan el mismo rendimiento en otro equipo.

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

Prueba manual autorizada el 16 de octubre de 2026, posterior a la aceptación M2.
Objetivo: preparar dos modelos Ornith para programación, comprobar inferencia y
tool calling mediante la API de llama.cpp y publicar el entregable win-x64.
La conexión a un harness real es un paso posterior; estas consultas no la certifican.

## Entorno y fuentes

- `COQUENA_HOME` de proceso y usuario: `${CoquenaHomeLower}`, equivalente en Windows a
  `${CoquenaHome}`. Estado, descargas y evidencia local se mantienen en D:.
- CPU de prueba, capacidad de memoria anonimizada y GPU de prueba (memoria anonimizada).
- La configuración de prueba no fija afinidad automáticamente a una clase de núcleos.
- [Ornith 9B GGUF](https://huggingface.co/ornith-ai/Ornith-1.5-9B-GGUF),
  commit `abdd624b12ebf020b767fff532ff44fe552b28c3`:
  `Ornith-1.5-9B-Q4_K_M.gguf`, 5 780 090 816 bytes.
- [Ornith 35B](https://huggingface.co/ornith-ai/Ornith-1.5-35B-A3B) enlaza su
  [GGUF oficial](https://huggingface.co/ornith-ai/Ornith-1.5-35B-A3B-GGUF),
  commit `12393612fd4f730ff5aadc23e9b8f9648aa49ceb`:
  `Ornith-1.5-35B-Q4_K_M.gguf`, 21 713 463 040 bytes.
- [Servidor llama.cpp](https://raw.githubusercontent.com/ggml-org/llama.cpp/refs/heads/master/tools/server/README.md),
  contrastado con `--help` del binario instalado `b11696`, CUDA 12.4.

Los README consultados y la ayuda efectiva se conservan localmente en
`${CoquenaHome}/validation/release-0.2.0/sources` y
`${CoquenaHome}/ornith-llama-help.txt`. Las recomendaciones de las fichas son
referencias de configuración, no evidencia de optimalidad en esta máquina.

## Configuración solicitada

Servidor `ornith`, loopback `127.0.0.1:9931`, backend propio
`ggml/cuda@12:latest` resuelto a `b11696`, `models-max=1`.
Presets con nombres identificables `Ornith-1.5-9B` y `Ornith-1.5-35B-A3B`.
Contexto nativo máximo explícito: `ctx-size=262144`; sin extensión YaRN.
Un slot, Flash Attention y cachés K/V Q8_0 como configuración inicial a comprobar.
Muestreo: temperatura 0.6, top-p 0.95, top-k 20. Para el 9B, min-p 0,
presence-penalty 0 y repeat-penalty 1 conforme a su receta de programación.
El MoE usará ocho hilos de generación y procesamiento de prompts por indicación
del usuario; la colocación de expertos en CPU es una opción independiente.

Las definiciones adicionales se crean mediante `param define` y su soporte se
declara únicamente para el backend instalado y build comprobado. No se cambia el
catálogo de fábrica del código ni se presume soporte en releases futuros.

## Evidencia de ejecución

- Publicación Native AOT win-x64 de producto y runner, versión 0.2.0.
- Suite completa nativa: exit 0, usando temporales en D: y consola UTF-8.
  Logs en `${CoquenaHome}/validation/release-0.2.0/checks-utf8`.
- 9B: carga real con `n_slots=1`, `n_ctx_slot=262144`.
- 35B base y con companion: carga real con `n_slots=1`,
  `n_ctx_slot=262144`; pool CPU de ocho hilos confirmado por el log.
- Consulta Python `unique_in_order`: respuesta final con función y dos asserts;
  567 tokens generados, 44.51 tokens/s según timings del servidor. Es una muestra
  corta; no mide calidad general ni prueba ocupación completa del contexto.
- Consulta de herramienta: respuesta API con `tool_calls` para `read_file`.
  La herramienta no se ejecutó contra archivos del repositorio.
- El código Python generado se inspeccionó y ejecutó: pasan sus dos asserts
  y dos comprobaciones adicionales (vacío y mezcla de enteros/listas).

### Paquete preparado para publicación autorizada

`${CoquenaHome}/validation/release-0.2.0/coquena-0.2.0-win-x64.zip`,
5 086 154 bytes, con coquena.exe, README.md y LICENSE.
SHA-256 ZIP: `d184d47011b09f567ee59ec9881e6696a9d85adcea1228c5a73fd7fc9c32444c`.
SHA-256 EXE publicado y extraído:
`b2716ed095464da3716122c84ac955327af48028b7958929c25f56da62e80deb`.
Publicación con `-p:InformationalVersion=0.2.0`; producto fuente `ad0b61a`.
Suite completa contra el EXE extraído: exit 0, logs `extracted-checks`.
El paquete no incluye modelos, backend, estado local ni runner de pruebas.
El binario se construyó y probó antes del commit documental de publicación.
Se comprobó que las fuentes de runtime, pruebas y global.json no cambiaron respecto
de ad0b61a; no se presenta como una recompilación posterior al tag.

## Incidencias observadas

1. Instalación CUDA: primera publicación falló con `transfer-io-failed` después
   de transferir 658 237 903 bytes. `download resume` completó instalación y
   limpieza sin descargar otra vez. Causa raíz no establecida.
2. Descarga 35B: `checkpoint-inconsistent` tras confirmar 3 989 028 454 bytes;
   el parcial medía 4 016 292 784 bytes. Reanudar rechazó reutilizarlo. Se conservó
   el JSON de evidencia, se canceló mediante CLI y se inició otra descarga.
   No se editaron checkpoints ni se publicaron bytes sin verificar.
   La segunda operación se interrumpió con `writer-busy` durante cambios y
   reinicios del servidor. El parcial de 10 739 737 358 bytes coincidía con el
   checkpoint y `download resume` continuó desde allí. Es evidencia de contención,
   no una causa raíz demostrada del primer desajuste.
3. Procedimiento de pruebas: ruta con capitalización distinta falla en una
   comparación textual del test de backend externo. Con la capitalización real
   supera esa comprobación. Consola no UTF-8 falla en la comprobación de logs;
   con `chcp 65001` la suite completa pasa. No se modificó código para ocultarlo.
4. Selección de companion: `#mtp:Q8_0` en el repositorio EryriLabs ofrece el
   principal de 36.9 GB cuyo nombre contiene el token MTP, pero omite el draft
   `mtpdraft-Q8_0.gguf`. Se canceló sin descargar ese principal. El resolver
   reconoce `mtp` como token delimitado; `mtpdraft` no coincide. Se eligió el
   companion separado de a4lg en lugar de editar la selección persistida.

Las incidencias de transferencia merecen evaluación antes de considerar robusto
el uso cotidiano. También afectan al futuro flujo de preparación declarativa de
M3, que depende de descargas y diagnósticos recuperables. El informe inicial no
autorizó crear tarjetas ni implementar correcciones. Las tarjetas creadas después
por solicitud expresa del usuario se relacionan al final de este documento.

**Decisión inicial después de los hallazgos:** investigar y reportar, sin corregir
el código, y retener la publicación hasta nueva aprobación. Más tarde el usuario
autorizó expresamente commit, tag, push y release con binario Windows, considerando
la prueba parcialmente exitosa. Las correcciones siguen sin implementar.

### Investigación de contención: mecanismo reproducido

Un diagnóstico local, sin cambios al producto y con home aislado, usa
`DownloadManager.RunAsync` y un transporte de memoria de 131072 bytes.
Antes de entregar el primer bloque retiene `.writer.lock` en otro hilo.

| Retención del escritor | Resultado de transferencia | Parcial/checkpoint | Consulta y reanudación |
|---|---|---|---|
| 2000 ms, control | Finaliza transferencia | 131072/131072 | `verifying`, sin diagnóstico |
| 8500 ms, dos ejecuciones | `StorageException`, `writer-busy` | 65536/0 | `failed`, `checkpoint-inconsistent`; resume sigue fallido |

Mecanismo comprobado en el código actual:

1. `LocalStore.Acquire` abandona tras 3000 ms de contención.
2. La transferencia escribe bytes antes del checkpoint. `Checkpoint` hace flush
   y después guarda `ConfirmedBytes` mediante el mismo escritor global.
3. Si tanto el checkpoint periódico como el del `finally` pierden ese bloqueo,
   `StorageException` escapa; no está entre las excepciones tratadas por el catch
   de transferencia. El JSON puede seguir en `downloading` con un offset anterior.
4. Al terminar el ejecutor, `Status` compara longitudes y clasifica correctamente
   el desajuste como `checkpoint-inconsistent`; `RunAsync` no reutiliza esos bytes.

El rechazo de bytes no confirmados protege la integridad. El problema observado
es que una contención ordinaria puede causar pérdida de reanudabilidad y obligar
a cancelar/redescargar. No se demostró corrupción de un recurso publicado.
La reproducción establece un mecanismo suficiente para ambos síntomas reales,
pero no identifica al dueño ni la duración de cada bloqueo en la sesión original.

La inspección de `ModelInstaller.FinalizeAsync` identifica una sección crítica
concreta: `store.Exclusive` engloba `PublishFile`, que copia el GGUF completo,
hace flush y calcula SHA-256 del origen y del temporal antes de renombrar.
La duración del bloqueo escala por tanto con el tamaño y el almacenamiento.
Esto ofrece una explicación técnicamente fundada para que publicar un modelo
interrumpa checkpoints de otra descarga; no se instrumentaron los bloqueos de
los incidentes originales para atribuir cada uno a esa operación.
No se observó desbordamiento: `ConfirmedBytes` y `ExpectedSize` son `long`.

Impacto actual: coexistencia de descargas largas y operaciones del mismo home.
Impacto potencial M3/TUI: más operaciones concurrentes podrían hacer el defecto
más frecuente. No se midió su frecuencia ni rendimiento bajo carga sostenida.
Evaluación requerida: estrategia de espera/backpressure del checkpoint y duración
de las secciones críticas, conservando exclusión, cancelación e integridad.
No se propone truncar parciales ni flexibilizar su validación como solución implícita.

Diagnóstico y resultados locales:
`${CoquenaHome}/validation/release-0.2.0/contention/Program.cs`,
`contention-result.txt`, `contention-control.txt` y `contention-repeat.txt`.
No agrega paquetes; referencia el proyecto existente. Reproducción:

```powershell
dotnet run --project ${CoquenaHome}/validation/release-0.2.0/contention/contention.csproj -c Release -- 2000
dotnet run --project ${CoquenaHome}/validation/release-0.2.0/contention/contention.csproj -c Release -- 8500
```

## Comparación inicial de especulación en 9B

Mismos dos prompts, temperatura 0 y seed 42 sólo durante esta comparación;
el preset de uso conserva temperatura 0.6. `cache_prompt=false`, máximo 1024
tokens de salida. Python terminó; C# alcanzó el límite, por lo que sus timings
no acreditan una solución completa. No se llenó el contexto de 262144 tokens.

| Método | Python tok/s | C# tok/s |
|---|---:|---:|
| Sin especulación | 40.77 | 42.52 |
| Sin especulación, repetición | 44.40 | 43.35 |
| MTP integrado, profundidad 3 | 45.70 | 43.24 |
| MTP integrado, profundidad 1 | 33.40 | 31.25 |
| ngram-mod | 49.56 | 44.07 |

El GGUF 9B declara `qwen35.nextn_predict_layers=1` y contiene tensores NextN.
La profundidad 3 aceptó 295/432 y 686/1009 tokens draft. La profundidad 1 tuvo
mayor aceptación proporcional, pero menor rendimiento: aceptación por sí sola
no mide el beneficio. Variación de carga y el pequeño número de muestras impiden
afirmar un óptimo general.

## Companion separado para la comparación MoE

Descargado mediante `model companion download`:
`a4lg/Qwen3.6-35B-A3B-MTP-ONLY-GGUF#mtp:Q8_0`, commit
`034793caf3d6f3a60cd9f0c37765434be9931cf4`, 1 990 649 632 bytes.
Su [autor](https://huggingface.co/a4lg/Qwen3.6-35B-A3B-MTP-ONLY-GGUF)
describe el uso como draft separado de derivados Qwen y recomienda medirlo.
Se creó el preset adicional `Ornith-1.5-35B-A3B-MTP`, con binding por ID al
parámetro `spec-draft-model`, sin sustituir los pesos oficiales del principal.

La ejecución posterior confirmó que el backend abre el draft separado, carga el
principal y acepta tokens especulativos. Ambos presets usan inicialmente
`n-cpu-moe=32`, dejando expertos de las primeras 32 capas en CPU; ese número
representa capas, mientras `threads=8`/`threads-batch=8` representan hilos.
Mover 36 capas de expertos a CPU se midió como alternativa.

| Método 35B | Python tok/s | C# tok/s | Finalización Python/C# |
|---|---:|---:|---|
| Base, CPU MoE 32 | 20.12 | 37.51 | stop / length |
| Companion Q8_0, MTP 3, CPU MoE 32 | 25.48 | 45.57 | length / length |
| Companion Q8_0, MTP 3, CPU MoE 36 | 24.28 | 41.84 | length / length |
| Companion Q8_0, MTP 4, mínimo 1, CPU MoE 32 | 24.95 | 45.23 | length / length |

Mismos prompts y controles que en 9B. La primera consulta incluye efectos de
carga/paginación distintos de la segunda; la velocidad cambia durante cada
generación. No es un benchmark aislado ni demuestra un máximo de rendimiento.
El companion con profundidad 3 y 32 capas CPU aceptó 617/1215 y 617/1216
tokens draft. Las respuestas de ese ensayo agotaron su presupuesto de 1024
durante el razonamiento, sin respuesta final utilizable; medir velocidad no
equivale a validar el contenido.

**Calidad observada en la respuesta base 35B:** entregó una función que elimina
duplicados globales, pero un assert esperaba repetir `A` al final de
`AABBBCCDAA`. Ejecutar el código emitido produce `AssertionError`. Es un fallo
de esa respuesta del modelo, no un fallo de transporte de Coquena. No se
generaliza la calidad del modelo a partir de una muestra.

## Presets conservados para uso

| Parámetro | Ornith-1.5-9B | Ornith-1.5-35B-A3B | Ornith-1.5-35B-A3B-MTP |
|---|---|---|---|
| Modelo | 9B Q4_K_M oficial | 35B Q4_K_M oficial | Mismo 35B, sin copiar pesos |
| ctx-size / parallel | 262144 / 1 | 262144 / 1 | 262144 / 1 |
| threads / threads-batch | 8 / 8 | 8 / 8 | 8 / 8 |
| temperature / top-p / top-k | 0.6 / 0.95 / 20 | 0.6 / 0.95 / 20 | 0.6 / 0.95 / 20 |
| min-p / presence-penalty / repeat-penalty | 0 / 0 / 1 | 0 / 0 / 1 | 0 / 0 / 1 |
| flash-attn / cache-type-k / cache-type-v | on / q8_0 / q8_0 | on / q8_0 / q8_0 | on / q8_0 / q8_0 |
| n-gpu-layers | auto | auto | auto |
| n-cpu-moe | No aplica | 32 | 32 |
| spec-type | ngram-mod | none | draft-mtp |
| spec-draft-model | Ninguno | Ninguno | Binding al companion Q8_0 |
| spec-draft-n-max / spec-draft-n-min | No asignados | No asignados | 3 / 0 |
| spec-draft-threads / spec-draft-threads-batch | No asignados | No asignados | 8 / 8 |
| spec-ngram-mod-n-match / n-min / n-max | 24 / 48 / 64 | No asignados | No asignados |
| reasoning-format / load-on-startup | deepseek / false | deepseek / false | deepseek / false |

`ngram-mod` fue la mejor opción observada para 9B en esta muestra. Para el
companion MoE se conserva profundidad 3 con 32 capas de expertos en CPU, frente a
las alternativas medidas. Estos resultados no establecen optimalidad universal.
Las penalizaciones neutras del 35B son una elección local explícita; su ficha
recomienda temperatura/top-p/top-k y no prescribe esas otras penalizaciones.

El catálogo local incluye definiciones para presence-penalty, repeat-penalty,
parallel, n-cpu-moe, reasoning-format, spec-type, spec-draft-model (alias
model-draft), spec-draft-ngl, spec-draft-threads, spec-draft-threads-batch,
spec-draft-n-max, spec-draft-n-min, los tres spec-ngram-mod y batch-size/ubatch-size.
Sólo se asignan los valores descritos; registrar una definición no activa el flag.
El soporte está asociado al ID de CUDA 12 b11696, según su ayuda y ejecución.
`preset show` compara disponibilidad con el backend global, aún CUDA 13 b11482;
la composición de `server ornith` utiliza su selección propia CUDA 12 b11696.
No se cambió el default global para alterar el comportamiento de otros servidores.

Snapshots finales y respuestas se conservan en
`${CoquenaHome}/validation/release-0.2.0`. El servidor expone los tres nombres en
`http://127.0.0.1:9931/v1`; `models-max=1` limita la carga simultánea.
El contexto completo está configurado y asignado por el backend; no se ensayó
una entrada de 262144 tokens ni calidad, latencia o consumo con esa ocupación.

## Cierre de la prueba manual

- La consulta de corrección del 35B con companion, muestreo de uso temperatura
  0.6 y presupuesto de 4096 tokens, terminó con `stop`: 1865 tokens generados,
  33.27 tokens/s según el backend. La respuesta corrigió la expectativa del
  assert y entregó una función compatible con listas no hashables. Se ejecutó
  su función de tests y dos casos adicionales: cuatro asserts pasaron.
- Con el binario extraído del paquete controlando el servidor y la configuración
  final, tanto `Ornith-1.5-9B` como `Ornith-1.5-35B-A3B-MTP` respondieron con
  `finish_reason=tool_calls`, función `read_file` y argumentos JSON válidos
  `{"path":"src/Program.cs"}`. No se ejecutó esa herramienta; la prueba acredita
  emisión y parseo de llamadas, no un ciclo completo de harness.
- El cambio 35B MTP → 9B → 35B MTP funcionó mediante el router. `/v1/models`
  enumera los tres presets. La instantánea final muestra base y 9B descargados
  de memoria (`unloaded`) y MTP activo, conforme a `models-max=1`.
- Servidor dejado activo: instancia `4bcb2a70-e9c7-4aba-b329-c3e33e344a6b`,
  PID observado 50732, loopback `127.0.0.1:9931`. Estos identificadores y el
  estado de carga son una instantánea, no una garantía para sesiones posteriores.
- Validaciones documentales: enlaces locales, bloques Markdown y `git diff --check`,
  incluido el archivo de evidencia nuevo. No se agregaron diagramas PlantUML ni
  ejemplos JSON independientes que compilar/validar; los snapshots y argumentos
  API se parsearon como JSON.

Archivos locales de cierre: `final-models.json`, `final-server-ps.txt`,
`final-server.txt`, `final-server.log`, `final-presets.ini`, `presets-final.json`,
`Ornith-1.5-9B-final-tool.json`, `Ornith-1.5-35B-A3B-MTP-final-tool.json`,
`35b-mtp-correction.json` y `35b-correction-validation.txt`.

La prueba no incorpora funciones del producto, paquetes ni correcciones de código.
Se amplió el catálogo de parámetros del home y se prepararon recursos/presets para
uso personal. El usuario aprobó commit, tag, push y release 0.2.0 el 16 de octubre
de 2026, manteniendo abiertos los problemas conocidos.

## Tareas de corrección autorizadas después de la prueba

El usuario solicitó crear tareas LeanSpec para los problemas de código encontrados.
Se crearon en estado planned, con evidencia, alcance y aceptación propios:

- [036: descargas reanudables bajo contención](../036-m2-checkpoints-contencion/README.md).
- [037: clasificación de drafts MTP](../037-m2-clasificacion-companions-mtp/README.md).
- [038: comparación de rutas Windows en pruebas](../038-m2-pruebas-rutas-windows/README.md).
- [039: codificación de salidas de procesos en pruebas](../039-m2-pruebas-codificacion-procesos/README.md).

Crear las tarjetas no inicia su implementación; la publicación recibió autorización
expresa posterior e independiente. El fallo del
assert generado por Ornith no se convirtió en una tarea del producto. Tampoco se
atribuyó una corrección concreta al primer transfer-io-failed de instalación CUDA,
cuya causa no se estableció. El caso reproducido de contención reúne publicación
y checkpoints en una corrección común; no crea dependencias artificiales entre
las otras tareas. No se creó un registro de hallazgos adicional.

Ornith también publica un
[DFlash propio](https://huggingface.co/ornith-ai/Ornith-1.5-35B-A3B-DFlash),
con recetas para vLLM/SGLang. No se presenta esa receta como evidencia de
ejecución en llama.cpp ni se convierte un checkpoint a GGUF automáticamente.
