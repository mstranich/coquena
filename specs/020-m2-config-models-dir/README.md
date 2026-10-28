---
status: archived
created: 2026-10-10
priority: medium
tags:
- m2
created_at: 2026-10-10T10:53:13.413246100Z
updated_at: 2026-10-28T10:21:41.645963200Z
completed_at: 2026-10-11T18:06:53.266478400Z
transitions:
- status: planned
  at: 2026-10-10T16:29:42.244111800Z
- status: in-progress
  at: 2026-10-11T17:29:27.521931200Z
- status: complete
  at: 2026-10-11T18:06:53.266478400Z
- status: archived
  at: 2026-10-28T10:21:41.645963200Z
---
# Configurar el directorio de modelos

## Objetivo
Consultar y ajustar la ubicación de modelos y companions gestionados mediante config models-dir.

## Referencias
- [M2](../../docs/milestones/M2-gestion-completa.md)
- [CLI](../../docs/comandos.md#config)
- [Directorios gestionados](../../docs/servidores.md#directorios-gestionados)
- [Propiedad y registros](../../docs/modelos-y-companions.md)

## Trabajo y aceptación
- [x] Persistir el directorio y usarlo al descargar, listar y resolver archivos para presets.
- [x] Verificar publicación y traslado entre volúmenes con copia y comprobación antes
  de retirar el origen, usando la preparación existente y recuperación ante interrupciones.
- [x] Crear destinos inexistentes y rechazar rutas a archivos o sin permisos suficientes
  antes de modificar la ubicación o trasladar recursos.
- [x] Verificar show/set/unset y el origen de la ruta efectiva; unset recupera
  <home>/models y ofrece el mismo traslado que set.
- [x] Conservar rutas relativas en la configuración y verificar su resolución después
  de reubicar el conjunto de directorios con home como base, incluso al invocar desde
  otra carpeta. Verificar las ubicaciones de instalación antiguas y nuevas del catálogo.
- [x] Verificar modelos de un archivo y unidades con shards, conservando su identidad.
- [x] Comprobar el tratamiento acordado de recursos existentes y fallos sin estado parcial.
- [x] Probar elección de traslado, conservación en origen y cancelación; resolver
  unidades en ambas ubicaciones, manteniendo identidades y referencias de presets.
- [x] Bloquear el traslado completo ante recursos en uso o descargas pendientes;
  comprobar el diagnóstico y la ausencia de cambios en archivos y configuración.
- [x] Publicar y ejecutar Native AOT win-x64; registrar evidencia y actualizar ayuda.
- [x] Verificar fusión con contenido existente y reporte completo de colisiones antes
  de mover archivos; comprobar confirmación única, sobrescrituras autorizadas y cancelación.
- [x] Conservar archivos sin colisión y proteger recursos en uso o externos en el destino;
  no sobrescribir colisiones nuevas que no estén incluidas en la confirmación.

## Límites
Esta entrega no ajusta el directorio de backends. Configurar la ubicación de companions
gestionados no incorpora su instalación: ese recorrido pertenece a la entrega de companions.

## Implementación y revisión

`config models-dir show|set|unset` conserva el ajuste literal y resuelve rutas relativas
desde el home. Cada modelo y cada operación conservan su base de instalación; los
registros anteriores sin base mantienen su interpretación original bajo el home.
La selección de conservar en origen permite coexistir instalaciones en varias bases.
La composición de presets y la consulta local resuelven la base del registro.

El traslado opera sólo sobre los archivos catalogados, reuniendo todas las ubicaciones
anteriores y conservando el orden de shards, IDs, parámetros, integridad y fechas.
No elimina directorios de repositorio ni archivos vecinos. Analiza colisiones antes
de copiar, obtiene una confirmación única y revalida el inventario bajo el escritor.
Protege recursos en uso, identidades de proceso inciertas, operaciones pendientes y
referencias externas del catálogo de backends que coincidan con archivos afectados.

La transacción `catalogs/model-directory-transaction.json` conserva ambos snapshots y
el inventario verificado. Antes del commit los lectores usan el estado anterior;
después leen configuración y modelos del snapshot publicado. El siguiente escritor
recupera la copia, los checkpoints o la retirada del origen. La publicación de una
descarga conserva toda la preparación hasta publicar el modelo y terminar su limpieza,
también cuando la preparación y el destino están en volúmenes distintos.

La tarjeta permanece **in-progress**, pendiente de revisión y aceptación. Sin commit.

## Evidencia

- Regresión administrada completa: exit 0. Suite dirigida final: 64 comprobaciones,
  exit 0; usa GGUF pequeños y transporte simulado, sin descargas remotas.
- Publicación Native AOT win-x64 de CLI y runner con análisis estricto: exit 0,
  sin advertencias. Ayuda del ejecutable publicada con `config models-dir`.
- Traslado y publicación reales entre C: (NTFS) y S: (ReFS); unidades de un archivo
  y de dos shards; instalaciones antiguas y nuevas, conservación y cancelación.
- Set/unset, origen propio/default, rutas relativas conservadas, reubicación del home,
  listado local e INI con ruta absoluta de la instalación trasladada.
- Fusión, conservación de archivos vecinos, confirmación única y cancelación; cambios
  del contenido de una colisión durante la confirmación invalidan el plan.
- Bloqueo por operación pendiente, identidad de proceso incierta y referencia externa
  en destino; archivos de sólo lectura y ACL de destino sin escritura, restaurada al
  finalizar la prueba. No se alteró ninguna instalación real del usuario.
- Fallos inyectados después de copiar, antes/después del commit, entre checkpoints y
  antes de retirar orígenes. Lectores coherentes y recuperación con un nuevo LocalStore.
- Publicación de descargas en otro volumen interrumpida después del primer archivo y
  después del índice; preparación completa conservada y recuperación sin nueva selección.
- Validación documental: 37 diagramas PlantUML, tres ejemplos JSON y enlaces locales.
  `git diff --check`: exit 0.

Reproducción desde la raíz del checkout:

```powershell
dotnet run --project tests/Coquena.Tests --no-restore
dotnet run --project tests/Coquena.Tests --no-restore -- --model-directory-tests
dotnet publish src/Coquena/Coquena.csproj --no-restore -c Release -r win-x64 -o artifacts/task020-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj --no-restore -c Release -r win-x64 -o artifacts/task020-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task020-checks/run-native-tests.ps1
& artifacts/task020-checks/validate-docs.ps1
git diff --check
```

El helper nativo normaliza las variables de entorno sin distinguir mayúsculas para
evitar duplicados Path/PATH. Los logs se guardan en `artifacts/task020-checks`.

Limitaciones: se rechazan enlaces/reparse points, colisiones archivo/directorio y
unidades con archivos superpuestos. La copia local mantiene el escritor del home;
un traslado largo puede provocar `writer-busy` en otros comandos de escritura. Los
fallos se verificaron mediante inyección, sin simular pérdida física de energía.
La prueba de uso emplea una identidad incierta y comprueba el bloqueo conservador;
no incorpora inferencia real. No se agregó registro de modelos externos ni instalación
de companions: mantienen sus entregas propias. Sus futuras instalaciones utilizarán
la misma base gestionada, sin adoptar archivos externos.
