---
status: archived
created: 2026-10-10
tags:
- m2
depends_on:
- 024-m2-backend-update
created_at: 2026-10-10T16:36:43.026561600Z
updated_at: 2026-10-28T10:21:41.651444800Z
completed_at: 2026-10-12T17:06:00.017104900Z
transitions:
- status: planned
  at: 2026-10-10T16:48:26.391011700Z
- status: in-progress
  at: 2026-10-12T14:48:49.136504200Z
- status: complete
  at: 2026-10-12T17:06:00.017104900Z
- status: archived
  at: 2026-10-28T10:21:41.651444800Z
---
# Actualizar backends y aplicar retención de builds

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

## Objetivo
Actualizar seguimientos latest con el build detectado y aplicar la política de retención de
instalaciones anteriores.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [Reglas funcionales](../../docs/servidores.md#definición-persistida)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [x] Aplicar uno o todos los planes latest usando el build detectado por update sin resolver
  otro silenciosamente.
- [x] Reutilizar transferencia, validación y publicación del instalador; conservar instalación
  anterior ante fallos y probar pausa/reanudación/cancelación por unidad.
- [x] Actualizar build actual para futuros inicios sin cambiar instancias activas o pins ni
  actualizar externos.
- [x] Probar retención por origen/familia/plataforma, límite inicial y protección de
  actual/fijados/en uso, con diagnóstico si los protegidos exceden el límite.
- [x] Verificar config backend-retention show/set/unset, persistencia del límite y
  origen informado; rechazar valores inválidos sin cambios. Consultar/cambiar el límite
  no elimina builds: la limpieza se aplica tras un upgrade exitoso.
- [x] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
No actualiza externos ni amplía familias oficiales; la limpieza conserva instalaciones
protegidas.

## Implementación y decisiones

- `backend upgrade [selector-latest]` recorre todos los seguimientos oficiales o
  uno seleccionado, con alias `oficial` y selectores de 024. Pins y externos no
  son unidades a actualizar. Se exige una observación vigente de update; sin
  novedades no descarga ni limpia. Un fallo por unidad no impide tratar las demás.
- El plan fija build, artefactos y observación de update. Antes de crear y publicar
  se comprueban observación y seguimiento; una consulta o instalación concurrente
  invalida el plan obsoleto. El transporte existente consulta únicamente el release
  fijado para resolver URLs efímeras y verificar nombres/tamaños, sin resolver latest.
- Según la decisión aprobada por el usuario el 12 de octubre de 2026, un candidato
  ya instalado se reutiliza tras validar árbol, marcador propio, versión y estabilidad
  del ejecutable. Conserva ID, pin y archivos. Una validación fallida no reinstala
  implícitamente. El cambio sólo modifica el seguimiento para futuros inicios.
- Los builds nuevos reutilizan `BackendInstaller` y `DownloadManager`, incluyendo
  pausa, Range, reanudación y cancelación por GUID. `BackendInstallPlan.Upgrade`
  conserva la observación para la comprobación de publicación y para ejecutar
  retención también al reanudar. Un upgrade pendiente no crea una operación duplicada.
- `config backend-retention show/set/unset` guarda un entero positivo, inicialmente
  cinco. La marca opcional `backendRetention.own` distingue un ajuste propio igual
  al inicial sin romper documentos anteriores. Cambiar el límite no limpia archivos.
- La retención cuenta builds distintos por distribución/familia/plataforma; las
  variantes CUDA comparten su familia. Conserva actual, pins, referencias fijadas,
  instancias verificadas o inciertas, referencias externas sobre el slot y operaciones
  pendientes. Los protegidos pueden superar el límite y generan un diagnóstico.
- La cola `catalogs/backend-retention.json` se publica antes de retirar un registro.
  Sólo se retiran slots reconocidos y propios, comprobando enlaces y acceso. Una
  retirada ya comprometida permanece en la cola hasta terminar su borrado físico;
  otra limpieza reintenta ese trabajo. Para instalaciones descargadas, el GUID
  original permanece en cleanup hasta terminar. La limpieza fallida no revierte
  el build publicado. La reutilización con limpieza pendiente se diagnostica y su
  cola se reintenta tras otro upgrade exitoso.
- Las consultas de operaciones dentro de la exclusión usan un snapshot puro, sin
  intentar adquirir de nuevo el bloqueo ni recuperar un ejecutor desde ese snapshot.
  Sin paquetes nuevos ni cambios en modelos, companions o retiradas explícitas.

## Evidencia

- Suite administrada completa y suite completa con runner/producto Native AOT:
  exit 0, stderr vacío. Resultados en `artifacts/task025-checks/managed-tests.txt`
  y `native-tests.txt`, con sus correspondientes archivos `*-errors.txt` vacíos.
- Tras añadir la comprobación final de separación por origen/distribución/plataforma,
  suite específica final: **45 comprobaciones administradas y 51 nativas**, exit 0,
  stderr vacío. Resultados `upgrade-managed-tests.txt` y `upgrade-native-tests.txt`.
  Publicación estricta final del runner en `artifacts/task025-tests-final-native`.
- Reutilización de pin instalado sin transferencias, validación rechazada sin
  reinstalación implícita, futuros inicios/latest y default fijo conservado.
  Native CLI verifica un ejecutable real mediante `--version`, su reutilización,
  configuración persistida, argumentos inválidos y ayuda.
- Builds nuevos, pausa, checkpoint parcial y reanudación HTTP Range del mismo build,
  cancelación, fallo de transferencia/validación, update concurrente que invalida
  publicación y continuación de otras unidades. Interrupciones después del movimiento
  y del reemplazo del catálogo se reanudan sin red.
- Retención de los builds más antiguos, límite inicial y ajustes propios, mismo
  build en varias variantes, protecciones y diagnóstico sobre límite. Fallos antes
  y después de retirar el registro verifican la cola recuperable. Set/unset y valores
  inválidos no borran ni dejan ajustes parciales.
- La suite de procesos incluye upgrade mientras una instancia real sigue activa:
  conserva PID, ID, build y archivos anteriores con retención uno, y avanza latest
  para futuros inicios. Una identidad incierta conserva también el build. Suite
  específica administrada y recorrido completo nativo ejecutados.
- Documentación: **39 diagramas PlantUML**, **3 ejemplos JSON** y enlaces locales
  de docs correctos, mediante `artifacts/task025-checks/validate-docs.ps1`.
  `git diff --check` sin errores. Board/search/view y dependencia 024 revisados;
  se mantiene la revisión manual de dependencias del adaptador local.

Comandos de reproducción:

```powershell
dotnet run --project tests/Coquena.Tests --no-restore
dotnet run --project tests/Coquena.Tests --no-restore -- --backend-upgrade-tests
dotnet publish src/Coquena/Coquena.csproj --no-restore -c Release -r win-x64 -o artifacts/task025-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj --no-restore -c Release -r win-x64 -o artifacts/task025-tests-final-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task025-checks/run-native.ps1
& artifacts/task025-checks/run-native.ps1 -Filter '--backend-upgrade-tests' -OutputName 'upgrade-native'
& artifacts/task025-checks/validate-docs.ps1
git diff --check
```

El helper nativo normaliza Path/PATH para este host. Las publicaciones y sus logs
están en `artifacts/task025-checks/publish-*.txt`. Se usaron homes, ejecutables y
HTTP de fixtures; no se modificó `${CoquenaHome}`, no se descargaron builds oficiales
reales ni pesos y no se realizó inferencia. La verificación local de versión no
certifica compatibilidad funcional con GPU o modelos reales.

Entrega aprobada por el usuario el 12 de octubre de 2026.
