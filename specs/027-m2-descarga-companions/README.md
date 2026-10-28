---
status: archived
created: 2026-10-10
tags:
- m2
created_at: 2026-10-10T16:36:43.597969200Z
updated_at: 2026-10-28T10:21:41.654016800Z
completed_at: 2026-10-12T22:15:00.530635800Z
transitions:
- status: planned
  at: 2026-10-10T16:48:26.555552600Z
- status: in-progress
  at: 2026-10-12T21:53:32.267663500Z
- status: complete
  at: 2026-10-12T22:15:00.530635800Z
- status: archived
  at: 2026-10-28T10:21:41.654016800Z
---
# Descargar y catalogar companions gestionados

## Objetivo
Instalar unidades auxiliares de forma independiente del modelo principal y de los presets.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [Reglas funcionales](../../docs/modelos-y-companions.md#companions)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [x] Implementar companion download/list reutilizando selección de 015 y publicación de 009,
  con tipo y cuantización independientes.
- [x] Probar menú, README a pedido, cancelación antes de GUID y ausencia de candidatos en
  commit fijo.
- [x] Descargar sólo unidad auxiliar/partes y publicar conjunto completo en ubicación
  configurada; probar pausa/reanudación/fallos/colisiones con otras unidades.
- [x] No descargar principal, asociar presets automáticamente ni certificar compatibilidad de
  inferencia.
- [x] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
No descarga principales ni asocia presets automáticamente. Registro externo, actualización,
retirada y referencias tienen entregas propias.

## Implementación y evidencia

Implementación y evidencia aprobadas por el usuario. Se cierra la tarjeta y se entrega
código, pruebas, documentación y evidencia en un único commit.

- `model companion download usuario/repositorio#tipo[:cuantización]` reutiliza
  selección/inspección de 015 y publicación/recuperación de 009. Tipos: mmproj y mtp.
  El tipo es obligatorio; `model download` rechaza auxiliares antes de consultar la red.
- Operaciones `companion` fijan commit, archivos y directorio de instalación.
  `download list --companion` las distingue de principales; pausa/reanudación/cancelación
  reutilizan el GUID y el consumidor de instalación existente.
- Catálogo gestionado con tipo y cuantización propios; listados separados. Reservas de
  nombres/rutas entre principales, auxiliares y registro externo. No crea presets ni
  descarga principales; no certifica inferencia o compatibilidad entre unidades.

### Validación ejecutada

- Regresión administrada completa: exit 0. Suite específica final: **154 comprobaciones**.
  Incluye ambos tipos, shards, ubicación configurada, menú/README/cancelación/ausencia de
  candidatos, pausa, hash incorrecto, cancelación, colisiones y recuperación sin red.
- CLI y runner publicados Native AOT win-x64 con errores estrictos de AOT/trimming.
  Suite específica con ambos binarios nativos: **160 comprobaciones**, exit 0 y stderr vacío.
- Regresión completa con runner y CLI nativos: exit 0 y stderr vacío; incluye contratos,
  configuración, procesos, persistencia, transferencias, backends, HF/GGUF, instalación,
  traslado y registro externo. Logs `artifacts/task027-checks/native-tests.txt` y
  `native-errors.txt`.
- Documentación: **39 diagramas PlantUML**, **3 ejemplos JSON** y enlaces locales válidos.
  `git diff --check` sin errores.
- Board/search/view revisados. Sin depends-on declarado; 009 y 015 están complete.
  `leanspec deps` no está migrado al adaptador; se revisaron las tarjetas manualmente.
- Fixtures pequeños y homes temporales. Sin pesos reales, servidores ni inferencia.

### Reproducción

Desde la raíz del checkout:

```powershell
dotnet run --project tests/Coquena.Tests -c Release -- --model-install-tests
dotnet run --project tests/Coquena.Tests -c Release --no-restore
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o artifacts/task027-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -o artifacts/task027-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task027-checks/run-native.ps1 -Filter '--model-install-tests' -OutputName 'model-install'
& artifacts/task027-checks/run-native.ps1 -OutputName 'native'
& artifacts/task027-checks/validate-docs.ps1
git diff --check
```

Scripts y logs locales en `artifacts/task027-checks/`; el script de ejecución conserva
el entorno y normaliza Path/PATH antes de iniciar procesos nativos. Alcance y reglas
resueltos con tarjeta/documentación canónica, sin decisiones pendientes de consulta.
