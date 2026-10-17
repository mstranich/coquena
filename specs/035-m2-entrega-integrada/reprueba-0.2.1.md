# Reprueba de ciclo completo para evaluar 0.2.1

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

Prueba manual autorizada el 17 de octubre de 2026 sobre `f9ac856`, después de
las correcciones 036–039. No reabre tareas históricas ni autoriza correcciones
de código encontradas durante la prueba. La eventual publicación está condicionada
al resultado del recorrido solicitado.

## Entorno y alcance

`COQUENA_HOME=${CoquenaHome}`. Producto Native AOT win-x64 recién publicado, aún
versionado 0.2.0; evidencia local en `${CoquenaHome}/validation/0.2.1-retest`.
Se conservan los recursos gestionados anteriores Gemma/Bonsai y CUDA 13.
El usuario amplió explícitamente la limpieza a todos los registros externos.
Inventario previo de catálogos/configuración en `before`; estado limpio en
`after-clean`; comandos de limpieza/reconstrucción en `commands.txt`.

Reglas contrastadas: [retirada de recursos](../../docs/modelos-y-companions.md),
[parámetros y cascadas](../../docs/catalogo-de-parametros.md),
[servidores](../../docs/servidores.md) y [CLI](../../docs/comandos.md).

## Limpieza comprobada

- Detención de `ornith`, instancia `e7d05b6d-b183-4eda-9b78-b3a00cc2df63`,
  PID observado 44740; después sin instancias activas verificadas.
- `param remove spec-draft-model --dry-run`: informó el binding del preset MTP
  y el servidor afectado. Cancelar la operación interactiva conservó los hashes
  de parameters/presets/servers. Confirmar retiró definición y binding, conservando
  preset y companion, sin borrar pesos.
- `config param-reset --dry-run` y confirmación interactiva: retiró parámetros
  añadidos y sus asignaciones, mantuvo las compatibles y las entidades.
  Impacto y transcripción en `reset-impact.txt` y `reset-transcript.txt`.
- Tres externos desregistrados: Gemma 12B, su mmproj y Qwen3.6 35B. Informó
  cero bytes liberados; SHA-256 de sus archivos antes/después idénticos.
  Inventario verificable en `external-before.json`.
- Retirada alternada: preset 9B antes de su modelo; modelo 35B antes de sus
  presets; companion y los presets restantes; backend CUDA 12 seleccionado.
  Los presets/referencias ausentes impidieron iniciar, sin sustitución silenciosa.
- Bytes físicos liberados: 9B 5 780 090 816; 35B 21 713 463 040;
  companion 1 990 649 632; backend CUDA 12 1 200 063 809.
  Se comprobó la ausencia de los pesos antiguos. Los recursos vecinos permanecen.
- Servidor detenido retirado; recursos/logs conservados. Los presets retirados
  dejaron referencias explícitas rotas hasta retirar el servidor: no hay una
  cascada implícita de entidades al eliminar modelos/presets/backends.

## Reconstrucción e incidencias observadas

Backend solicitado mediante `backend install cuda@12 --release latest`: b11712.
Descargó ambos ZIP, 658 584 979 bytes en total, pero el primer intento terminó
`failed/publishing`, diagnóstico `transfer-io-failed`. Se conservó la operación
en `backend-failed-operation.json`. `download resume
00b1a758-fdb5-4511-a654-4a1e3d793482` completó instalación y limpieza sin nueva
descarga. No se atribuye el fallo a contención ni a un bloqueo de archivo sin
evidencia adicional. La recuperación funciona; la primera instalación no fue limpia.

Nuevo backend `efa33533-1878-4489-bd9d-1dc6bd00f2af`; ayuda efectiva en
`llama-help.txt`. Definiciones adicionales y asociaciones de soporte se repusieron
por CLI, acotadas a este ID/build, después del reset. Servidor `ornith` recreado
en loopback puerto 9931, `models-max=1`, selección propia CUDA 12 latest.

Dos principales Q4_K_M confirmados interactivamente y descargándose en paralelo:
9B `11e40a8e-69fc-45ba-a7a6-4097abb94dad`, 35B
`e759fb9e-99d6-42e8-993c-5af90625944e`. Sin entrada interactiva el menú rechaza
la operación antes de crear trabajo; se retomó por terminal interactiva.

El selector `a4lg/Qwen3.6-35B-A3B-MTP-ONLY-GGUF#mtp:Q8_0` no ofrece candidato,
aunque el árbol remoto conserva el archivo Q8_0 de 1 990 649 632 bytes. La política
aprobada en 037 reconoce `mtpdraft`/`mtp-draft`, carpeta exacta `mtp` y prefijo
`mtp-` del basename; no el token interno `MTP-ONLY`. Hecho observado y explicado
por el clasificador, no desaparición remota ni certificación de incompatibilidad.
No se alteró la política ni se reclasificó el archivo manualmente. Se consultó
usar el draft EryriLabs reconocido o conservar a4lg para evaluar una corrección.
Impacto: esta receta previa no puede reconstruirse íntegramente por el selector
actual. M3 también necesitará distinguir selección y compatibilidad; no se amplía
su implementación en esta prueba.

Para avanzar dentro de la configuración autorizada, se tomó como alternativa de
prueba el draft EryriLabs reconocido por la política vigente, informándolo en el
chat y conservando la posibilidad de ajustar la preferencia. No se interpreta
la falta de respuesta a la consulta como aprobación de una regla nueva o del
lanzamiento. Su [ficha](https://huggingface.co/EryriLabs/Ornith-1.5-35B-A3B-BigBang-MTP-GGUF)
declara que `mtpdraft-Q8_0.gguf` también puede usarse con Ornith stock; esto es
una afirmación del publicador pendiente de comprobar aquí, no una garantía del
selector. El menú ofreció sólo ese draft (1 990 649 440 bytes), sin el principal
de 36,9 GB; se confirmó su descarga. README y árbol remoto en `sources/eryri-mtp.md`
y `eryri-tree.json`, transcripción en `download-eryri-interactive.txt`.

El usuario respondió después: «eso hay que corregirlo para contemplar el caso»
y solicitó un reporte para revisar tras las pruebas. Se conserva ese requisito;
EryriLabs es una alternativa para ejercitar la ejecución, no una resolución del
caso a4lg ni aprobación de lanzamiento con la limitación. La corrección queda
para la revisión posterior solicitada, sin modificar código en este recorrido.

Fuentes actualizadas: [Ornith 9B](https://huggingface.co/ornith-ai/Ornith-1.5-9B-GGUF),
[Ornith 35B](https://huggingface.co/ornith-ai/Ornith-1.5-35B-A3B-GGUF) y
[companion a4lg](https://huggingface.co/a4lg/Qwen3.6-35B-A3B-MTP-ONLY-GGUF).
Se mantienen las recetas locales de la [prueba anterior](prueba-ornith.md), con
contexto explícito 262144 y ocho hilos, pendientes de validar de nuevo con b11712.

## Resultados finales

9B instalado y publicado mientras el 35B seguía transfiriendo. Se probó pausa en
3 006 068 081 bytes y reanudación hasta completar la misma operación, sin reiniciar
desde cero ni `writer-busy` observado en ese tramo. Evidencia `9b-paused.json` y
`9b-resume.txt`. Nuevo modelo `6519a20a-3938-42ff-9cf8-94989e5f0486`, preset
`e5e09369-d8cb-48d1-a737-eac677e24fe8`, con valores de la receta anterior
repuestos por CLI. Inicio de `ornith`: instancia
`79f3d374-dd72-4d03-ab8d-a865f7e8263a`, PID observado 56216.

Con la instancia activa, retirar modelo 9B o backend se rechazó mostrando su
GUID, sin borrarlos. La consulta API de programación terminó `stop` y entregó
`def add(a, b): return a + b`; inspeccionado antes de ejecutar, pasó tres asserts.
La consulta de herramienta emitió `read_file` y argumentos JSON con
`path=src/Program.cs`; no se ejecutó la herramienta. `server ps` mostró métricas
del 9B. Requests/responses y validación en `9b-{code,tool}-{request,response}.json`,
`9b-answer.py` y `9b-answer-validation.txt`.

Se comprobaron los registros, tamaños y fechas de archivos gestionados vecinos
contra el inventario previo y la configuración global por hash: sin cambios.
Los presets vecinos también permanecen iguales. Evidencia `neighbors-check.txt`.

Los dos principales y el companion EryriLabs completaron verificación, publicación
y limpieza. No quedan operaciones de descarga. El 35B se publicó con otra unidad
ya instalada y después de las transferencias simultáneas; no se observaron
`writer-busy` ni `checkpoint-inconsistent` en las operaciones de modelos de esta pasada.
IDs y commits finales en `installed-models.json`:

| Recurso | ID | Commit remoto |
|---|---|---|
| 9B Q4_K_M | 6519a20a-3938-42ff-9cf8-94989e5f0486 | abdd624b12ebf020b767fff532ff44fe552b28c3 |
| 35B Q4_K_M | b7725499-9f50-4b2c-8154-803dfc1d0ab0 | 12393612fd4f730ff5aadc23e9b8f9648aa49ceb |
| EryriLabs MTP Q8_0 | 2406e417-a1e6-4f33-9435-afbf92c77426 | 2e9f6f487b82200f2a045e67d01a3f1e2982b00a |

Se creó el preset base 35B y se clonó mediante CLI para el MTP, compartiendo el
principal y conservando asignaciones independientes. Se repusieron por CLI los
valores de la receta anterior; el binding MTP apunta al nuevo ID EryriLabs.
En ambos MoE, `n-cpu-moe=32` representa capas de expertos en CPU, mientras
`threads=8` y `threads-batch=8` representan hilos. Los tres tienen
`ctx-size=262144` y `parallel=1`. Las opciones MTP conservadas son profundidad
3, mínimo 0 y ocho hilos de draft. Se mantienen muestreo 0.6/0.95/20 y cachés Q8_0.
La ayuda de b11712 y la ejecución efectiva respaldan el soporte local declarado;
no se extrapola ese soporte a otros builds.

Tras reiniciar, `/v1/models` enumera los tres nombres canónicos. La consulta de
código en cada preset terminó `stop` y pasó tres asserts de la función corta
`add`: nueve asserts en total. Las cinco consultas de herramienta (9B inicial,
MTP, base, 9B por router y MTP por router) terminaron `tool_calls`, función
`read_file`, JSON válido y `path=src/Program.cs`. La herramienta no fue ejecutada.
La alternancia MTP → base → 9B → MTP fue exitosa mediante el router.

El log confirma carga del draft EryriLabs separado, ocho hilos y contexto/slot
262144. La consulta inicial MTP aceptó 25/51 tokens draft; las siguientes 26/93
y 23/72. Esto acredita especulación en esta ejecución, no aceleración óptima:
la generación corta de código midió aproximadamente 8,45 tok/s en base y 7,51
en MTP, con cargas/paginación distintas y sólo 43 tokens generados en cada una.
No constituye un benchmark controlado ni demuestra que MTP sea más rápido.
Tampoco certifica calidad general de programación o uso completo del contexto.

Retirar el companion o el servidor activos fue rechazado con el GUID de la
instancia, sin cambiar sus registros. Estado dejado para uso:
`ornith`, instancia `3d5b1a8d-2b1d-4545-9825-1bd833f528b9`, PID observado 42512,
`127.0.0.1:9931/v1`, backend CUDA 12 b11712. MTP cargado, 9B y base unloaded,
conforme a `models-max=1`. Son identificadores/estado observados, no una garantía
de permanencia posterior.

Snapshots `after-rebuild`, `final-instance.json`, `final-models.json`,
`final-presets.ini`, `final-server.log`, `final-server-ps.txt` y
`final-server-show.txt`. Requests/responses del 35B en
`35b-{base,mtp}-{code,tool}-{request,response}.json`; repeticiones de router en
`{9b,35b-mtp}-router-tool-{request,response}.json`. Sus funciones inspeccionadas y
validaciones están en `35b-{base,mtp}-answer.py` y `*-answer-validation.txt`.

El registro conserva invocaciones de sintaxis rechazadas y luego corregidas:
install recibe familia y `--release`, la consulta de descarga es `status`,
y restart no acepta `-d`. No se presentan esos errores de invocación como fallos
del producto. La validación corresponde al producto Native AOT y al recorrido
manual descrito; no se volvió a ejecutar aquí la suite automatizada general.

## Estado de revisión

Prueba manual terminada con entorno reconstruido y alternativa EryriLabs funcional.
El [reporte solicitado](reporte-reprueba-0.2.1.md) conserva el caso a4lg por corregir
y la incidencia de publicación CUDA recuperada manualmente. No se considera
cumplida una pasada limpia que habilite automáticamente el lanzamiento.
Sin cambios de producción, bump de versión, commit, tag, push o release.
Sólo documentación de evidencia/reporte modificada en el checkout.
