---
status: complete
created: 2026-10-10
tags:
- m2
depends_on:
- 026-m2-registro-modelos-externos
- 027-m2-descarga-companions
created_at: 2026-10-10T16:36:43.892788900Z
updated_at: 2026-10-12T22:52:12.419629900Z
completed_at: 2026-10-12T22:52:12.419629900Z
transitions:
- status: planned
  at: 2026-10-10T16:48:26.642155600Z
- status: in-progress
  at: 2026-10-12T22:16:30.475274700Z
- status: complete
  at: 2026-10-12T22:52:12.419629900Z
---
# Actualizar metadatos y verificar integridad local

## Objetivo
Exponer observaciones locales e integridad como operaciones explícitas y diferenciadas.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [Reglas funcionales](../../docs/modelos-y-companions.md#integridad-local)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [x] Implementar refresh/verify de modelos y companions; refresh anota observaciones sin red
  ni reemplazar hashes o afirmar integridad.
- [x] Comparar SHA-256 completos por archivo/conjunto con discrepancia, referencia ausente,
  error de lectura y shards faltantes; no reparar ni rebasar referencias.
- [x] Implementar --set-baseline sólo para externos con advertencia de alcance; lectura
  fallida o archivo cambiante no produce base exitosa.
- [x] Persistir resultado/fecha y separar disponibilidad/seguimiento/integridad; reutilizar
  008 con fixtures pequeños.
- [x] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
No consulta HF, repara archivos ni sustituye referencias de integridad implícitamente.

## Implementación y evidencia

Implementación aprobada; código, pruebas, documentación y evidencia se entregan en un único commit.

- `model refresh` y `model companion refresh` observan tamaño/fecha de archivos
  legibles y reevaluan GGUF/disponibilidad. Conservan referencias, seguimiento y la
  última observación de integridad; los archivos inaccesibles retienen sus observaciones.
- `verify` reutiliza SHA-256 completo de 008 con memoria acotada, sin red. Guarda fecha,
  resultado por archivo y resultado del conjunto: verified, mismatch, no-reference,
  missing-file, read-error o changed-during-read. El listado muestra el resultado histórico.
- `--set-baseline` sólo para externos: advierte que no certifica procedencia ni corrección
  original y produce baseline-set, distinguido de verified. Publica todas las referencias
  juntas; un fallo o cambio detectado conserva la base anterior completa. La fecha de
  establecimiento de la base se conserva aunque se realicen comparaciones posteriores.
- Lectura fuera del bloqueo de escritura; al guardar se revalida el registro original.
  Una modificación concurrente produce model-changed y conserva el cambio ajeno.
  Cancelar la lectura no publica resultados parciales. No escribe pesos ni sidecars.
- Dependencias 026 y 027 revisadas, ambas complete. `leanspec deps` sigue sin migrar al
  adaptador; revisión manual con board/search/view y metadatos de la tarjeta.

### Validación

- Suite específica administrada: 21 comprobaciones, exit 0; cubre comparación, base
  opcional, sustitución explícita, bloqueo, ausencia de shards, conservación de bases,
  cambio durante lectura del conjunto, concurrencia, cancelación, companions y gestionados.
- Regresión administrada completa: exit 0; log `artifacts/task028-checks/managed-tests.txt`.
- CLI y runner publicados Native AOT win-x64, sin advertencias, con errores estrictos
  de AOT/trimming. Suite específica con runner/CLI nativos: 28 comprobaciones, exit 0,
  stderr vacío; logs `local-integrity-tests.txt` y `local-integrity-errors.txt`.
- Regresión completa con runner y CLI nativos finales: exit 0, stderr vacío; logs
  `artifacts/task028-checks/native-tests.txt` y `native-errors.txt`.
- Documentación: 39 diagramas PlantUML, 3 ejemplos JSON y enlaces locales válidos.
- `git diff --check` sin errores; board/search/view y dependencias revisados.
- Fixtures GGUF pequeños y homes temporales; sin descarga de pesos reales, servidores
  de inferencia ni consultas HF para refresh/verify.

### Reproducción

Desde la raíz del checkout:

```powershell
dotnet run --project tests/Coquena.Tests -c Release -- --local-integrity-tests
dotnet run --project tests/Coquena.Tests -c Release --no-restore
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o artifacts/task028-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -o artifacts/task028-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task028-checks/run-native.ps1 -Filter '--local-integrity-tests' -OutputName 'local-integrity'
& artifacts/task028-checks/run-native.ps1 -OutputName 'native'
& artifacts/task028-checks/validate-docs.ps1
git diff --check
```

Scripts/logs locales en `artifacts/task028-checks/`; el runner normaliza Path/PATH
al iniciar procesos nativos. Sin ambigüedades pendientes de consulta.
