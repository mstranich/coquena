---
status: complete
created: 2026-10-09
priority: medium
created_at: 2026-10-09T14:07:09.965469Z
updated_at: 2026-10-11T13:33:25.947805500Z
completed_at: 2026-10-11T13:33:25.947805500Z
transitions:
- status: draft
  at: 2026-10-09T14:07:24.090154500Z
- status: planned
  at: 2026-10-10T12:15:33.140541Z
- status: in-progress
  at: 2026-10-11T13:05:16.904437400Z
- status: complete
  at: 2026-10-11T13:33:25.947805500Z
---
# Configuración de retención de logs y separación de config/preset

## Objetivo
Liberar config para las políticas de Coquena, trasladar la asignación de parámetros a
preset e incorporar la retención global y por servidor.

## Referencias
- [M1: servidor local](../../docs/milestones/M1-servidor-local.md)
- [CLI](../../docs/comandos.md#config) y [presets](../../docs/comandos.md#preset)
- [Valores de presets](../../docs/modelos-y-companions.md#presets)
- [Retención y unidades](../../docs/servidores.md#consulta-de-logs)
- [Persistencia](../../docs/servidores.md#persistencia)
- Antecedentes implementados: [010](../010-m1-config-presets-ini/README.md) y
  [013](../013-m1-metricas-logs/README.md).

## Trabajo
- [x] Sustituir config set/unset/show --preset por preset set/unset/show.
- [x] Retirar los ámbitos global/family/modelo, sus valores compartidos y su composición
  en el INI. Cada preset conserva sus propios valores; server set administra los del proceso.
- [x] Eliminar la sintaxis anterior sin alias, migraciones ni avisos de deprecación.
- [x] Implementar config log-retention show/set/unset y
  server [nombre] log-retention show/set/unset, con persistencia segura.
- [x] Aplicar la política efectiva en la limpieza existente y actualizar la ayuda.

## Aceptación: asignación y CLI
- [x] Set, unset y show operan sobre el preset indicado y su modelo asociado.
- [x] Dos presets del mismo modelo mantienen valores independientes; unset no recupera
  valores globales, de familia ni del modelo.
- [x] Dos servidores con el mismo backend mantienen parámetros de proceso independientes.
- [x] Verificar la composición del INI conforme al
  [alcance por instancia](../../docs/catalogo-de-parametros.md#aplicación-en-router-mode),
  sin incorporar los ámbitos retirados.
- [x] La sintaxis retirada recibe el diagnóstico normal de argumentos inválidos.

## Aceptación: retención
- [x] Sin política propia, cada servidor usa el límite inicial de 100 MiB.
- [x] Set/unset global modifica/restablece la política; el override de servidor prevalece
  y su unset recupera el global. Cambiar el global no altera los overrides existentes.
- [x] La política persiste entre invocaciones; sin nombre se usa el servidor predeterminado.
- [x] Show informa bytes exactos, tamaño legible y origen inicial/global/propio.
- [x] Consultar o cambiar la política no elimina archivos. La limpieza al iniciar,
  detener o consultar logs usa el límite efectivo y conserva activo y último con diagnóstico.
- [x] Un fallo de validación o persistencia no deja una actualización parcial.

## Aceptación: tamaños
- [x] KB/MB/GB usan base decimal; KiB/MiB/GiB usan base binaria; B y ausencia de sufijo
  expresan bytes. 100MB = 100000000 B; 100MiB = 104857600 B.
- [x] 1.5GiB = 1610612736 B; 0.5KiB = 512 B; 0.1KiB se rechaza sin redondear.
- [x] 100MiB, 100mib y "100 MiB" son equivalentes, independientemente de la cultura del SO.
- [x] Rechazar cero, negativos, coma decimal, separadores de miles, unidades desconocidas,
  fracciones de byte y overflow. Verificar los extremos del rango documentado.

## Validación y límites
- [x] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 con evidencia.
- [x] PlantUML, JSON, enlaces y git diff --check para la documentación afectada.

La gestión del catálogo de parámetros pertenece a 018; los directorios y system probe,
a sus tarjetas de M2. Companions, clonación, filtrado de parámetros personalizados y
cambios de métricas no forman parte de esta entrega. Se reutilizan la limpieza y el
ciclo de procesos existentes; no se agrega rotación continua.

## Evidencia — 11 de octubre de 2026

Implementados `preset set/unset/show`, sin herencia global, de familia o de modelo.
La composición usa sólo valores propios y referencias al modelo por ID. Retirada la
sintaxis anterior de config y los campos de parámetros globales/familias de su contrato;
no se migra ni se copia estado anterior. Los valores del modelo no participan en la
consulta ni en la composición de presets. El ejemplo público del README y la ayuda
reflejan la sintaxis nueva.

Implementadas políticas global y propia del servidor, con persistencia atómica existente,
resolución propio/global/inicial y salida de bytes exactos, unidad binaria y origen.
La limpieza existente consulta la política efectiva bajo la misma exclusión de escritura;
se mantienen sus puntos de inicio/parada/logs y las protecciones activo/último.
Consultar o cambiar la política no limpia archivos. Conversión de tamaños con aritmética
entera exacta, independiente de cultura, sin redondeo y con límites de Int64.

Validaciones ejecutadas:
- Suite administrada completa: exit 0. Tras ampliar la prueba de fallo de escritura,
  suite de configuración dirigida: 73 comprobaciones, exit 0.
- Publicación CLI y runner Native AOT win-x64, sin advertencias y con análisis estricto.
- Suite nativa completa con CLI publicada: exit 0; 73 comprobaciones de presets/INI,
  49 de políticas de retención, 72 de definiciones de servidor, 66 de ciclo de procesos
  y 22 de métricas/retención, además de la regresión de contratos, almacenamiento,
  backends, descargas, GGUF e instalación de modelos.
- Cubiertos independencia de presets del mismo modelo, unset sin herencia, alias,
  sintaxis retirada, conservación ante fallo de escritura y ausencia de valores compartidos.
- Cubiertos precedencia de retención, persistencia entre procesos, unset, falta de limpieza
  inmediata, limpieza con override frente a global más bajo, logs protegidos, tamaños
  fraccionarios exactos, cultura es-AR, límites 1/Int64.MaxValue y entradas inválidas sin escritura.
- Validador documental: 37 diagramas PlantUML, tres ejemplos JSON y 169 enlaces locales.
  `git diff --check`: exit 0.

Comandos de reproducción:

```powershell
dotnet run --project tests/Coquena.Tests --no-restore
dotnet run --project tests/Coquena.Tests --no-restore -c Release -- --configuration-tests
dotnet restore tests/Coquena.Tests/Coquena.Tests.csproj -r win-x64 -p:PublishAot=true --ignore-failed-sources -p:NuGetAudit=false
dotnet publish src/Coquena/Coquena.csproj --no-restore -c Release -r win-x64 -o artifacts/task017-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests/Coquena.Tests.csproj --no-restore -c Release -r win-x64 -o artifacts/task017-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task017-tests-native/Coquena.Tests.exe artifacts/task017-native/coquena.exe
& artifacts/task013-checks/validate-docs.ps1
git diff --check
```

El entorno del host exponía simultáneamente `Path` y `PATH`: el primer recorrido nativo
falló al construir el entorno de un proceso fixture. Se relanzó con un diccionario
OrdinalIgnoreCase de variables, mediante ProcessStartInfo.Environment.Clear() y copia
sin claves duplicadas. La prueba de señales Ctrl+C tuvo un timeout bajo sandbox;
la suite completa pasó con permisos para sus procesos y consolas fixture.
No se cambió el código de procesos para absorber esos problemas del entorno.

Las pruebas usan homes temporales, backends y modelos fixture; no se modificó
D:\Coquena, no se descargaron pesos ni se ejecutó inferencia real. Sin paquetes nuevos,
trabajo de otras tarjetas. El usuario aprobó la entrega el 11 de octubre de 2026,
solicitó cerrar la tarea y autorizó un único commit de implementación, pruebas,
documentación y evidencia. Estado actualizado a complete mediante LeanSpec.
