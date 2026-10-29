---
status: complete
created: 2026-10-21
priority: high
tags:
- m3
depends_on:
- 053-m3-servicios
created_at: 2026-10-21T23:26:05.469823600Z
updated_at: 2026-10-29T13:53:11.687167800Z
completed_at: 2026-10-29T13:53:11.687167800Z
transitions:
- status: in-progress
  at: 2026-10-29T12:44:35.914616900Z
- status: complete
  at: 2026-10-29T13:53:11.687167800Z
---
# Recuperar snapshots funcionales sin reinicios implícitos de consumidores

## Objetivo

Conservar disponibilidad ante fallos recuperables de la candidata conforme al modo de ejecución.

## Fuente de verdad

- [Milestone M3](../../docs/milestones/M3-definiciones-declarativas.md)
- [servidores.md#recuperación-y-fallos](../../docs/servidores.md#recuperación-y-fallos)
- [persistencia-proyectos.md](../../docs/persistencia-proyectos.md)
- [definiciones-declarativas.md](../../docs/definiciones-declarativas.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Trabajo y aceptación

- [x] Aplicar recuperación desacoplada en la misma invocación, incluida preparación fallida: cada invocación intenta una vez la candidata, aunque haya fallado antes; si falla recupera última buena informando en terminal y log, y si funciona la promueve y elimina la marca de fallo.
- [x] Preparar recursos exactos faltantes del snapshot sin resolver latest, recorrer históricos anteriores ni reintentar indefinidamente.
- [x] Ofrecer decisiones en modo interactivo y diagnóstico finito cuando no haya recuperación posible, sin bloquear la recuperación ya completada por una UI.
- [x] Mantener integridad inválida conocida como fallo fatal en ambos modos, preservando la instancia activa y sin eludirlo mediante fallback.
- [x] Respetar service.recovery.allow-draft-last-good y separar éxito local de borrador, preparación o éxito importado.
- [x] Probar doble fallo, recursos ausentes, YAML distinto del snapshot y conservación de contribuciones ajenas; la recuperación de una publicación no autoriza reiniciar el servicio compartido.

## Dependencias

- [053: Registrar servicios y operar su proceso con identidad segura](../053-m3-servicios/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin supervisor permanente, editor de borradores o alertas no configuradas. Las notificaciones se integran en 061.

## Estado y evidencia

Implementación, documentación, evidencia y reporte de hallazgos revisados y
aprobados explícitamente por el usuario el 29 de octubre de 2026. Cierre de la
tarjeta y un único commit de todos los archivos de la entrega autorizados.
No se añadieron paquetes ni se ejecutaron descargas de modelos o backends reales.

### Decisión aprobada y documentación

El usuario sustituyó expresamente la preferencia previa por una candidata ya
fallida: cada invocación daemon vuelve a intentarla una vez y, si falla, recupera
la última buena; si funciona, elimina la marca de fallo. En interactivo, cada
reintento exige decisión del usuario. Se actualizó esa regla en servidores.md;
es una decisión funcional nueva aprobada, no una adaptación implícita al código.
La tabla de persistencia aclara que última buena conserva ejecución, endpoint y
plazos de salud probados; esos datos permiten recuperar sin leer el YAML actual.

### Implementación

- Un bloqueo de ciclo cubre preparación, candidata y recuperación. Se captura la
  última buena local antes del intento; sólo se recurre a ella, una vez. No hay
  supervisor, búsqueda de históricos ni interacción durante recuperación daemon.
- Un fallo de preparación de restart conserva la instancia anterior, sin detenerla
  ni sustituirla. Se informa el fallo; la salida es 0 con warning si se conserva
  disponibilidad, conforme a comandos.md. Se ajustó la expectativa anterior de
  esa regresión, que exigía error aun conservando el proceso.
- Fallo de salud/arranque reconcilia y detiene exclusivamente la candidata verificada
  antes de recuperar. Cancelación, conflictos e identidad incierta no autorizan un
  reemplazo. Integridad inválida conocida sigue siendo fatal, sin fallback ni reparación.
- La recuperación compone argumentos y recursos del snapshot probado. Recursos
  gestionados ausentes se preparan con selección y referencias exactas, comprobando
  que no cambien; una dependencia ausente de backend se reconstruye en otra instalación,
  conservando los archivos anteriores. Externos ausentes requieren intervención.
- Una ejecución sana promueve última buena sólo si es elegible. Un borrador requiere
  allow-draft-last-good explícito; preparación/locks recibidos no acreditan éxito
  local. El diagnóstico fallido de la candidata permanece tras recuperar la anterior
  y se elimina cuando funciona la candidata.
- Avisos de recuperación van a stderr en la CLI y a un log local por servicio
  (local/runtime/<service-id>/recovery.log); el registro del servicio conserva el
  resultado y códigos estables. Fallar la escritura de ese log informa el problema
  sin revertir una recuperación completada.
- Se conservan snapshots y recursos de otros servicios. No se añadió ninguna llamada
  desde publicación de presets al reinicio de procesos. La restauración de entradas
  INI de varios consumidores se integra y acredita en 055, según sus propios criterios;
  estas pruebas no acreditan una publicación de presets todavía inexistente.

### Validación ejecutada

Evidencia pública anonimizada. Estado, fixtures y publicaciones de la validación
final en el volumen acordado; rutas reales y salidas crudas sólo locales.

- Build Release final: cero errores y advertencias. Publicación Native AOT win-x64
  de producto y runner, con TreatWarningsAsErrors=true, correcta.
- Recuperación: 24 comprobaciones administradas y 32 Native AOT, incluidas invocaciones
  reales del producto publicado. Se prueban fallback por salud y preparación, YAML
  inválido distinto del snapshot, reintento daemon de candidata fallida, reintento y
  elección interactiva del núcleo, restart interactivo tras detener la anterior,
  doble fallo, ausencia de última buena, integridad fatal, conflictos, promoción de
  borradores, preservación de otro servicio y avisos en terminal/log.
- Recursos exactos: fixture oficial sin red real; se elimina una dependencia de b1,
  se cambia latest a b2 y se acredita que sólo se solicita b1, en otra instalación,
  conservando snapshot y referencias de integridad. También se prueban bloqueo por
  integridad conocida y externo ausente sin sustitución.
- Servicios: 33 comprobaciones administradas y 35 Native AOT con producto publicado,
  incluyendo cancelación real de consola durante salud, hijos sobrevivientes,
  identidad incierta y conservación de evidencias.
- Persistencia: suite administrada y Native AOT correcta, con selección global/propia,
  referencias, salud, procesos e identidad. Combinaciones: 69 comprobaciones en ambos
  modos. Ayuda/aliases y 16 errores sin efectos: correctos con runner/producto nativos.
- Backends: 49 comprobaciones administradas correctas; la repetición Native AOT con
  producto publicado pasó 56. Una ejecución Native AOT previa falló en el monitor
  de limpieza; se conserva como hallazgo y no se presenta la validación como libre
  de fallos intermitentes.
- 173 rutas existentes del mapa, 14 enlaces documentales locales, espacios finales
  de archivos nuevos y git diff --check correctos. No se modificaron
  diagramas PlantUML ni ejemplos JSON documentales.

### Hallazgos para evaluar

- **Fallo intermitente del monitor de descargas.** Una ejecución Native AOT de la
  suite de backends produjo FileNotFoundException al consultar una petición de
  control mientras desaparecía el directorio de una descarga. La repetición pasó.
  En la evaluación posterior solicitada por el usuario, 23 ejecuciones de la suite
  sobre un export limpio del commit base 98430d3 pasaron 49 comprobaciones cada una.
  Una prueba controlada de la ventana de concurrencia sí reprodujo la excepción
  sobre el ensamblado base y sobre el actual, aun completándose el finalizador;
  también se reprodujo con un probe Native AOT enlazado al ensamblado base.
  Queda demostrada la existencia de esa condición en el código anterior a 054;
  no se afirma que la suite ordinaria falle de manera determinista ni se atribuye
  pérdida de datos a esta excepción. No se modificaron DownloadManager,
  ScopeTransfers ni ScopeContext. La evaluación y recomendación se describen abajo;
  no se creó una tarea ni se implementó una corrección automáticamente.
- **Identidad incierta impide recuperar disponibilidad automáticamente.** Se conserva
  la protección de 053: si termina el principal y quedan hijos sin un handle previo
  verificable, se preserva la evidencia y se bloquea el reemplazo. La suite de
  servicios lo prueba. La decisión sigue la regla canónica de identidad, con impacto
  operativo real: la recuperación no puede sustituir ese grupo por conveniencia.
  Las interfaces de M4 deben representar ese diagnóstico y la intervención requerida;
  no se añade una política nueva de señales por PID ni un supervisor.
  A solicitud del usuario, se registró por separado en
  [hallazgo 001](../../findings/001-hijos-identidad-incierta.md), sin crear una tarjeta.

### Evaluación adicional de coordinación monitor/limpieza

Solicitada por el usuario el 29 de octubre de 2026. La evaluación, reproducción
controlada sobre el commit base y el actual, impacto y recomendación se mantienen
en el [hallazgo 002](../../findings/002-coordinacion-monitor-limpieza.md).
Se registró por separado a solicitud del usuario; no se modificó el motor ni se
incorporó su corrección al alcance de esta tarjeta.

### Reproducción

Definir $TestRoot fuera del repositorio en el volumen de validación acordado:

```powershell
New-Item -ItemType Directory -Force "$TestRoot/temp" | Out-Null
$env:TEMP = "$TestRoot/temp"
$env:TMP = $env:TEMP
dotnet build Coquena.slnx -c Release
dotnet publish src/Coquena -c Release -r win-x64 -p:TreatWarningsAsErrors=true -o "$TestRoot/product"
dotnet publish tests/Coquena.Tests -c Release -r win-x64 -p:PublishAot=true -p:TreatWarningsAsErrors=true -o "$TestRoot/tests"
& "$TestRoot/tests/Coquena.Tests.exe" --service-recovery-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --scoped-service-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --project-persistence-tests
& "$TestRoot/tests/Coquena.Tests.exe" --combination-tests
& "$TestRoot/tests/Coquena.Tests.exe" --scoped-backend-tests "$TestRoot/product/coquena.exe"
& "$TestRoot/tests/Coquena.Tests.exe" --help-tests "$TestRoot/product/coquena.exe"
git diff --check
```

Board/search/view y dependencia 053 revisados manualmente. leanspec deps sigue
sin migrar en el adaptador; no se presenta como una comprobación exitosa.
La aprobación cierra esta entrega; no acredita aceptación integrada de M3.
Los hallazgos 001 y 002 permanecen registrados para evaluación, sin correcciones
ni tareas adicionales implícitas.
