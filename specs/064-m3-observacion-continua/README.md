---
status: archived
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 063-m3-estado-metricas
created_at: 2026-10-21T23:26:06.353833100Z
updated_at: 2026-11-04T19:52:46.480060900Z
completed_at: 2026-11-02T21:52:05.502269500Z
transitions:
- status: in-progress
  at: 2026-11-02T20:54:19.332198400Z
- status: complete
  at: 2026-11-02T21:52:05.502269500Z
- status: archived
  at: 2026-11-04T19:52:46.480060900Z
---
# Observar con ps -w o --watch y reconciliar eventos

## Objetivo

Añadir observación continua explícita y transporte reutilizable mientras el observador permanezca abierto.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [observabilidad.md](../../docs/design/observabilidad.md)
- [comandos.md](../../docs/design/comandos.md)
- [ui-servicio.md](../../docs/design/ui-servicio.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [x] Implementar ps -w y ps --watch como flags equivalentes, no subcomando ni comando independiente; sin flag conservar salida finita.
- [x] Redibujar terminal y tratar salida redirigida conforme al contrato; cancelar cierra sólo el observador.
- [x] Suscribir eventos soportados y reconciliar snapshot al conectar/reconectar, sin asumir replay durable o campos universales de forks.
- [x] Aplicar reconexión con preferencias globales y distinguir cambio de identidad, desconexión y terminación; no reiniciar para reconectar.
- [x] Separar frecuencia de fuente y refresco visual, mostrar cambios externos y evitar conversaciones privadas o instrumentación que genere inferencia.
- [x] Probar pérdida de conexión, eventos desconocidos, cambio de proceso y ausencia de fuentes; exponer componentes reutilizables por dashboard.

## Dependencias

- [063: Consultar servicios y hardware con métricas atribuibles y salida finita](../063-m3-estado-metricas/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin servicio residente, streams privados, benchmarking o nuevas políticas de ejecución.

## Estado y evidencia

Implementación realizada y validada; permanece in-progress para revisión y cierre
explícito. Sin commit, push, tag o release. Evidencia anonimizada: ubicaciones,
hardware, identificadores de dispositivos y logs originales se conservan fuera de Git.

### Decisiones acordadas

El usuario autorizó las recomendaciones tras revisar los puntos abiertos del
contrato: refresco visual de un segundo, fuentes cada dos segundos sin solapar,
reconciliación periódica de cinco segundos y reconexión exponencial desde uno
hasta treinta segundos configurable globalmente. También autorizó seguir la misma
identidad de servicio entre reinicios, separando instancias y esperando durante
la detención, sin enlazar una baja/alta con el mismo nombre.

La documentación funcional incorpora estas decisiones nuevas, que antes estaban
abiertas; no se derivan retrospectivamente del código. Se concretan además las
cotas técnicas y la diferencia entre catálogo conservado y confirmación actual.

### Implementación

- `ps -w` y `ps --watch` comparten selección general/global/servicio/receta con
  `ps` finito. La vista general renueva los registros conocidos; servicios recién
  registrados se observan por su identidad, sin retargetear identidades anteriores.
- El terminal utiliza pantalla alternativa y restaura cursor/pantalla al cancelar.
  Muestra un resumen ajustado al tamaño disponible. Redirigido emite snapshots
  textuales completos separados por newline, sin escapes de terminal.
- `ServiceWatch` publica snapshots para consumidores sin widgets ni propiedad del
  proceso. Mantiene una captura por servicio y un presupuesto compartido de cuatro
  capturas concurrentes. Reutiliza el cliente HTTP durante la sesión.
- El catálogo conserva su fecha entre reconciliaciones y se invalida por instancia,
  composición registrada, conexión o evento. Una copia conservada se identifica
  como `cached-catalog` y no confirma composición actual. Las métricas mantienen
  sus fechas independientes y `autoload=false`.
- `ModelEvents` ofrece frames SSE acotados, nombres, datos multilínea y fecha de
  recepción. El seguimiento puede entregarlos con servicio/instancia a adaptadores
  posteriores, sin interpretar payloads desconocidos ni reconstruir replay.
  Una sola señal pendiente agrupa invalidaciones y evita consultas por cada evento.
- Reconexión con `config observer-retry show|set|unset`, persistencia global AOT y
  validación antes de modificar estado. Fallos de SSE, API y proceso se distinguen.
  Se verifica identidad y escucha antes de cada conexión; redirecciones y proxies
  automáticos están desactivados. No se añaden paquetes.

### Validación ejecutada

- Build Release administrado: sin errores ni advertencias. Suite watch ampliada:
  26 comprobaciones; ayuda y 21 errores de sintaxis sin efectos.
- Publicación Native AOT win-x64 de producto y runner con advertencias como errores.
  Suite watch sobre el producto publicado y backend local: 26 comprobaciones;
  observación: 37; seguimiento de logs: 47. Ayuda y 21 errores de sintaxis sin efectos
  en las rutas administrada y Native AOT.
- Watch cubre eventos desconocidos/multilínea, frame incompleto excesivo, cancelación,
  espera exponencial, persistencia y restauración de preferencias, catálogo conservado,
  reconexión, API caída con proceso vivo, SSE ausente, reinicio con identidad nueva,
  ambos flags publicados, snapshots redirigidos y conservación del backend al salir.
- Router oficial local: conexión SSE real, catálogo inicial y entrada publicada por
  un cliente externo mediante recarga explícita de prueba. Las entradas permanecieron
  descargadas de memoria, con pesos inexistentes y sin peticiones de inferencia.
  La recarga la ejecuta el fixture externo, nunca el observador.
- Binario publicado en terminal con PTY de Windows: redibujado continuo, hardware
  y servicio detenido visibles, espera mientras está detenido y salida con Ctrl+C
  restaurando pantalla/cursor. Configuración publicada show/set/unset y `ps` finito
  también ejecutados con estado aislado.
- Enlaces Markdown locales afectados, referencias del mapa, revisión de privacidad
  de archivos nuevos/modificados y `git diff --check`: correctos. Sin cambios de
  PlantUML ni ejemplos JSON documentales. Board/search/view revisados manualmente;
  `leanspec deps` continúa sin migrar al adaptador.

Reproducción (definir ubicaciones propias en un volumen de pruebas y un backend
local compatible ya disponible; no se descargan pesos):

```powershell
$TestRoot = Join-Path $env:TEMP 'coquena-watch-validation'
$Backend = $env:COQUENA_TEST_BACKEND
dotnet publish src/Coquena -c Release -r win-x64 -p:TreatWarningsAsErrors=true -o "$TestRoot/product"
dotnet publish tests/Coquena.Tests -c Release -r win-x64 -p:PublishAot=true -p:TreatWarningsAsErrors=true -o "$TestRoot/tests"
& "$TestRoot/tests/Coquena.Tests.exe" --watch-tests "$TestRoot/product/coquena.exe" $Backend
& "$TestRoot/tests/Coquena.Tests.exe" --observation-tests "$TestRoot/product/coquena.exe" $Backend
& "$TestRoot/tests/Coquena.Tests.exe" --help-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --service-log-follow-tests "$TestRoot/product/coquena.exe"
$env:COQUENA_HOME = "$TestRoot/home"
& "$TestRoot/product/coquena.exe" ps --global --watch
# Ctrl+C cierra el observador.
```

El argumento de backend es opcional; omitirlo excluye el contraste con router real.
El runner crea fixtures aislados en el volumen de pruebas previsto para Windows.

### Hallazgos para revisión

- **La frecuencia efectiva depende de fuentes y cantidad de servicios.** Las
  capturas no se solapan y comparten cuatro permisos; SSE mantiene una suscripción
  por servicio vivo. Esta cota evita consultas simultáneas sin límite, pero no
  garantiza muestras de dos segundos con fuentes lentas o muchos servicios. Las
  fechas conservadas y el catálogo no confirmado evitan ocultar esa diferencia.
  Es una restricción técnica, no una medición de capacidad: resta evaluar escala
  y vigencia visual al integrar el dashboard de M3 y las interfaces de M4.
- **Watch ajusta el resumen a la terminal.** El texto que no cabe se recorta para
  no llenar el buffer; la salida redirigida conserva el snapshot completo. No
  ofrece navegación de dashboard ni acredita su usabilidad con muchos servicios.
  La integración de terminal posterior deberá evaluar esa densidad por su propio
  contrato, sin convertir este resumen en evidencia del dashboard integrado.

No se ejecutó inferencia, benchmark, suite general completa, validación Linux de
este seguimiento ni pruebas de gran escala. No se acredita correlación por tarea
ni aceptación integrada de M3. La documentación conserva los límites de identidad
de servicios Windows; el proveedor de hardware Linux no cambia ese alcance.
