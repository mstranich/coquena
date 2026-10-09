---
status: complete
created: 2026-10-07
tags:
- m1
depends_on:
- '012'
created_at: 2026-10-07T22:02:22.125315100Z
updated_at: 2026-10-09T16:44:51.741007200Z
completed_at: 2026-10-09T16:44:51.741007200Z
transitions:
- status: in-progress
  at: 2026-10-09T14:03:05.365238Z
- status: complete
  at: 2026-10-09T16:44:51.741007200Z
---
# Instantánea de métricas y consulta de logs

Las rutas de evidencia están parametrizadas. Para reproducir comandos de este documento, definir `$TestRoot` y `$CoquenaHome` con directorios de prueba propios; las ubicaciones originales no se publican.

```powershell
$TestRoot = Join-Path $env:TEMP "coquena-validation"
$CoquenaHome = Join-Path $TestRoot "home"
$TestRootUpper = $TestRoot.ToUpperInvariant()
$CoquenaHomeLower = $CoquenaHome.ToLowerInvariant()
New-Item -ItemType Directory -Force $TestRoot | Out-Null
```

## Objetivo
Integrar server ps con métricas por modelo y server logs/-f para instancia actual o último log. Concretar referencia de último log y retención limitada, sin inventar historial de instancias activas.

## Referencias
- [Milestone M1](../../docs/milestones/M1-servidor-local.md)
- [servidores.md](../../docs/servidores.md)
- [comandos.md](../../docs/comandos.md)

## Trabajo y aceptación
- [x] Fixtures prueban métricas desactivadas, timeout/formato inválido y modelo no cargado sin cifras inventadas. Ctrl+C de logs -f termina seguimiento, no servidor. Probar retención sin borrar log activo y acceso tras stop; no incorporar TUI.
- [x] Pruebas pertinentes y publicación/ejecución Native AOT win-x64 del recorrido entregado.
- [x] Registrar comandos ejecutados, resultados, decisiones técnicas y limitaciones en esta tarjeta.

## Límites
Sólo M1; no implementar M2 ni agregar comandos ficticios. Las reglas canónicas prevalecen sobre este resumen. Mantener cambios pequeños; justificar dependencias nuevas con evidencia AOT.

## Evidencia
Implementación y validación del 9 de octubre de 2026. El usuario aprobó la entrega,
dio por terminada la tarea y autorizó el commit conjunto de implementación, pruebas,
documentación, evidencia y la tarjeta 017 draft. La 013 queda complete; la 017 conserva
draft sin implementación. Dependencia 012 complete verificada por board/view.

### Entrega y decisiones
- Server ps verifica los procesos y muestra instantáneas por preset del INI ejecutado.
  Consulta GET /models, y sólo para loaded consulta GET /metrics con model escapado
  y autoload=false. Nunca carga modelos para medirlos; fallos/desactivación/estado
  no cargado no producen cifras inventadas ni eliminan la instancia.
- Plazo de cinco segundos por instancia y 1 MiB por respuesta. Sin redirects ni proxy.
  Tasas prompt/generación y contadores Prometheus reconocidos, sin sumar modelos
  ni muestras ambiguas. Incertidumbre de identidad conserva registro y proceso.
- Logs muestra salida reciente acotada a 64 KiB; -f sigue el mismo archivo. Ctrl+C
  sólo interrumpe esa consulta. Salida CLI UTF-8 conserva texto acentuado al redirigir.
- Last-log.json bajo run/<servidor> guarda IDs estables y ruta validada. Se publica
  atómicamente antes de activar el proceso y antes de limpiar una instancia detenida;
  no representa una instancia activa ni un historial. Una referencia de otro servidor
  no se reconecta por nombre. No se adivinan logs antiguos sin referencia; una
  instancia anterior aún registrada establece la referencia al consultar logs o detener.
- El usuario aceptó 100 MiB por servidor, retiro de logs antiguos y protección del
  activo/último incluso si exceden el límite. Se limpia al iniciar, detener o consultar
  logs; no hay truncado ni rotación continua. Config.json admite logRetention.maxBytes
  positivo; ausencia conserva 100 MiB. No hay paquetes nuevos.
- Por solicitud del usuario se creó [017 draft](../017-configuracion-retencion-logs/README.md)
  para diseñar consulta/ajuste por CLI. Su implementación queda fuera de 013.
- Reglas y referencia técnica en [servidores.md](../../docs/servidores.md#consulta-de-logs).

### Verificación
```powershell
dotnet build Coquena.slnx
dotnet run --project tests/Coquena.Tests -- --runtime-tests
dotnet publish src/Coquena -c Release -r win-x64 -o artifacts/task013-native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
dotnet publish tests/Coquena.Tests -c Release -r win-x64 -o artifacts/task013-tests-native -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false
& artifacts/task013-tests-native/Coquena.Tests.exe artifacts/task013-native/coquena.exe
& artifacts/task013-checks/validate-docs.ps1
git diff --check
leanspec board
leanspec search metricas
leanspec view 013
leanspec view 017
```

Build y publicación Native AOT win-x64 del producto y runner: sin errores ni
advertencias, con análisis estricto. Pruebas administradas finales: 20 de métricas/
retención y 64 de ciclo de procesos. Incluyen dos modelos con tasas distintas,
escapado URL, modelo no cargado sin petición de métricas, desactivación sin red,
HTTP fallido, timeout, JSON/Prometheus inválido, etiquetas/timestamps, muestras
ambiguas, protección del log activo, cancelación de seguimiento, consulta tras stop,
último log de otro serverId rechazado y salida UTF-8 acotada.

El proceso fixture Windows y la CLI reciben un evento Ctrl+C real durante logs -f:
el seguidor termina y la identidad del servidor se conserva sin recibir esa señal.
Las pruebas de proceso mantienen stop/restart, recuperación y publicación segura.
Una carrera en el desmontaje de imágenes de hijos Windows podía ocultar un fallo
con una excepción de limpieza del home de prueba; la limpieza admite ahora hasta
tres segundos de reintentos, sin relajar ninguna aserción del producto.

Documentación: 37 diagramas PlantUML, 3 ejemplos JSON y 110 enlaces locales correctos,
mediante script local en artifacts/task013-checks. Sin cambios de diagramas ni
herramientas incorporadas al producto. git diff --check correcto.

Suite completa nativa final exit 0, con producto y runner publicados del código final:
CLI 9+9, contratos, config/presets 75, definiciones 68, métricas/retención 20, procesos
64, almacenamiento, backends 181, descargas 54, instalación de backend 56,
resolución 217, HF 168, GGUF/integridad 127 e instalación de modelos 120.
Las comprobaciones ejecutadas desde el runner nativo y las invocaciones de CLI
nativa acreditan el recorrido entregado; no sustituyen el recorrido real de 014.

### Límites
Fixtures pequeños con homes temporales; no se modifica ${CoquenaHome} ni se descargan
pesos. No se acredita inferencia ni métricas con backend/pesos reales, recorrido de 014.
La protección de logs puede exceder el límite aceptado. Logs -f no cambia de archivo
tras restart y sigue esperando hasta Ctrl+C aunque la ejecución termine.
