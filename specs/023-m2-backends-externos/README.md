---
status: archived
created: 2026-10-10
tags:
- m2
created_at: 2026-10-10T16:35:37.958318600Z
updated_at: 2026-10-28T10:21:41.649013Z
completed_at: 2026-10-12T13:40:08.163389200Z
transitions:
- status: planned
  at: 2026-10-10T16:48:26.225918200Z
- status: in-progress
  at: 2026-10-12T05:23:06.096030500Z
- status: complete
  at: 2026-10-12T13:40:08.163389200Z
- status: archived
  at: 2026-10-28T10:21:41.649013Z
---
# Registrar y seleccionar backends externos

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

## Objetivo
Implementar backend register y la selección de instalaciones externas por nombre.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [CLI](../../docs/comandos.md#backend)
- [Identificación](../../docs/servidores.md#identificación-de-backends)

## Trabajo y aceptación
- [x] Validar ruta, nombre, familia declarada y sondeo local válido de --version; persistir
  ID, propiedad externa y distribución declarada, con origen oficial implícito.
- [x] Probar colisiones de nombres y selectores; distinguir un externo de un gestionado
  con igual origen, familia y release.
- [x] Seleccionar el externo mediante backend use y server use; iniciar/reiniciar el
  servidor con ese binario sin incorporarlo a latest o a descargas oficiales.
- [x] Verificar registro/listado sin copiar archivos; fallos de sondeo o persistencia
  no crean registros parciales ni alteran la selección previa.
- [x] Actualizar ayuda y documentación afectadas; ejecutar pruebas pertinentes y
  publicar/ejecutar Native AOT win-x64 con evidencia reproducible.

## Límites
No incorpora fuentes de descarga de terceros ni gestión de nombres cortos de orígenes.
No certifica aceleración ni procedencia a partir del nombre del ejecutable.

## Implementación y decisiones

- Registro local mediante `backend register`, sin red, copia de archivos ni cambio
  del default. Nombre único case-insensitive; `default`, `/`, `:` y controles rechazados.
- Propiedad `managed`/`external` independiente de la distribución declarada. Los
  registros existentes sin ese campo conservan el default gestionado del contrato.
  Los externos guardan nombre, ID, ruta absoluta, versión/build sondeados y familia.
- Distribución omitida o alias oficial normalizados a `ggml-org/llama.cpp`; terceros
  se declaran como propietario/repositorio, sin crear fuentes de descarga.
- Familias externas declaradas como identificadores minúsculos con versión mayor
  opcional `@N`; CUDA conserva `cuda@N`. Otras familias, por ejemplo `rocm`, no amplían
  la selección oficial, conforme a M2. La declaración no certifica aceleración.
- Sondeo reutiliza el proceso limitado a 15 segundos, salida acotada y exit 0.
  Se exige una sola línea de versión reconocida: `version: N (commit)` o
  `version: X.Y.Z (build N, commit commit)`, como en el instalador existente.
- `backend use` y `server use` resuelven el nombre a ID fijo; externos no adquieren
  pin ni seguimiento. Resolución latest, reinstalación y traslado de directorios
  gestionados excluyen externos, incluso cuando declaran la distribución oficial.
- Ayuda y documentación de comandos actualizadas. Sin paquetes nuevos.

## Evidencia

- Suite administrada completa ejecutada: contratos, almacenamiento, configuración,
  parámetros, servidores, procesos, descargas, instalación y directorios, modelos,
  GGUF y resolución. Incluye arranque/reinicio de un externo mediante un fixture.
- Publicaciones Native AOT estrictas `win-x64` de producto y runner: exit 0.
- Suite completa con el producto y runner nativos: exit 0; 32 comprobaciones de
  externos, 72 de procesos (incluye start/restart externo), 103 de directorios de
  backends y 56 de instalación/reinstalación. Registro, listado y selección probados
  con procesos CLI independientes y sondeo real del runner mediante `--version`.
  Salidas conservadas en `artifacts/task023-checks/native-tests.txt` y
  `native-errors.txt` (vacío).
- Validación documental: 37 diagramas PlantUML, tres ejemplos JSON y enlaces locales.
- `leanspec board/search/view` revisados. `leanspec deps` informa que el comando
  todavía no está migrado al adaptador; dependencias revisadas en la tarjeta y M2.

Comandos de reproducción:

```powershell
dotnet run --project tests/Coquena.Tests --no-restore
dotnet publish src/Coquena/Coquena.csproj --no-restore -c Release -r win-x64 -o artifacts/task023-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj --no-restore -c Release -r win-x64 -o artifacts/task023-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task023-tests-native/Coquena.Tests.exe artifacts/task023-native/coquena.exe
git diff --check
```

Los homes y binarios son fixtures temporales. No se modificó `${CoquenaHome}`, no se
instalaron backends reales ni se descargaron modelos. El sondeo nativo usa el runner
publicado; el recorrido de procesos no certifica inferencia real ni compatibilidad
con hardware. En este host el runner se inicia con `ProcessStartInfo` y entorno
normalizado para evitar duplicados Path/PATH.

Implementación y evidencia aprobadas explícitamente por el usuario el 12 de octubre de 2026. Tarea cerrada.
