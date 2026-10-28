---
status: archived
created: 2026-10-07
tags:
- m1
depends_on:
- '003'
- '010'
created_at: 2026-10-07T22:02:20.212362700Z
updated_at: 2026-10-28T10:21:41.636284400Z
completed_at: 2026-10-09T12:06:47.782159300Z
transitions:
- status: in-progress
  at: 2026-10-09T11:58:33.134006200Z
- status: complete
  at: 2026-10-09T12:06:47.782159300Z
- status: archived
  at: 2026-10-28T10:21:41.636284400Z
---
# Definiciones de servidor, defaults y puertos

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

## Objetivo
Integrar create/default/use/set/unset/preset/show/list y resolución de nombre opcional con palabras reservadas. Persistir intención de backend y presets asociados; host/port/metrics estáticos, métricas true. Componer plan de inicio con catálogos inyectados, sin lanzar procesos.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [comandos.md](../../docs/comandos.md)
- [servidores.md](../../docs/servidores.md)

## Trabajo y aceptación
- [x] Probar server create default y server default default, herencia backend, latest/pin, selección ausente, preset inválido y puerto único. Asignación automática max+1/8080 inicial informa valor y rechaza overflow sin envolver. Inicio sin sustitutos ni presets omitidos.
- [x] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [x] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Implementación y validación del 9 de octubre de 2026, sin paquetes nuevos.

### Entrega y decisiones

- `ServerCli` integra create/default/use/set/unset/preset/show/list y ayuda de CLI.
  Interpreta la gramática antes de leer entidades; `default` seguido de una acción
  actúa sobre ese nombre aunque otro sea el predeterminado. No anuncia start/stop,
  restart, ps, logs ni remove como disponibles en esta entrega.
- `ServerManager` persiste definiciones y designación mediante `LocalStore`. Crea
  el default inicial al primer uso de servidores con catálogo ausente, conservando
  la selección global de backend y sin asociar presets. `server create default`
  permite concretar esa primera definición explícitamente. Un catálogo guardado
  vacío o un default retirado no se recrea; un catálogo ausente con designación
  existente se diagnostica y no se sustituye. La persistencia usa las garantías
  por documento de 003; no introduce transacciones multidocumento.
- Puerto concreto y único, asignación max+1 bajo la exclusión entre procesos,
  8080 con catálogo vacío; unset incluye el puerto previo en el cálculo. Rechaza
  65535 sin envolver, buscar huecos ni perder el valor anterior. La CLI informa
  el puerto después de cada modificación exitosa. La disponibilidad del puerto
  en el sistema se comprobará al lanzar en 012.
- Parámetros desde el catálogo existente: host/port/metrics estáticos y valores
  del router models-max/models-autoload. No mezcla parámetros de modelo; reserva
  models-preset para el INI generado. Unset recupera defaults sin retirarlos del
  catálogo. Se validan tipos y nombres; no se agregan definiciones arbitrarias.
- Backend propio conserva intención latest o ID fijado; use default recupera
  herencia. Reutiliza el resolvedor oficial local de 006 y protege los pins.
  Show presenta selección propia/heredada, build resuelto, valores efectivos,
  presets y diagnósticos de referencias ausentes, sin red ni procesos.
- `ServerManager.Compose` devuelve `ServerStartPlan` con backend concreto,
  parámetros efectivos, INI y recursos de todos los presets. Rechaza selección
  ausente/ambigua, servidor vacío, referencias rotas y archivos inválidos;
  nunca usa sustitutos ni omite presets. Reutiliza el núcleo `ComposeLocked` de
  010 para mantener una sola exclusión y una instantánea consistente; su API
  pública anterior conserva la misma conducta. Admite store y compositor
  inyectados para pruebas. No escribe INI de instancia ni lanza procesos.
- Externos y companions permanecen en M2 conforme al milestone. No requiere
  decisiones nuevas del usuario ni extiende las tareas 012–014.

### Verificación reproducible

```powershell
dotnet build Coquena.slnx
dotnet run --project tests/Coquena.Tests -- --server-tests
dotnet publish src/Coquena -c Release -r win-x64 -o artifacts/task011-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests -c Release -r win-x64 -o artifacts/task011-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet run --project tests/Coquena.Tests -- --server-tests artifacts/task011-native/coquena.exe
& artifacts/task011-tests-native/Coquena.Tests.exe artifacts/task011-native/coquena.exe
git diff --check
leanspec board
leanspec search server
leanspec view 011
```

Resultados: build sin errores/advertencias. CLI y ejecutable de pruebas Native AOT
win-x64 publicados con análisis estricto sin advertencias. 68 comprobaciones nuevas
administradas y con CLI nativa: gramática, persistencia, defaults, tipos, puertos,
fallo antes de reemplazar, seis escritores concurrentes, latest/pin/herencia y
composición real de INI con rechazo de referencias rotas y ausencia de archivos.
Las pruebas usan homes temporales; no modifican ${CoquenaHome} ni arrancan llama-server.

Suite completa nativa exit 0: 9 casos de CLI y 9 del ejecutable, contratos y
almacenamiento, 75 comprobaciones de configuración, 68 de servidores, 181 de
backends, 54 de descargas, 56 de instalación de backend, 217 de resolución,
168 de selección HF, 127 de GGUF/integridad y 120 de instalación de modelos.
No acredita inferencia ni ciclo de procesos, que pertenecen a 012/014.

`git diff --check` y enlaces locales correctos. Sin cambios PlantUML ni ejemplos
JSON nuevos. LeanSpec revisado mediante board/search/view y dependencias 003/010
completas; `leanspec deps 011` informa que no está migrado al adaptador, por lo que
la revisión de dependencias es manual. El usuario aprobó el cierre y el commit
conjunto de implementación, pruebas y evidencia el 9 de octubre de 2026.
