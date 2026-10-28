---
status: archived
created: 2026-10-10
tags:
- m2
created_at: 2026-10-10T16:36:42.739369Z
updated_at: 2026-10-28T10:21:41.650182800Z
completed_at: 2026-10-12T14:43:16.743876200Z
transitions:
- status: planned
  at: 2026-10-10T16:48:26.310946200Z
- status: in-progress
  at: 2026-10-12T14:24:35.516157800Z
- status: complete
  at: 2026-10-12T14:43:16.743876200Z
- status: archived
  at: 2026-10-28T10:21:41.650182800Z
---
# Consultar actualizaciones de backends oficiales

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

## Objetivo
Separar la consulta de novedades de la instalación, conservando un candidato por seguimiento
latest.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [Reglas funcionales](../../docs/servidores.md#definición-persistida)
- [CLI](../../docs/comandos.md)

## Trabajo y aceptación
- [x] Consultar todos los seguimientos latest o uno seleccionado y guardar
  candidato/filtro/diagnóstico sin descargar ni cambiar el build instalado.
- [x] Excluir selecciones fijadas a un build específico y externos; un pin no
  desactiva un seguimiento latest que comparta su build actual. Un error no impide
  consultar los demás seguimientos.
- [x] Probar novedades, ausencia de cambios, red fallida y persistencia con fixtures
  oficiales; reutilizar fuente y resolución de 005/006.
- [x] Actualizar ayuda/documentación y ejecutar pruebas pertinentes; publicar/ejecutar
  Native AOT win-x64 con evidencia reproducible.

## Límites
No instala builds ni modifica instancias activas. Reutiliza la fuente oficial de 005/006.

## Implementación y decisiones

- `backend update [selector-latest]` consulta todos los seguimientos oficiales
  registrados o uno concreto, con los mismos selectores abreviados y alias `oficial`
  del catálogo existente. No deriva el conjunto del backend o servidor default.
- Un pin fija un build específico. Según la aclaración del usuario del 12 de octubre
  de 2026, no desactiva el seguimiento latest que comparta ese build. Se consultan
  registros gestionados con `TracksLatest`, sin excluirlos por `Pinned`; los pins
  sin seguimiento y todos los externos se omiten. Selectores fijados se rechazan.
- Se reutilizan `OfficialBackendSource` y `BackendCatalog`: consulta acotada de
  metadatos oficiales, filtro de variante/plataforma y bundle CUDA completo.
  No se descargan artefactos ni se crean operaciones de transferencia.
- Una observación por origen/variante/plataforma en
  `catalogs/backend-updates.json`, mediante `LocalStore` y JSON generado para AOT.
  Conserva ID/build instalado de partida, fecha UTC, estado y diagnóstico; la
  novedad fija build, nombres/tamaños de artefactos y evidencia de compatibilidad.
  No guarda URLs, credenciales ni digests de backends. No modifica `backends.json`,
  defaults, selecciones fijadas, archivos instalados o instancias.
- Sólo se propone un build posterior al instalado; no se propone downgrade.
  Sin novedades se elimina el candidato anterior. Un fallo remoto reemplaza la
  observación por un diagnóstico sin candidato aplicable, sin afirmar «actualizado».
  Un fallo de una unidad no impide consultar las demás; la CLI devuelve exit 1 si
  alguna falla y exit 2 para argumentos/selectores inválidos.
- La red se consulta sin mantener la exclusión de escritura. Al guardar se verifica
  que siguen vigentes ID/build/filtro y observación anterior. Los resultados
  obsoletos se rechazan. Fallos antes del reemplazo o cancelación no publican un
  candidato parcial. Corrupción de documentos se diagnostica, sin reparación silenciosa.
- `backend list` muestra observaciones correspondientes al build actual sin red ni
  sondeo de hardware. Ayuda, reglas funcionales y secuencia PlantUML actualizadas.
  Sin paquetes nuevos. El consumo del candidato por upgrade pertenece a 025.

## Evidencia

- Suite administrada completa y suite completa con runner/producto Native AOT:
  exit 0. Incluyen regresión de contratos, almacenamiento, configuración,
  parámetros, servidores, procesos, descargas, instalación, directorios, HF y GGUF.
  Salidas: `artifacts/task024-checks/managed-tests.txt` y `native-tests.txt`;
  sus archivos `*-errors.txt` están vacíos.
- Tras el ajuste final de reconocimiento de la URL oficial ya admitida por el
  catálogo, republicación estricta del producto y runner Native AOT `win-x64`:
  exit 0. Suite específica final: **49 comprobaciones administradas y 52 nativas**,
  con salidas en `update-managed-tests.txt` y `update-native-tests.txt` dentro
  de `artifacts/task024-checks/`; stderr vacío.
- Fixtures de metadatos oficiales: novedades, mismo build, respuesta anterior,
  bundle CUDA completo y compatibilidad desconocida, ausencia de candidato local,
  fallos HTTP/red/metadatos y continuación de otros filtros. Persistencia/reinicio,
  alias oficiales, pins/exclusión de externos, escritura fallida, cancelación,
  cambios concurrentes, seguimiento ambiguo y corrupción comprobados.
- El runner AOT ejecuta la consulta real con HTTP simulado. Procesos independientes
  del producto nativo verifican listado de candidatos/diagnósticos persistidos,
  rechazo de selectores fijados/externos y ayuda. En este host el runner se inicia
  mediante `ProcessStartInfo` con entorno normalizado para evitar duplicados Path/PATH.
- Validación documental: **38 diagramas PlantUML**, **3 ejemplos JSON** y enlaces
  locales de docs. Resultado en `artifacts/task024-checks/docs.txt`; repetir con
  `& artifacts/task024-checks/validate-docs.ps1`.
  `git diff --check` sin errores.
- Board/search/view revisados. `leanspec deps` sigue sin migrarse al adaptador;
  revisión manual de la tarjeta, M2 y las entregas existentes de 005/006 y 023.

Comandos de reproducción:

```powershell
dotnet run --project tests/Coquena.Tests --no-restore
dotnet run --project tests/Coquena.Tests --no-restore -- --backend-update-tests
dotnet publish src/Coquena/Coquena.csproj --no-restore -c Release -r win-x64 -o artifacts/task024-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj --no-restore -c Release -r win-x64 -o artifacts/task024-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task024-tests-native/Coquena.Tests.exe artifacts/task024-native/coquena.exe
& artifacts/task024-tests-native/Coquena.Tests.exe --backend-update-tests artifacts/task024-native/coquena.exe
git diff --check
```

Se usaron homes y binarios temporales con HTTP simulado. No se modificó `${CoquenaHome}`,
no se instalaron builds reales ni se descargaron modelos. La consulta no prueba
inferencias ni certifica compatibilidad funcional con hardware.

Tarea cerrada tras aprobación del usuario el 12 de octubre de 2026.
