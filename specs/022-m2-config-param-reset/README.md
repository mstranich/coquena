---
status: complete
created: 2026-10-10
priority: medium
tags:
- m2
depends_on:
- 018-m2-param-define
created_at: 2026-10-10T15:24:01.531947400Z
updated_at: 2026-10-12T05:21:30.295983Z
completed_at: 2026-10-12T05:21:30.295983Z
transitions:
- status: planned
  at: 2026-10-10T15:24:43.424765200Z
- status: in-progress
  at: 2026-10-11T20:21:06.654742900Z
- status: complete
  at: 2026-10-12T05:21:30.295983Z
---
# Restablecer el catálogo de parámetros de fábrica

## Objetivo
Implementar config param-reset y su consulta de impacto para recuperar el catálogo
suministrado por Coquena y ajustar las asignaciones afectadas con confirmación.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [CLI](../../docs/comandos.md#config)
- [Restablecimiento de fábrica](../../docs/catalogo-de-parametros.md#restablecimiento-de-fábrica)
- [Informe de impacto](../../docs/catalogo-de-parametros.md#consulta-del-impacto)

## Trabajo
- [x] Resolver el catálogo de fábrica suministrado con la versión instalada.
- [x] Reutilizar el análisis de impacto y las garantías de persistencia de 018.
- [x] Implementar el comando, --dry-run, confirmación y ayuda.

## Aceptación
- [x] Restablecer definiciones iniciales editadas y eliminadas, y retirar definiciones
  añadidas, manteniendo host, port y metrics protegidos.
- [x] Conservar asignaciones válidas y comprobar la cascada de parámetros retirados
  y valores incompatibles en varios presets y servidores.
- [x] Verificar que los cambios de default no sobrescriben asignaciones y distinguir
  el cambio de aplicabilidad del borrado de valores.
- [x] Guardar el informe de --dry-run mediante redirección, sin escritura de estado
  ni interacción; comparar con la operación real sobre el mismo estado.
- [x] Comprobar confirmación, cancelación y ausencia de entrada interactiva.
- [x] Verificar consistencia ante fallos, conservación de entidades y archivos, y
  ausencia de cambios sobre instancias activas.
- [x] Actualizar ayuda, ejecutar pruebas pertinentes y publicar/ejecutar Native AOT
  win-x64 con evidencia reproducible.
- [x] Validar documentación afectada y git diff --check.

## Límites y dependencia
Depende de 018 para reutilizar la gestión del catálogo, el análisis de impacto y la
cascada confirmada. No restablece otras políticas del manager ni instala o elimina
backends, modelos o companions.

## Evidencia — 11 de octubre de 2026

Implementado `config param-reset [--dry-run]` sobre el catálogo de fábrica compilado
con esta versión de Coquena. Recupera definiciones eliminadas, restablece los metadatos
y asociaciones iniciales y retira las definiciones añadidas. Conserva las definiciones
protegidas y los valores compatibles; no copia defaults sobre asignaciones existentes.
La documentación funcional ya describe estas reglas y no requirió cambios.

El restablecimiento comparte el análisis por definición, el informe y el ciclo de
confirmación/publicación de 018. Calcula todos los efectos antes de publicar una única
operación consistente entre parámetros, presets y servidores. La identidad previa de
las asignaciones se conserva al cambiar alias; las asignaciones desconocidas que
coinciden con una definición recuperada se validan también, incluidos sus alias.
Los cambios de aplicabilidad se informan como valores conservados con su nueva condición
de emisión, separadamente del borrado por incompatibilidad o retirada.

La confirmación se exige incluso cuando no hay cascada o el catálogo ya coincide con
fábrica. Se obtiene fuera del bloqueo de escritura; un cambio concurrente obliga a
recalcular y confirmar nuevamente. Dry-run no confirma, publica ni recupera checkpoints.
El reset no modifica recursos, archivos de ejecución ni otras políticas del manager.

Validaciones ejecutadas:
- Suite administrada completa: exit 0. Suite dirigida final de parámetros: 174
  comprobaciones, exit 0.
- CLI y runner publicados Native AOT win-x64 con análisis estricto, sin advertencias.
- Suite nativa completa contra la CLI publicada: exit 0; incluye 174 comprobaciones
  de parámetros, 73 de configuración/presets/INI, 72 de servidores y 70 de procesos,
  además de los demás recorridos existentes.
- Recuperación de definiciones y aliases, retirada de personalizados en varios presets
  y servidores, defaults, aplicabilidad, confirmación/cancelación, ausencia de entrada,
  gramática inválida y comparación del informe con la operación real.
- Fallos antes del commit, después de publicar y entre checkpoints: conservación del
  estado previo o lectura coherente del estado publicado y recuperación del checkpoint
  por el siguiente escritor. Entidades, recursos y archivos de ejecución preservados.
- CLI nativa ejecutada en procesos independientes para consultas y rechazo sin entrada.
  Dry-run redirigido a `artifacts/task022-checks/impacto-reset.txt` sobre un home inexistente:
  exit 0, sin crear ese home. Ayuda del ejecutable publicado comprobada.
- Validador documental: 37 diagramas PlantUML, tres ejemplos JSON y enlaces locales,
  incluidos los de esta tarjeta. `git diff --check`: exit 0.

Comandos de reproducción:

```powershell
dotnet run --project tests/Coquena.Tests --no-restore
dotnet run --project tests/Coquena.Tests --no-restore -- --parameter-tests
dotnet publish src/Coquena/Coquena.csproj --no-restore -c Release -r win-x64 -o artifacts/task022-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj --no-restore -c Release -r win-x64 -o artifacts/task022-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task022-tests-native/Coquena.Tests.exe artifacts/task022-native/coquena.exe
& artifacts/task022-native/coquena.exe --help
git diff --check
```

En este host se ejecutó el runner nativo con `ProcessStartInfo` y una copia del entorno
normalizada mediante `OrdinalIgnoreCase`, para evitar duplicados Path/PATH. Las pruebas
usan homes temporales y recursos fixture; no se modificó `D:\Coquena`, no se descargaron
pesos ni se ejecutó inferencia real. Los archivos de ejecución preservados en las pruebas
son fixtures; no se necesita modificar una instancia real para comprobar el reset.
Sin paquetes nuevos ni ampliación de alcance.

Implementación y evidencia listas para revisión. La tarjeta permanece `in-progress`
hasta la aprobación explícita; no se realizó commit.
