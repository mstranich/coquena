---
status: archived
created: 2026-10-10
tags:
- m2
depends_on:
- 026-m2-registro-modelos-externos
created_at: 2026-10-10T16:36:44.181081300Z
updated_at: 2026-10-28T10:21:41.656703600Z
completed_at: 2026-10-13T00:43:11.721947900Z
transitions:
- status: planned
  at: 2026-10-10T16:48:26.722191300Z
- status: in-progress
  at: 2026-10-12T22:54:44.568434100Z
- status: complete
  at: 2026-10-13T00:43:11.721947900Z
- status: archived
  at: 2026-10-28T10:21:41.656703600Z
---
# Emparejar externos mediante selección asistida

## Objetivo
Asistir el emparejamiento remoto opcional conservando siempre el registro local ya creado.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [Reglas funcionales](../../docs/modelos-y-companions.md#incorporación-externo-o-gestionado)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [x] Completar --try-match en altas de principal/companion; sin flag no consultar red y
  persistir el alta antes del menú.
- [x] Mostrar candidatos/razones y selección o cancelación explícita; guardar equivalencia
  informativa sin cambiar ID/nombre/propiedad ni habilitar upgrade.
- [x] Probar ambigüedad, ausencia de candidatos, fallo remoto y falta de interacción
  conservando alta local; consultas acotadas sin descargar pesos.
- [x] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
No certifica procedencia o identidad de bytes ni convierte externos en gestionados.

## Implementación y evidencia

Entrega aprobada y completada.

- `--try-match` funciona en el alta de principales, mmproj y mtp. Alta local persistida
  antes de consultas/menú; sin flag o sin interacción no consulta HF.
- Consulta sólo API de revisión e inventario del repositorio declarado, fijado a commit,
  con límites existentes de tamaño/paginación y presupuesto total remoto de 30 segundos.
  Menú hasta 128 candidatos; sin descargas de pesos ni hashes completos.
- Candidatos por rol/cuantización remotos, número de partes y tamaños, descartando
  contradicciones conocidas con cabeceras locales. Shards completos como unidad.
- Elección o cancelación explícitas, incluso con un candidato. `InformativeMatch` conserva
  candidato, razones y fecha por separado de `Selection`, que sigue null en externos.
  Conserva ID, nombre, rutas, parámetros, propiedad e integridad. Listado no certificado.
- Cancelación, selección inválida, ausencia de candidatos, unidad local incompleta,
  fallo remoto y falta de interacción conservan alta local, exit 0. Guardado revalida
  el registro original bajo el bloqueo de escritura; cambio concurrente no se sobrescribe.
- Dependencia 026 complete revisada mediante board/search/view. `leanspec deps` sigue
  sin migrar al adaptador; relaciones revisadas manualmente.

### Validación

- Suite específica administrada: 22 comprobaciones, exit 0. Regresión administrada
  completa: exit 0; `artifacts/task029-checks/managed-tests.txt`.
- Publicaciones CLI y runner Native AOT win-x64 sin advertencias, con errores estrictos
  de AOT/trimming; logs `publish-product.txt` y `publish-runner.txt`.
- Suite específica nativa: 24 comprobaciones, exit 0, stderr vacío;
  `external-matching-tests.txt` y `external-matching-errors.txt`.
- Regresión completa con runner y CLI nativos finales: exit 0, stderr vacío;
  `artifacts/task029-checks/native-tests.txt` y `native-errors.txt`.
- Documentación: 39 diagramas PlantUML, 3 ejemplos JSON y enlaces locales válidos;
  `artifacts/task029-checks/docs.txt`. `git diff --check` sin errores.
- Fixtures HTTP/GGUF y homes temporales; sin consultas HF reales, pesos reales ni
  servidores de inferencia. No certifica equivalencia por bytes o reconstruye procedencia.

### Reproducción

```powershell
dotnet run --project tests/Coquena.Tests -c Release -- --external-matching-tests
dotnet run --project tests/Coquena.Tests -c Release --no-restore
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o artifacts/task029-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -o artifacts/task029-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task029-checks/run-native.ps1 -Filter '--external-matching-tests' -OutputName 'external-matching'
& artifacts/task029-checks/run-native.ps1 -OutputName 'native'
& artifacts/task029-checks/validate-docs.ps1
git diff --check
```

Scripts y logs locales en `artifacts/task029-checks/`. Sin ambigüedades pendientes.