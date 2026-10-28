---
status: archived
created: 2026-10-10
tags:
- m2
depends_on:
- 023-m2-backends-externos
- 026-m2-registro-modelos-externos
- 027-m2-descarga-companions
created_at: 2026-10-10T16:36:45.885192400Z
updated_at: 2026-10-28T10:21:41.663253200Z
completed_at: 2026-10-13T15:45:09.414100300Z
transitions:
- status: planned
  at: 2026-10-10T16:48:27.134655700Z
- status: in-progress
  at: 2026-10-13T15:21:12.960650200Z
- status: complete
  at: 2026-10-13T15:45:09.414100300Z
- status: archived
  at: 2026-10-28T10:21:41.663253200Z
---
# Retirar recursos según propiedad y uso

## Objetivo
Retirar instalaciones y registros según propiedad, uso activo y resultados reales del borrado.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [Reglas funcionales](../../docs/modelos-y-companions.md#quitar-un-modelo-o-companion)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [x] Implementar backend uninstall, model remove y companion remove; externos pierden
  registro sin tocar archivos y gestionados eliminan sólo archivos propios.
- [x] Bloquear uso activo con diagnóstico; conservar presets/selecciones como referencias
  rotas sin reasignar automáticamente.
- [x] Contar bytes efectivamente liberados y conservar propiedad/información para reintentar
  eliminaciones parciales sin anunciar éxito completo.
- [x] Probar shards, companion compartido, backend default/pin, nombres reutilizados y fallos;
  conservar archivos vecinos y carpetas no vacías.
- [x] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
No implementa limpieza global ni retención de builds, cubierta por 025.

## Implementación y revisión

- `backend uninstall <id>`, `model remove <nombre>` y `model companion remove <nombre>`
  comparten retirada bajo la exclusión de escritores del home. Externos sólo pierden
  registro; gestionados borran su inventario. No se cambian presets, pins ni selecciones.
- Backends fijan `OwnedFiles` antes de publicar la instalación, incluidos los archivos
  de todos los artefactos y la marca existente. El inventario viaja con el plan recuperable
  y con el catálogo; no se descubre propiedad enumerando el directorio al desinstalar.
- `RemovalPending` se persiste antes del primer borrado. El registro, ID, nombre e
  inventario permanecen hasta terminar; el mismo comando reintenta. Se bloquean inicio,
  modificación de la instalación, traslado y limpieza automática del registro pendiente.
- Instancias activas o con identidad incierta informan sus IDs. Operaciones pendientes
  reservan el recurso y deben reanudarse/cancelarse. Conflictos de propiedad entre
  registros se rechazan antes del borrado.
- Cada intento informa archivos eliminados, pendientes con causas y bytes realmente
  eliminados. Ausencias previas no suman bytes. Un fallo del último guardado del catálogo
  conserva y reporta los bytes del intento sin anunciar finalización.
- El usuario autorizó cambios incompatibles en alfa y descartó retrocompatibilidad.
  No se implementa migración de catálogos ni de operaciones anteriores sin inventario.
  La orientación futura sobre TUI/recetas YAML no amplía esta entrega.
- Documentación funcional: se concreta la representación de retirada parcial que estaba
  pendiente de diseño, conservando las reglas de propiedad, reintento y referencias.
  El bloqueo de recursos pendientes y reserva de nombre mantienen ese estado recuperable.

### Hallazgo para evaluación

La retirada explícita usa el inventario y conserva archivos agregados después. La
retención automática y la limpieza de reinstalaciones preexistentes siguen borrando
slots completos mediante `BackendPaths.RemoveOwnedTree`. Es una diferencia comprobada
en el código; conservar archivos añadidos en esos recorridos requiere evaluación propia.
Esta entrega sólo protege de retención al registro con retirada parcial, sin ampliar
la tarea a otras políticas de limpieza ni crear nuevas tarjetas. El inventario queda
disponible para esa evaluación y para futuras definiciones declarativas.

### Evidencia ejecutada

- Suite de retirada administrada: **38 comprobaciones**, exit 0.
- Regresión administrada completa: exit 0; incluye la cobertura de inventario en
  instalación de backend y las suites previas de modelos, procesos y persistencia.
- Producto y runner publicados Native AOT win-x64 con errores estrictos de AOT/trimming.
- Suite de retirada con runner y producto nativos: **39 comprobaciones**, exit 0,
  stderr vacío; incluye una invocación de `model remove` al producto publicado.
- Regresión completa con runner y producto nativos: exit 0 y stderr vacío.
  Logs `native-tests.txt` y `native-errors.txt`; incluye instalación/recuperación,
  retención, directorios, procesos, integridad y actualización de modelos/backends.
- Documentación: **39 diagramas PlantUML**, **3 ejemplos JSON** y enlaces locales válidos.
- `git diff --check` sin errores.
- Fixtures y homes temporales; sin descargas de pesos reales ni inferencia. El bloqueo
  activo usa un proceso Windows real suspendido con identidad/job verificables.
- Dependencias 023, 026 y 027 complete, revisadas con board/search/view.
  `leanspec deps` no está migrado al adaptador; revisión manual.

### Reproducción

Desde la raíz del checkout:

```powershell
dotnet run --project tests/Coquena.Tests -c Release -- --removal-tests
dotnet run --project tests/Coquena.Tests -c Release --no-build
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o artifacts/task034-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj -c Release -r win-x64 -o artifacts/task034-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task034-checks/run-native.ps1 -Filter '--removal-tests' -OutputName 'removal'
& artifacts/task034-checks/run-native.ps1
& artifacts/task034-checks/validate-docs.ps1
git diff --check
```

Scripts y logs locales en `artifacts/task034-checks/`; los lanzamientos nativos
normalizan Path/PATH sin descartar el entorno heredado.

Implementación, documentación, pruebas, evidencia y reporte de hallazgos aprobados
por el usuario. Se cierra la tarjeta y se entrega todo lo aprobado en un único commit.
