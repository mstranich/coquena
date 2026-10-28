---
status: archived
created: 2026-10-10
priority: medium
tags:
- m2
created_at: 2026-10-10T10:53:13.328913500Z
updated_at: 2026-10-28T10:21:41.644827200Z
completed_at: 2026-10-11T17:26:31.256948Z
transitions:
- status: planned
  at: 2026-10-10T16:29:42.160543200Z
- status: in-progress
  at: 2026-10-11T16:04:36.937347700Z
- status: complete
  at: 2026-10-11T17:26:31.256948Z
- status: archived
  at: 2026-10-28T10:21:41.644827200Z
---
# Configurar el directorio de backends

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

## Objetivo
Consultar y ajustar la ubicación de los backends gestionados mediante config backends-dir.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [CLI](../../docs/comandos.md#config)
- [Directorios gestionados](../../docs/servidores.md#directorios-gestionados)
- [Persistencia](../../docs/servidores.md#persistencia)

## Trabajo y aceptación
- [x] Persistir el directorio y usarlo en instalación, consulta y resolución de backends.
- [x] Verificar publicación y traslado entre volúmenes con copia y comprobación antes
  de retirar el origen, usando la preparación existente y recuperación ante interrupciones.
- [x] Verificar cambio/restablecimiento, reinicio de Coquena y fallos de acceso o escritura.
- [x] Crear destinos inexistentes y rechazar rutas a archivos o sin permisos suficientes
  antes de modificar la ubicación o trasladar recursos.
- [x] Verificar show/set/unset y el origen de la ruta efectiva; unset recupera
  <home>/backends y ofrece el mismo traslado que set.
- [x] Conservar rutas relativas en la configuración y verificar su resolución después
  de reubicar el conjunto de directorios con home como base, incluso al invocar desde
  otra carpeta. Verificar las ubicaciones de instalación antiguas y nuevas del catálogo.
- [x] Comprobar el tratamiento acordado de instalaciones, descargas y servidores existentes.
- [x] Probar elección de traslado, conservación en origen y cancelación; mantener
  identidades y referencias y resolver instalaciones en ambas ubicaciones.
- [x] Bloquear el traslado completo ante recursos en uso o descargas pendientes;
  comprobar el diagnóstico y la ausencia de cambios en archivos y configuración.
- [x] Publicar y ejecutar Native AOT win-x64; registrar evidencia y actualizar ayuda.
- [x] Verificar fusión con contenido existente y reporte completo de colisiones antes
  de mover archivos; comprobar confirmación única, sobrescrituras autorizadas y cancelación.
- [x] Conservar archivos sin colisión y proteger recursos en uso o externos en el destino;
  no sobrescribir colisiones nuevas que no estén incluidas en la confirmación.

## Límites
Esta entrega configura la ubicación de backends; no implementa actualización de binarios
ni el ajuste del directorio de modelos.

## Decisiones de alcance

El usuario aclaró el 11 de octubre de 2026 que se trasladan todas las instalaciones
gestionadas por Coquena, desde sus ubicaciones registradas, y cada directorio de
instalación completo. Las referencias importadas/externas permanecen iguales.
Se conserva fuera del traslado cualquier archivo ajeno a esos directorios.

## Implementación y evidencia

Implementada, validada y aprobada por el usuario el 11 de octubre de 2026. El usuario
autorizó cerrar la tarea y hacer un único commit con implementación, pruebas,
documentación y evidencia. No se modificó el home real ${CoquenaHome}.

Config backends-dir show/set/unset conserva el ajuste literal en config.json y muestra
la ruta efectiva y su origen. Las instalaciones y las operaciones guardan su propia
ubicación, independiente de la configuración vigente. Los registros anteriores sin
ubicación explícita siguen resolviendo desde el home. Backend list, selección y arranque
resuelven el ejecutable desde la ubicación registrada.

El traslado inventaría el directorio completo de cada instalación, incluidos archivos
auxiliares y directorios vacíos. Consulta y confirmaciones ocurren fuera del bloqueo de
escritura. Antes de aplicar repite inventario, hashes, comprobación de uso y estado de
catálogo/configuración. Cambios concurrentes obligan a repetir el comando y revisar el
informe. Un preflight comprueba escritura del destino y permisos para reemplazar las
colisiones y retirar el origen, sin borrar ni renombrar archivos existentes.

El marcador recuperable mantiene los snapshots de catálogo y configuración y un
manifiesto de archivos y sobrescrituras autorizadas. La copia se verifica mediante
SHA-256 antes de publicar cada archivo; los orígenes se conservan hasta publicar
conjuntamente las nuevas rutas. Después del commit, LocalStore ofrece ambos snapshots
aunque los checkpoints estén incompletos. La siguiente escritura recupera la operación
antes de modificar el estado. La limpieza tolera archivos ya retirados por una
interrupción y protege un origen o destino que haya cambiado. Estos hashes son evidencia
local del traslado; no añaden digests remotos al catálogo de backends.

La publicación de instalaciones entre volúmenes copia y verifica la extracción en una
carpeta propia del destino antes de activar el slot. Reanudar conserva selección,
identidad y ubicación fijadas; cancelar retira sólo la copia propia. La preparación
permanece hasta publicar el índice. Reinstalar conserva el ID y retira el slot anterior
desde su ubicación real.

Validaciones ejecutadas:

- Suite administrada completa: exit 0; después del ajuste final de preflight, suite
  dirigida de directorios: 96 comprobaciones, exit 0.
- CLI y runner publicados Native AOT win-x64 con análisis estricto, sin advertencias.
- Suite nativa completa contra la CLI publicada: exit 0. Incluye 103 comprobaciones
  específicas de directorios, 70 de procesos, 56 de instalación/reinstalación, 54 de
  descargas y 120 de instalación de modelos, además de la regresión de contratos,
  almacenamiento, catálogos, parámetros, presets, servidores, métricas, HF y GGUF.
- Traslado de instalaciones desde distintas ubicaciones; conservación en origen;
  cancelación; set/unset; referencias e IDs; árboles completos y directorios vacíos;
  rutas relativas después de reubicar home y directorios e invocar desde otra carpeta.
- Informe con varias colisiones, una confirmación única, cancelación, conservación de
  contenido sin colisión y rechazo de colisiones nuevas o modificadas durante la elección.
- Protección de referencias externas y de un servidor realmente activo. Tras detenerlo,
  traslado completo e inicio desde el nuevo directorio con la misma identidad de backend.
- Rutas a archivos, antecesores que son archivos, rutas parciales, nombres reservados,
  archivos bloqueados/de sólo lectura y ACL sin escritura o sin permiso de retirada.
  Las ACL sólo se aplicaron a fixtures temporales y se restauraron.
- Fallos inyectados después de copiar, antes/después del commit, entre checkpoints y
  durante limpieza. Una nueva instancia de LocalStore recupera antes de escribir;
  los lectores observan el estado anterior o los snapshots publicados conjuntamente.
- Copia, publicación, reinstalación, cancelación y traslado real entre C: (NTFS) y
  S: (ReFS). Fallos de publicación después de copiar, mover y publicar el índice.
- CLI nativa en procesos independientes: persistencia, resolución desde otra carpeta,
  consulta del directorio y de ubicaciones antiguas, cancelación con entrada redirigida,
  sintaxis inválida y ayuda. Sin descargas remotas ni inferencia real.
- Validación documental: 37 diagramas PlantUML, tres ejemplos JSON y enlaces locales.
  git diff --check: exit 0.

Reproducción desde la raíz del checkout:

```powershell
dotnet run --project tests/Coquena.Tests --no-restore
dotnet run --project tests/Coquena.Tests --no-restore -- --backend-directory-tests
dotnet restore tests/Coquena.Tests/Coquena.Tests.csproj -r win-x64 -p:PublishAot=true --ignore-failed-sources -p:NuGetAudit=false
dotnet publish src/Coquena/Coquena.csproj --no-restore -c Release -r win-x64 -o artifacts/task019-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj --no-restore -c Release -r win-x64 -o artifacts/task019-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task019-tests-native/Coquena.Tests.exe artifacts/task019-native/coquena.exe
& artifacts/task019-native/coquena.exe --help
git diff --check
```

En este host la ejecución nativa se lanzó mediante ProcessStartInfo con variables de
entorno normalizadas con OrdinalIgnoreCase, para evitar duplicados Path/PATH. El helper
y los logs de ejecución están en artifacts/task019-checks; el ejecutable de la CLI está
en artifacts/task019-native/coquena.exe.

Limitaciones explícitas:

- Las colisiones estructurales entre un archivo y un directorio, los directorios de
  instalación superpuestos y los enlaces/reparse points se rechazan antes de trasladar.
- La copia/publicación local se serializa con el escritor del home; durante un traslado
  extenso otros comandos de escritura pueden informar writer-busy. No se mantiene ese
  bloqueo durante transferencias de red ni se detienen procesos/descargas automáticamente.
- La recuperación se probó con fallos inyectados en los puntos de publicación; no se
  simuló una pérdida de energía física. Un cambio externo del contenido protegido se
  diagnostica conservando el origen, sin adoptar bytes distintos automáticamente.
- Una prueba inicial falló al restaurar la ACL de su fixture; se corrigió y las suites
  posteriores terminaron y limpiaron sus fixtures. Se restauró la ACL del fixture inicial,
  pero la revisión automática rechazó su eliminación recursiva con «blocked by policy».
  Quedaron ${TestRoot}/coquena-directories-run
  y artifacts/task019-volume-a57dfa941cc94060a8b538db72751db1. El segundo directorio
  conserva la copia fixture de esa misma ejecución fallida; su retirada también fue rechazada.

No se agregaron paquetes, registro/importación de externos, actualización de builds,
config models-dir ni otras entregas de M2.
