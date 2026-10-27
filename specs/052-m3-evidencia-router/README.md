---
status: complete
created: 2026-10-21
priority: high
tags:
- m3
created_at: 2026-10-21T23:26:05.322040800Z
updated_at: 2026-10-27T21:26:52.969458400Z
completed_at: 2026-10-27T21:26:52.969458400Z
transitions:
- status: in-progress
  at: 2026-10-27T15:47:03.446262500Z
- status: complete
  at: 2026-10-27T21:26:52.969458400Z
---
# Acreditar capacidades del build oficial para presets y observación

## Objetivo

Fijar evidencia ejecutada del build/plataforma que respalda el adaptador, subordinada al contrato funcional aprobado.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [servidores.md](../../docs/servidores.md)
- [catalogo-de-parametros.md](../../docs/catalogo-de-parametros.md)
- [observabilidad.md](../../docs/observabilidad.md)
- [implementacion-catalogo-modelos.md](../../docs/implementacion-catalogo-modelos.md)
- [ui-servicio.md](../../docs/ui-servicio.md)
- [interfaces-visuales.md](../../docs/interfaces-visuales.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [x] Fijar build y demostrar arranque vacío, codec INI, precedencia y recarga de altas/cambios/retiradas de presets con nombres válidos.
- [x] Comprobar publicaciones de varios consumidores y el resultado de una recarga con peticiones en curso, sin prometer políticas propias de ocupación o atomicidad remota.
- [x] Acreditar restauración acotada de entradas afectadas y diagnóstico de reinicio explícito necesario sin ejecutarlo automáticamente.
- [x] Contrastar health, warmup declarado, SSE, métricas, slots y logs, con atribución disponible por servicio/instancia/entrada/tarea y ausencia de cargas por observar.
- [x] Distinguir aceptación, resultado confirmado e incertidumbre; documentar límites y diagnóstico de capacidades ausentes sin redefinir la política funcional.
- [x] Usar fixtures o recursos existentes; lectura de master no acredita soporte del build ni autoriza adoptar dependencias.
- [x] Construir una matriz de cada cifra y fase del dashboard con fuente pública o log reconocido, campo, unidad, ventana, granularidad, build y fixture real. Cubrir prompt/cache/progreso, salida/tokens por segundo, cola, finalización, slot/tarea y entrada/modelo.
- [x] Ejecutar peticiones de prueba de prompt y generación sobre dos presets, preferentemente de modelos distintos con recursos acordados; capturar snapshots y logs correlacionables sin registrar conversaciones privadas. Verificar concurrencia y reutilización de IDs tras reinicio.
- [x] Evaluar explícitamente qué puede observarse a tiempo real y qué sólo al terminar. Una capacidad ausente debe tener diagnóstico y representación visible; no acreditar un dashboard funcional sólo con fixtures ni asumir que SSE contiene todos los datos.

## Dependencias

Sin bloqueos de implementación dentro de M3. Referenciar documentación no crea una dependencia.

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin supervisor, gestión propia de modelos residentes, adaptadores universales de forks ni descargas costosas implícitas. Es evidencia del backend, no implementación anticipada de las otras tarjetas.

## Estado y evidencia

En progreso por autorización del usuario. Protocolo opt-in en
`tests/Coquena.Tests/RouterEvidenceTests.cs`, invocable desde el runner con
`--router-evidence <llama-server.exe> <directorio-vacío> [modelo-a.gguf modelo-b.gguf]`.
No descubre ni descarga recursos ni se conecta a instancias existentes. El modo
sin modelos prueba control con una ruta inexistente, sin inferencia. El usuario
aprobó Gemma 4 E4B Q4_K_XL y Ornith 1.5 9B Q4_K_M locales para ejecutar inferencia
sintética en dos entradas distintas.

### Evidencia ejecutada

Entorno anonimizado; rutas, logs completos y snapshots originales permanecen en
evidencia local externa al repositorio. Plataforma win-x64; backend oficial local
`ggml-org/llama.cpp b11899`, versión `0.7.0-dev`, commit `c6fb3b5be`, variante
CUDA 12, con ejecución CPU explícita. No se generalizan los resultados a otros
builds o variantes. [Matriz ejecutada, fuentes y límites](evidencia-b11899.md)
documenta cada fase/cifra del dashboard y los escenarios reales.

Resumen del recorrido de control sin inferencia:

| Escenario | Resultado observado |
| --- | --- |
| INI vacío `version=1` | `/health` 200 y `/models` vacío, sin inferencia. |
| Alta de `consumer-a.chat` y `consumer-b.chat` | `/models?reload=1` 200, ambas entradas `unloaded`. |
| Consulta de `/metrics`, `/slots`, `/props` por entrada con `autoload=false` | HTTP 400 en entrada no cargada; ambas siguen `unloaded`. No representa métricas cero. |
| Cambio de una entrada no cargada | Recarga 200, conserva ambas definiciones sin cargar pesos. |
| Retirada de una entrada | La otra definición permanece publicada. |
| Carga explícita con archivo inexistente | `/models/load` devuelve 200 y `success=true`; después `/models` informa `unloaded`, `failed=true`, `exit_code=1`. Aceptación no es resultado final. |
| Restauración de definición afectada | Reescritura y recarga conservan la entrada ajena y restablecen ambas definiciones sin reiniciar el router. No acredita restauración de inferencia. |

El protocolo emite INI mediante el codec existente de 046; su nombre histórico
`LlamaB11482ArgumentAdapter` no se renombra ni se interpreta como soporte universal.
La aceptación de este subconjunto por b11899 no acredita todavía toda la matriz
de opciones arbitrarias del adaptador. Se contrastaron también strings con espacios
internos, `=`, comillas, backslash y Unicode, rechazos sin representación reversible,
herencia global y precedencia CLI > preset > global con configuración cargada.

La ejecución real acreditó dos modelos concurrentes, cola con un slot, caché,
recarga con petición interrumpida (HTTP 500), restauración funcional acotada,
warmup explícito, SSE, logs y reutilización de slot/tarea tras reinicio. Los valores
del dashboard distinguen fuentes por tarea de acumulados/buckets por hijo y de
progreso discreto en logs. Se documentan datos no expuestos y su representación
como no disponibles, sin implementar anticipadamente el dashboard.

### Reproducción y validación

Definir `$TestRoot` en el volumen de pruebas, `$BackendExe` como la instalación
local oficial indicada y carpetas de salida independientes. Cada ejecución del
protocolo exige un directorio de evidencia vacío. El protocolo crea su propio
router, limpia las variables `LLAMA_*` heredadas y sólo termina su propio árbol
de procesos al salir; esa limpieza de pruebas no es política de stop del producto.

```powershell
$env:TEMP = $TestRoot
$env:TMP = $TestRoot
$env:COQUENA_HOME = Join-Path $TestRoot 'home'
$ProductOutput = Join-Path $TestRoot 'product'
$TestsOutput = Join-Path $TestRoot 'tests'
dotnet run --project tests/Coquena.Tests -c Release -- --router-evidence $BackendExe (Join-Path $TestRoot 'control-managed')
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o $ProductOutput -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -p:PublishAot=true -o $TestsOutput -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false
& (Join-Path $TestsOutput 'Coquena.Tests.exe') --router-evidence $BackendExe (Join-Path $TestRoot 'control-native')
& (Join-Path $TestsOutput 'Coquena.Tests.exe') --router-evidence $BackendExe (Join-Path $TestRoot 'real-native') $ModelA $ModelB
& (Join-Path $TestsOutput 'Coquena.Tests.exe') --argument-tests
& (Join-Path $ProductOutput 'coquena.exe') --help
```

- Control administrado y Native AOT win-x64: escenarios de la tabla OK. La rama
  de inferencia real se ejecutó desde el runner Native AOT con los recursos aprobados:
  última ejecución completa **28 comprobaciones OK**, exit 0, incluidos reinicio
  explícito necesario y endpoints desactivados (501 con health 200).
- Producto y runner publicados sin advertencias ni errores; producto ejecutó
  `--help`; runner nativo ejecutó además las 160 verificaciones de argumentos OK.
- Se corrigieron dos expectativas del protocolo según evidencia: la aceptación
  de carga puede fallar después; y una edición INI anulada por CLI puede conservar
  el hijo cargado, por lo que no se solicita cargarlo de nuevo. No se cambiaron
  reglas del producto para adaptar las pruebas.
- `leanspec deps` y `leanspec tokens` devuelven que no están migrados al adaptador. Se contrastaron
  manualmente tarjeta y tabla de M3: 052 no tiene bloqueos de implementación.
- Enlaces locales, rutas del mapa y `git diff --check` OK; 868 registros JSON/JSONL
  de la ejecución final parseados. No se modificaron diagramas PlantUML ni se
  agregaron ejemplos JSON estáticos. Evidencia compartible revisada por privacidad;
  rutas reales, logs crudos y snapshots quedan sólo en almacenamiento local.

### Alcance de la revisión

La evidencia acredita primitivas y límites del backend, no el ciclo de servicios,
la publicación de producto ni un dashboard integrado. A pedido del usuario, la
revisión incorpora decisiones funcionales, ajuste del Salt y una corrección acotada
de presentación de métricas en el ps existente; no implementa anticipadamente
055/065/068 ni agrega dependencias. Hardware del SO, TTFT
exacto y filas individuales de peticiones en cola no se acreditan a través de las
fuentes del backend estudiadas: la matriz explicita ausencia y diagnóstico.

La tarjeta queda lista para cierre con la aprobación explícita del usuario y el
commit único de todos los archivos de esta entrega.

### Decisiones de revisión acordadas

- Separar ciclo desacoplado de service start/restart y publicación interactiva de
  up/down y apply/withdraw. Autorizar el inicio con --start-service no acepta el
  riesgo de recarga. La advertencia de ocupación/incertidumbre y la elección de
  aplicar ahora o cancelar se especifican en servidores; no hay up -d, encolado ni
  drenaje implícitos. Esta es una decisión funcional nueva aprobada en el chat.
- Aclarar reglas existentes de observación: métricas agregadas por entrada y cifras
  por tarea son fuentes distintas; totales necesitan períodos compatibles y no
  se suman medias como velocidad instantánea. El Salt presenta ambas perspectivas.
- Tarea es una operación individual; su número se reutiliza. La correlación incluye
  generación del hijo, aun con router vivo. Puertos públicos e internos tienen
  funciones distintas; una dirección interna no es identidad estable.
- Los hallazgos quedan resueltos para el alcance de esta revisión mediante estas
  decisiones y restricciones documentadas; no constituyen otro backlog. Las tarjetas
  existentes 055, 065 y 068 enlazan su aplicación futura y siguen planned.
- El código existente de ps conserva nombres y valores del backend, añade caché,
  tiempos y ocupación cuando existen, y etiqueta acumulados, medias y período no
  informado. No produce datos por tarea ni totales nuevos, y no inventa métricas
  ausentes. La presentación se valida con las pruebas de observabilidad/runtime.

### Validación de las decisiones de revisión

Entorno anonimizado, sin datos del equipo en archivos compartibles:

```powershell
dotnet run --project tests/Coquena.Tests -c Release -- --runtime-tests
# Publicar producto y runner con los comandos Native AOT anteriores, después:
& (Join-Path $TestsOutput 'Coquena.Tests.exe') --runtime-tests (Join-Path $ProductOutput 'coquena.exe')
& (Join-Path $ProductOutput 'coquena.exe') server ps
java -jar $PlantUmlJar -charset UTF-8 -tpng -o $RenderOutput docs/tui-servicio.salt.puml
java -jar $PlantUmlJar -charset UTF-8 -checkonly docs/secuencias.md
git diff --check
```

`$PlantUmlJar` identifica la instalación local de PlantUML y `$RenderOutput` una
carpeta externa al repositorio. Resultado: 25 comprobaciones de observabilidad y
95 de ciclo de procesos OK en administrado y Native AOT, en este último caso con
la CLI del producto nativo. Producto y runner republicados sin advertencias ni
errores; `server ps` del producto sin instancias informa ese estado correctamente.
Los fixtures verifican aislamiento entre modelos, medias etiquetadas, métricas
ausentes sin cero inventado, caché/procesados separados, estados inaccesibles y
ausencia de cargas por consulta, además de identidad y ciclo de procesos.

Salt renderizado a PNG y revisado visualmente; sintaxis de secuencias comprobada,
enlaces locales, rutas del mapa, privacidad y diff comprobados. No se reejecutó
inferencia b11899 por el cambio de etiquetas/parser de ps: esa matriz conserva
la ejecución nativa previa. No se implementa un dashboard funcional por renderizar
el Salt ni se acredita publicación M3 por ajustar su contrato.

Fuentes de contraste fijadas al build (lectura no sustitutiva de ejecución):
[API y presets b11899](https://github.com/ggml-org/llama.cpp/blob/b11899/tools/server/README.md),
[router b11899](https://github.com/ggml-org/llama.cpp/blob/b11899/tools/server/server-models.cpp),
[parser INI b11899](https://github.com/ggml-org/llama.cpp/blob/b11899/common/preset.cpp).
