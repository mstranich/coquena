---
status: complete
created: 2026-10-10
tags:
- m2
depends_on:
- 030-m2-model-update
created_at: 2026-10-10T16:36:44.729429400Z
updated_at: 2026-10-13T01:54:08.099985300Z
completed_at: 2026-10-13T01:54:08.099985300Z
transitions:
- status: planned
  at: 2026-10-10T16:48:26.888750100Z
- status: in-progress
  at: 2026-10-13T01:22:49.947583500Z
- status: complete
  at: 2026-10-13T01:54:08.099985300Z
---
# Aplicar actualizaciones y reubicaciones de unidades gestionadas

## Objetivo
Publicar la revisión planificada y sus renombres conservando identidad, referencias y
recuperación ante fallos.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [Reglas funcionales](../../docs/modelos-y-companions.md#seguimiento-y-actualización-de-gestionados)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [x] Aplicar planes resueltos por update con vigencia/commit comprobados; reutilizar
  contenido local verificado y publicar sólo conjuntos completos.
- [x] Probar actualización de shards sin mezcla de revisiones y renombre físico conservando
  ID/referencias sin sobrescribir archivos ajenos.
- [x] Bloquear unidades en uso antes de mover/descargar y coordinar con start; probar
  carreras, espacio insuficiente, cancelación y recuperación entre disco/índice.
- [x] Conservar instalación anterior ante fallos y limpiar sólo archivos propios después de
  publicar; respetar ubicaciones registradas/configuradas.
- [x] Cubrir alcance global por unidad y companion; no exigir atomicidad global ni implementar
  transferencia diferencial.
- [x] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
No cambia silenciosamente el commit del plan ni incorpora transferencia diferencial.

## Implementación y evidencia

Implementación aprobada, tarea cerrada y entregada en un único commit con código, pruebas, documentación y evidencia.
Dependencia 030 complete comprobada mediante board/search/view. Alcance resuelto con las
reglas canónicas de modelos, descargas, CLI y guía de implementación, sin decisiones
funcionales pendientes ni ampliación a la transición de externos.

- `model upgrade [nombre]` aplica planes resueltos de principales y companions;
  `model companion upgrade [nombre]` limita el ámbito a auxiliares. Cada unidad informa
  su resultado y GUID; una unidad fallida no impide tratar las restantes. Sin plan,
  externas y casos no resueltos se informan sin seleccionar otra revisión.
- Plan de operación ligado al registro de partida y al commit objetivo de update.
  Conserva ID, valores, referencias y ubicación de instalación registrada, aun si
  cambió el directorio configurado para altas futuras. Renombres y rutas nuevas
  reflejan el destino elegido sin sobrescribir rutas ajenas ni registros colisionantes.
- Reutilización sólo tras verificar SHA-256 local; preparación del conjunto completo,
  comprobación de cabeceras/shards y hashes antes de sustituir archivos. No hay
  transferencia diferencial ni deduplicación entre unidades.
- Reserva persistida bajo la misma exclusión de escritores usada por start; rechaza
  instancias activas o de identidad incierta antes de reservar/descargar. Start no
  adquiere la unidad reservada. Update/refresh/verify no invalidan el registro durante
  el trabajo. La prueba de runtime incluye instancia real de fixture y carrera
  concurrente entre start y creación de upgrade, con exactamente un ganador.
- Respaldo anterior completo y referencias de sus bytes observados en operation.json;
  no modifica la base de integridad anterior. Intento de movimiento persistido antes
  de sustituir cada ruta, copia temporal en el volumen destino y checkpoint posterior.
  Comprueba nuevamente todos los destinos, incluso los movimientos confirmados,
  antes de cambiar el índice. Una interrupción durante publicación bloquea el uso
  hasta reanudar o cancelar; no se oculta una mezcla física temporal como instalación.
- Resume aplica la misma selección fijada. Cancel restaura los archivos anteriores
  si el índice no cambió; después de publicarse conserva la nueva instalación y
  completa limpieza. Retira exclusivamente archivos anteriores propios comprobados;
  fallos de limpieza conservan operación y diagnóstico para reintentar.
- Espacio previo calculado para preparación, respaldo y destino, incluyendo
  ubicaciones en otro volumen. Espacio agotado durante I/O sigue siendo un fallo
  recuperable. No se descargaron pesos reales ni se iniciaron runtimes de llama.cpp.

### Validación ejecutada

- Suite específica administrada final: 100 comprobaciones, exit 0;
  artifacts/task031-checks/model-upgrade-managed.txt.
- Regresión administrada durante implementación: exit 0; managed-tests.txt.
- CLI y runner Native AOT win-x64 publicados con advertencias AOT/trimming como errores;
  publish-product.txt y publish-runner.txt.
- Suite específica nativa: 101 comprobaciones, exit 0 y stderr vacío;
  model-upgrade-tests.txt y model-upgrade-errors.txt; incluye el CLI publicado.
- Regresión completa con runner y CLI Native AOT finales: exit 0 y stderr vacío;
  native-tests.txt y native-errors.txt. Incluye procesos reales de fixture, carrera
  start/upgrade, contratos, almacenamiento, backends, descargas, HF/GGUF, instalación,
  directorios, externos, mantenimiento local, update y upgrade.
- Documentación: 39 diagramas PlantUML, 3 ejemplos JSON y enlaces locales válidos;
  docs.txt. git diff --check sin errores.
- Fixtures de bytes/GGUF y homes temporales, sin consultas HF reales ni inferencia.
  Las pruebas de procesos usan el ejecutable de fixture del runner.

### Reproducción

```powershell
dotnet run --project tests/Coquena.Tests -c Release -- --model-upgrade-tests
dotnet run --project tests/Coquena.Tests -c Release --no-restore
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o artifacts/task031-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -o artifacts/task031-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task031-checks/run-native.ps1 -Filter '--model-upgrade-tests' -OutputName 'model-upgrade'
& artifacts/task031-checks/run-native.ps1 -OutputName 'native'
& artifacts/task031-checks/validate-docs.ps1
git diff --check
```

Scripts y logs locales en artifacts/task031-checks/. El script nativo normaliza
Path/PATH al iniciar los procesos, conservando el entorno.
