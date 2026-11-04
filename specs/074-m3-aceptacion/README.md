---
status: archived
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 073-m3-integracion-cli
created_at: 2026-10-21T23:26:07.450587100Z
updated_at: 2026-11-04T19:52:46.486510900Z
completed_at: 2026-11-04T19:37:12.825561600Z
transitions:
- status: in-progress
  at: 2026-11-04T02:10:53.057144300Z
- status: complete
  at: 2026-11-04T19:37:12.825561600Z
- status: archived
  at: 2026-11-04T19:52:46.486510900Z
---
# Acreditar y preparar el paquete integrado Native AOT de M3

## Objetivo

Acreditar desde una carpeta limpia el catálogo de proyecto, la composición y
operación de recetas YAML escritas o recibidas, y su importación al catálogo
global. El recorrido imperativo global se evalúa en la 073.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [README.md](../../docs/README.md)
- [Servicios y recetas](../../docs/servicios-recetas.md)
- [Formato de proyecto](../../docs/formato-proyecto.md)
- [Persistencia](../../docs/persistencia-proyectos.md)
- [Comandos](../../docs/comandos.md)

Las reglas funcionales prevalecen. Esta tarjeta registra aceptación y evidencia.

## Trabajo y aceptación

- [x] Ejecutar receta global importada, consumidor de default/servicio nombrado y proyecto self desde archivos manuales/recibidos, sin requerir init o editor.
- [x] Componer capas separadas para servicio, modelos y presets, y argumentos. Confirmar la precedencia en el INI del proyecto y en una importación.
- [x] Descargar y probar un derivado de llama.cpp para Windows con un modelo pequeño. Registrar por separado inferencia y compatibilidad de publicación.
- [x] Cambiar un preset de un servicio vivo tras terminar una inferencia y durante generación del preset afectado. Verificar confirmación y efectos de cancelar.
- [x] Acreditar name raíz único, entidades independientes, rename de carpeta con estado local, down bajo nombres previos y nuevo up sin afectar consumidores ajenos.
- [x] Probar preparación, inicio/recuperación, INI y refresh en up/down, import idempotente/selectivo/reemplazo, update/upgrade, verify y limpieza con propiedad/pins/referencias.
- [x] Ejecutar los correctivos 041/042/043 y las conveniencias 071/072; comprobar formatos legibles y sintaxis de ayuda sin capacidades planificadas presentadas como implementadas.
- [x] Acreditar ps finito/continuo y logs entre reinicios con falta de telemetría, recursos compartidos y cierre sin efectos.
- [x] Probar concurrencia, fallos de red/integridad/candidata, incertidumbre remota, copia/traslado, pérdida de home y recursos locales existentes sin redescarga injustificada.
- [x] Ejecutar la regresión vigente del recorrido de recetas y preparar un paquete Native AOT win-x64 extraído para revisión explícita.
- [x] Ejecutar inferencia real con un modelo pequeño desde el paquete extraído.
- [x] Aislar las regresiones reproducibles de la preview que afecten el uso de recetas.

## Dependencias

- [073](../073-m3-integracion-cli/README.md).

La relación histórica permanece en el tablero porque `leanspec rel` todavía no
está migrado al adaptador. El usuario separó la aceptación del catálogo local
del recorrido imperativo global. La 073 figura completa y no bloqueó estas pruebas.

## Límites

Sin release/tag/push, instalación global, commits automáticos, descargas costosas
implícitas ni incorporar M4 como requisito oculto. Cierre sujeto a revisión explícita.

## Estado y evidencia

Todos los checks de aceptación ejecutados. Aprobada por el usuario el 4 de noviembre de 2026.
Entorno anonimizado, Windows x64, estado y temporales de ejecución en D:.
Se reutilizó Qwen2.5-0.5B-Instruct Q4_K_M y los backends indicados abajo.
Las pruebas no son mediciones de rendimiento ni certifican otras máquinas.

### Matriz de aceptación

| Área | Evidencia ejecutada |
| --- | --- |
| Identidad, traslado y concurrencia | `--project-persistence-tests` pasó registro, identidad física, índice reconstruible, copia con identidad nueva, referencias, lectores/escritores concurrentes y fallos entre generaciones. `--combination-tests` pasó 69 comprobaciones, incluidas resoluciones portables sin home anterior y reutilización exacta sin metadatos ni transferencia. |
| Publicación | `--publication-tests` pasó 43 comprobaciones de contribuciones independientes, rechazo de nombres en conflicto, respuesta perdida, reconciliación, protección de recursos y restauración parcial. `--publication-router` pasó contra llama.cpp b12089 CPU con dos consumidores, up/down, capas, apply/withdraw, cambio de alias y YAML, rename, traslado, self, inicio autorizado y precarga fallida. |
| Importación | Selección 24, integración 70 y `--recipe-import-runtime` 15 comprobaciones. El recorrido real ejecutó la CLI AOT con home nueva, importación repetida y selectiva, reemplazo con router activo, conservación de contribución ajena y de la instancia activa, y sustitución de intención sin reinicio. |
| Recursos y preparación | Backends por ámbito 56, modelos 53, archivos locales 90 y transferencias por ámbito 61 comprobaciones. Cubren latest/pins, incompatibilidad/force, shards, externos, concurrencia, fallos de red/integridad, interrupción y recuperación. |
| Actualización y limpieza | Update 78, upgrade 30, verify 66 y clean 35 comprobaciones. Incluyen propuestas exactas, pins, edición concurrente, publicación conjunta y recuperación, consulta fallida que invalida propuesta anterior, GUID ajeno, propiedad externa, referencias activas/inciertas y protección explícita. |
| Proceso y recuperación | Ciclo de servicio 39 y recuperación 32 comprobaciones. Además, con ik_llama.cpp real, una capa con argumento desconocido hizo terminar la candidata. Se recuperó la última buena y respondió inferencia de 8 tokens conservando el diagnóstico de la candidata. |
| Observación | Observación finita 37 comprobaciones con CLI AOT y router real, watch 23, seguimiento de logs 47 y logs/hooks 32. Cubren métricas ausentes/desactivadas, límites de consulta, PID reutilizado, servicios detenidos, atribución, reinicios, rotación y cierre del observador sin detener servicio. |
| Ayuda y unidades | `--help-tests` pasó en API y producto AOT, con aliases y 27 errores sin efectos. `--byte-size-tests` pasó 85 comprobaciones y retención 58 en el runner nativo. Configuración global del producto aceptó 1.5GiB y 1M, mostró magnitudes humanas y rechazó 1Mi con código 2. |
| Selección de cuantización | `--model-selection-tests` pasó 183 y 217 comprobaciones de metadata, cuantización inequívoca, shards y selección sin pesos remotos. El motor de instalación y las pruebas por ámbito cubren el camino directo sin menú redundante. |
| Edición e init | Recetas 323 y argumentos 167 comprobaciones. En producto AOT, backend set, model add --name/--file, preset set/clone/bind/unset/remove y param define/set/remove editaron sólo la última capa UTF-16, conservaron BOM, comentario y hash de la base y no crearon home ni estado. Init preservó receta manual y exclusiones, fue idempotente en carpeta con espacios, no creó Git/estado y rechazó un directorio en lugar del YAML antes de crear metadata. |
| Regresión de motores M1/M2 vigente | El runner Native AOT ejecutó 571 comprobaciones del motor de modelos y 295 de backends. Estas pruebas conservan las APIs del motor. La sintaxis histórica de creación de presets y retención por servicio pertenece al recorrido imperativo de la 073; el producto se acredita aquí por su recorrido YAML. |

### Recetas e inferencia reales

Se compusieron tres archivos mediante `-f service.yaml -f models.yaml -f args.yaml`.
La última capa cambió ctx-size sin perder el modelo tipado de la anterior. La
importación de las capas fue idempotente y generó su INI con el override seleccionado.
Una variante portable omitió la ruta de INI específica del proyecto.

Dos consumidores compartieron el router por default y por nombre. Tras renombrar
una carpeta con su estado, down retiró sólo su publicación anterior y up la repuso.
Editar name después de down produjo el nombre nuevo, acreditado por la prueba
integrada real. Un segundo consumidor con el mismo nombre recibió
`preset-name-conflict` (3). Las APIs confirmaron los conjuntos después de cada cambio.

Después de terminar una inferencia, up cambió el preset sin preguntar. Durante
emisión de contenido SSE del preset afectado, solicitó confirmación. Cancelar
devolvió 130 y conservó el INI anterior. Las aserciones integradas ahora exigen
cero confirmaciones para cambios ociosos, conservando las pruebas de cancelación
por actividad en fixtures y el recorrido SSE real.

Se ejecutó inferencia con llama.cpp b12089 CPU, ik_llama.cpp y TurboQuant.
El ejecutable final del ZIP extraído arrancó el proyecto propio con una home
nueva, reutilizó estado y recursos externos del proyecto, generó 8 tokens y
se detuvo. Ps confirmó después el servicio detenido. Esto acredita autonomía
respecto de la home anterior; los casos gestionados sin redescarga se cubren
además en las suites de combinaciones y recursos. Los servicios reales quedaron detenidos.

### Backends alternativos

- [Thireus/ik_llama.cpp main-b5574-45acbfc](https://github.com/Thireus/ik_llama.cpp/releases/tag/main-b5574-45acbfc), Windows CPU AVX2, ZIP de 31 711 391 bytes, SHA-256 `0fef40e10d424ee3f92e756cdb4bca4205a036354b098658b9476efe34cb6d1f`. Pasó salud, inferencia, ps, logs, reinicio y recuperación de candidata. El binario rechaza `--models-preset`. El [servidor de esa revisión](https://github.com/Thireus/ik_llama.cpp/blob/45acbfc8/examples/server/server.cpp) carga el modelo al arrancar y usa `/load` para sesiones SQLite. No se identificó otro formato de catálogo recargable en ese build. Up devolvió `publication-unsupported` (5).
- [AtomicBot TurboQuant b10298-2.1.0](https://github.com/AtomicBot-ai/atomic-llama-cpp-turboquant/releases/tag/b10298-2.1.0), build 11428, commit `0fe16d12d`, ZIP Windows CPU de 13 726 040 bytes, SHA-256 `91da01170ff3b3963149c26553725a830667114299bc5e18919046c10810ec72`. Pasó capas, INI, up/down, inferencia, reinicio, ps y logs. Recibió cache-type-k/v=turbo3 y flash-attn=on; el log documentó la promoción automática de K a q8_0 por el GQA del modelo. Down dejó `/v1/models` vacío y `/health` disponible. La política del backend interviene en la configuración efectiva.

### Correctivos y regresiones aisladas

- Se corrigió el filtro de ps que ocultaba servicios detenidos. Contradecía la selección general documentada. La prueba de observación de 37 comprobaciones pasó con el producto corregido, incluidos servicios globales y de proyecto detenidos, alias default y selección explícita.
- La aserción que exigía un GUID en ps se sustituyó por nombre, estado y salud del servicio seleccionado. Verify se comprueba mediante JSON y checksum explícito cuando se exige `verified`, conforme al contrato de auditoría rápida y checksum opcional. No se cambió ese comportamiento del producto.
- Los fixtures de router externo ahora declaran su INI explícito. No dependen de que su versión coincida con el build externo conocido por el producto. Se conservaron las comprobaciones de contribuciones ajenas y de la identidad de la instancia.
- El fallo de la batería histórica al ejecutar preset create sin receta y server log-retention quedó atribuido a sintaxis global imperativa de la 073. Las APIs de motor y los caminos públicos de recetas se probaron por separado.

### Paquete Native AOT para revisión

SDK 10.0.403 seleccionado en global.json, ILCompiler 10.0.14. La restauración
forzada con `NuGetAuditMode=all` y PublishAot pasó. Producto y runner se publicaron
win-x64 Release con análisis AOT habilitado y sin advertencias.

```powershell
$env:TEMP = Join-Path $TestRoot 'temp'
$env:TMP = $env:TEMP
dotnet restore tests/Coquena.Tests/Coquena.Tests.csproj -r win-x64 -p:PublishAot=true -p:NuGetAuditMode=all --force
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o $ProductRoot -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -o $RunnerRoot -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& $Runner --observation-tests $Product $Backend
& $Runner --publication-router $Backend (Join-Path $TestRoot 'publication')
& $Runner --recipe-import-runtime $Backend $Product (Join-Path $TestRoot 'import')
& $Runner --recipe-update-tests (Join-Path $TestRoot 'update') $Product
```

TestRoot es una carpeta de prueba vacía en D:. ProductRoot y RunnerRoot son sus
subcarpetas de publicación; Product es el ejecutable extraído, Runner el ejecutor
publicado y Backend el llama-server local compatible acordado. Cada directorio de
fixture debe ser nuevo. Las demás suites de la tabla corresponden a los selectores
homónimos de Program.cs, con directorio adicional en cleanup y recipe-upgrade.

Paquete preparado `coquena-0.3.0-win-x64.zip`, 6 239 977 bytes.
SHA-256 ZIP `7a65baf77b67586de344f656127a2e198072a2cc101019d584e96c1db29a0c1d`.
SHA-256 ejecutable extraído `9fb41856d75574fda0c2949c210403bd4e4e48a16ed95ce9bddb64e46ae70394`.
Tras promover Version a 0.3.0 se repitió la publicación Native AOT y la extracción.
Los metadatos PE informaron FileVersion 0.3.0.0 y ProductVersion 0.3.0 con
el sufijo de revisión Git. Para preparar el release se recompiló desde
`64eb76b12a96ddbba5b307505aa2885c56fe532c`, con las fuentes runtime ya commiteadas.
El ZIP incluye ejecutable, README y LICENSE, con SHA256SUMS.txt separado.
El README incorpora un ejemplo ilustrativo de varios presets con modelo de
Hugging Face y archivo local opcional. Se validaron lectura, referencias y
emisión de argumentos sin descargar pesos. El cambio de ZIP corresponde a
esa documentación y conserva el mismo ejecutable.
Config e init se ejecutaron con éxito desde ese paquete extraído.
Las ubicaciones reales se conservan en el chat. El ZIP final sustituye los
paquetes preliminares de esta ejecución.

## Hallazgos y límites de la evidencia

El [hallazgo 002](../../findings/002-coordinacion-monitor-limpieza.md) mantiene
su alcance autorizado. Pasar estas pruebas no descarta la carrera conocida entre
monitor y limpieza temporal. Su corrección no forma parte de esta aceptación.
Las pruebas remotas usan fixtures controlados para red, corrupción e incertidumbre.
La inferencia real acredita los builds y el modelo indicados, sin mediciones de
escala, rendimiento o calidad. Las capacidades recargables se comprobaron por
build. La ubicación externa del INI requirió configuración explícita y específica
de la instancia, relevante para evaluar una experiencia portable posterior.

El borrado recursivo del temporal autorizado volvió a ser rechazado por la política
de ejecución, sin motivo detallado. Los procesos propios ya terminaron y el
paquete de revisión está fuera de ese directorio. La limpieza sigue pendiente.

Todos los checks de aceptación están completos y la revisión fue aprobada.
Versión del proyecto promovida a 0.3.0 por instrucción del usuario. Entrega
registrada en `64eb76b`. Archivado autorizado junto con la preparación del
release, cuyo texto requiere aprobación antes de publicarse.
No se crearon registros de hallazgos ni tareas adicionales.
