---
status: archived
created: 2026-10-10
priority: medium
tags:
- m2
created_at: 2026-10-10T10:53:13.495445100Z
updated_at: 2026-10-28T10:21:41.646880600Z
completed_at: 2026-10-11T20:20:00.803934500Z
transitions:
- status: planned
  at: 2026-10-10T12:29:25.357764100Z
- status: in-progress
  at: 2026-10-11T18:08:18.947227300Z
- status: complete
  at: 2026-10-11T20:20:00.803934500Z
- status: archived
  at: 2026-10-28T10:21:41.646880600Z
---
# Separar el sondeo del sistema mediante system probe

El hardware del entorno de prueba se ha anonimizado. Los resultados conservados describen esa ejecución y no garantizan el mismo rendimiento en otro equipo.

## Objetivo
Exponer el sondeo de SO y hardware mediante system probe.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [CLI](../../docs/comandos.md#system)

## Trabajo y aceptación
- [x] Trasladar el sondeo existente a system probe conservando informe y diagnósticos.
- [x] System sin subcomando no ejecuta el sondeo; muestra uso con el diagnóstico normal
  de argumentos incompletos. No se mantiene la invocación anterior como alias.
- [x] Rechazar argumentos adicionales no definidos antes de iniciar el sondeo.
- [x] Actualizar ayuda y pruebas del despacho CLI.
- [x] Publicar y ejecutar Native AOT win-x64; registrar evidencia.

## Límites
No modifica la detección de hardware ni añade ajustes del manager o nuevas capacidades
de sondeo. No requiere avisos de deprecación ni mecanismos de transición.

## Implementación

El despacho acepta únicamente `system probe` y mantiene el informe existente sin
cambios en HardwareProbe. `system`, subcomandos desconocidos y argumentos adicionales
devuelven código 2, salida estándar vacía y diagnóstico de uso antes del sondeo.
La ayuda general anuncia `system probe`.

Las pruebas de BackendCli inyectan un sondeo que falla si se invoca con argumentos
inválidos. También conservan la cobertura de diagnósticos `unknown`. El despacho
general verifica ayuda y rechazo de argumentos en memoria y mediante la CLI publicada;
la prueba de hardware real invoca ahora `system probe`.

## Evidencia

- Regresión administrada completa: exit 0; 14 casos de despacho y 184 comprobaciones
  de backends, además de las suites existentes.
- Publicación Native AOT win-x64 de CLI y runner con análisis estricto: exit 0,
  sin advertencias.
- Regresión completa con runner y CLI Native AOT: exit 0; 14 casos de despacho
  administrados y 14 del ejecutable, 185 comprobaciones de backends y todas las
  suites existentes. Logs en `artifacts/task021-checks/native-suite.txt`.
- CLI nativa: `system probe` devuelve 0 e informa win-x64, CPU i9-13900K, RAM,
  adaptadores GPU de prueba/GPU de prueba, CUDA y Vulkan compatibles, con sus
  diagnósticos y la aclaración de límites del sondeo. `system` y
  `system probe extra` devuelven 2 con el uso esperado.
- Documentación: 37 diagramas PlantUML, tres ejemplos JSON y 176 enlaces locales
  válidos, incluyendo esta tarjeta.
- `git diff --check`: exit 0.

Reproducción desde la raíz del checkout:

```powershell
dotnet run --project tests/Coquena.Tests --no-restore
dotnet publish src/Coquena/Coquena.csproj --no-restore -c Release -r win-x64 -o artifacts/task021-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj --no-restore -c Release -r win-x64 -o artifacts/task021-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task021-native/coquena.exe system probe
& artifacts/task021-checks/run-native-tests.ps1
& artifacts/task021-checks/validate-docs.ps1
git diff --check
```

El helper nativo normaliza Path/PATH sin distinguir mayúsculas y guarda los logs en
`artifacts/task021-checks`. El runner debe ser nativo: la prueba de instalación lo
extrae como fixture ejecutable autónomo; el runner administrado no satisface esa prueba.
Implementación y evidencia aprobadas por el usuario el 11 de octubre de 2026.
Tarea cerrada con código, pruebas y evidencia en un único commit.

Limitaciones: no se amplía el sondeo ni se comprueba inferencia. `leanspec deps` no
está implementado en el adaptador; la revisión manual de la tarjeta no encuentra
dependencias declaradas ni bloqueos adicionales.
