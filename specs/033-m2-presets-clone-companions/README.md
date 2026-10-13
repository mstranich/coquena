---
status: complete
created: 2026-10-10
tags:
- m2
depends_on:
- 017-configuracion-retencion-logs
- 027-m2-descarga-companions
created_at: 2026-10-10T16:36:45.595954800Z
updated_at: 2026-10-13T15:06:35.955454500Z
completed_at: 2026-10-13T15:06:35.955454500Z
transitions:
- status: planned
  at: 2026-10-10T16:48:27.055578400Z
- status: in-progress
  at: 2026-10-13T14:19:57.751421500Z
- status: complete
  at: 2026-10-13T15:06:35.955454500Z
---
# Clonar presets y resolver referencias a companions

## Objetivo
Reutilizar ajustes mediante clones independientes y resolver referencias explícitas a unidades
auxiliares.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [Reglas funcionales](../../docs/modelos-y-companions.md#presets)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [x] Implementar preset clone a nombre libre conservando modelo o usando --model; copiar
  valores/referencias sin archivos ni herencia y probar independencia.
- [x] Guardar vínculos bind por ID y distinguirlos de literales; resolver rutas registradas al generar
  INI por instancia, incluidos shards y reubicaciones.
- [x] Comprobar referencias/disponibilidad antes de iniciar; conservar presets no servibles
  con diagnóstico y rechazar inicio.
- [x] Probar companion compartido y referencias estables ante renombres sin duplicar propiedad
  ni certificar inferencia.
- [x] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
No descarga ni asocia companions automáticamente; asignación básica corresponde a 017.

## Decisiones de alcance acordadas

El usuario eligió `preset bind <preset> <parámetro> <recurso>` en lugar de `set --ref`.
Set conserva la asignación literal; bind mantiene el ID del recurso, sin introducir un
nuevo tipo público de parámetro. Unificación de comandos/tipos y clasificación semántica
de companions frente a métodos de especulación quedan como hallazgos para evaluación,
sin ampliar esta implementación ni crear otros registros o tarjetas.

El vínculo pertenece al preset y al parámetro, no al modelo globalmente. La selección
de backend permanece en el servidor. Se pueden vincular companions o principales usados
como auxiliares; bind model conserva la restricción de principal para ModelId. Los
parámetros sin definición se admiten con el diagnóstico existente de soporte desconocido;
los definidos deben ser texto de ámbito modelo. No se deduce compatibilidad ni método
de especulación por el nombre/tipo de una unidad.

## Implementación y evidencia — 13 de octubre de 2026

- Clone publica un ID nuevo y copias independientes de valores/vínculos, con el modelo
  original o el indicado mediante --model. No copia archivos ni asociaciones de servidor.
- Bind almacena `bindings: { parámetro: GUID }`, opcional en el contrato de presets;
  los documentos existentes sin ese campo siguen siendo válidos. ModelId conserva la
  referencia al principal. Set/bind se reemplazan mutuamente para el mismo parámetro;
  unset retira el literal o vínculo auxiliar. Show/list muestran nombre e ID y diagnósticos
  locales; referencias rotas conservan sus IDs y no se reconectan por nombre.
- Composición resuelve las rutas actuales, comprueba GGUF/shards y rechaza inicio ante
  recursos efectivos ausentes/incompletos/inválidos. Omisión por soporte no admitido
  conserva el vínculo con warning; soporte desconocido conserva el argumento. Los IDs
  emitidos entran en ResourceIds y reutilizan reservas/protecciones existentes de runtime.
- La cascada de definiciones incluye vínculos y servidores afectados: dry-run informa
  IDs, cancelar conserva el estado y confirmar publica la retirada/incompatibilidad.
  Alias se normalizan; definir un tipo incompatible sobre vínculos existentes se rechaza.
- Actualizados CLI, ayuda y reglas/diagramas funcionales para la decisión aprobada de bind,
  sin introducir clasificación automática de métodos ni certificación de inferencia.

Validaciones ejecutadas:

- Suite dirigida administrada: **119 comprobaciones** de configuración/presets/INI,
  exit 0. Incluye clones, destino alternativo, independencia, colisiones, aliases,
  reemplazo literal/vínculo, companion compartido, primer shard, renombre/reubicación,
  referencia rota/reuso de nombre, soporte omitido, cascada y fallos de escritura.
- Ciclo de procesos dirigido: **81 comprobaciones**, exit 0; inicio bloqueado por companion
  ausente, ID/ruta persistidos en instancia real fixture y upgrade bloqueado mientras
  ese companion está en uso.
- Regresión administrada completa final: exit 0, log `artifacts/task033-checks/managed-tests.txt`.
- CLI y runner publicados Native AOT win-x64, sin advertencias y con análisis estricto.
  Regresión nativa completa con CLI publicada: exit 0, **119 comprobaciones** de presets
  y **81** del ciclo de procesos, además del resto de suites; stderr vacío. Logs
  `artifacts/task033-checks/native-tests.txt` y `native-errors.txt`.
- Documentación: **39 diagramas PlantUML**, **3 ejemplos JSON** y enlaces locales válidos.
  `git diff --check`: exit 0. Board/search/view y dependencias 017/027 revisados manualmente;
  `leanspec deps` continúa sin migrar al adaptador.

Reproducción desde la raíz:

```powershell
dotnet run --project tests/Coquena.Tests -c Release -- --configuration-tests
dotnet run --project tests/Coquena.Tests -c Release -- --runtime-tests
dotnet run --project tests/Coquena.Tests -c Release --no-restore
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o artifacts/task033-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -o artifacts/task033-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task033-checks/run-native.ps1
& artifacts/task033-checks/validate-docs.ps1
git diff --check
```

Una repetición administrada mediante `dotnet .../Coquena.Tests.dll` falló al lanzar
los procesos hijos de StorageTests: el runner utiliza Environment.ProcessPath, que
en esa forma identifica dotnet y no su apphost. Relanzar el .exe final pasó la suite
completa; no se modificó producción ni el harness para ocultar el error de invocación.
El script nativo normaliza Path/PATH antes de iniciar el runner.

Pruebas con homes temporales, archivos GGUF mínimos y procesos fixture. No se modificó
D:\Coquena ni se descargaron pesos o ejecutó inferencia real. Sin paquetes nuevos ni
implementación de otras tarjetas. El usuario aprobó la entrega el 13 de octubre de 2026
y solicitó cerrar la tarea y crear un único commit con código, pruebas, documentación
y evidencia. Estado actualizado a complete mediante LeanSpec.
