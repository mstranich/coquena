---
status: complete
created: 2026-10-21
priority: high
tags:
- tui
- dashboard
- m4
depends_on:
- 062-m3-seguimiento-logs
- 066-m3-tui-base
created_at: 2026-10-21T23:26:06.949521100Z
updated_at: 2026-11-07T16:30:16.349567200Z
completed_at: 2026-11-07T16:30:16.349567200Z
transitions:
- status: in-progress
  at: 2026-11-07T14:07:28.969582300Z
- status: complete
  at: 2026-11-07T16:30:16.349567200Z
---
# Integrar logs continuos con selección de servicio y navegación

## Objetivo

Centralizar logs debajo de hardware y actividad con continuidad por identidad de servicio.

## Acuerdo de producto y continuidad entre sesiones

M4 es un milestone abierto, centrado en una Terminal UI con buena experiencia de
usuario. Se agregan, corrigen y refinan tarjetas una por una a partir del uso y la
revisión visual. Las tarjetas actuales no definen un inventario cerrado ni su
finalización implica cerrar M4. Cada tarjeta mantiene un alcance concreto; el
horizonte abierto no autoriza implementar otras secciones sin acordarlas.

La primera entrega utilizable es [066](../066-m3-tui-base/README.md): observar
inferencia real con fases, avance disponible hacia la respuesta, curvas de tokens,
promedio/pico, consumo y logs dentro de la terminal. Leer su intención, matriz de
fuentes y aceptación antes de implementar esta tarjeta. Una carcasa de navegación,
hardware aislado o un indicador binario de actividad no satisfacen ese recorrido.

Se comienza por 066; 065, 067, 068 y 069 amplían una experiencia ya utilizable;
070 contrasta su integración. Los IDs/directorios conservan nombres históricos,
no fijan el orden. No esperar a 070 para integrar datos o probar inferencia real.
La Web UI será otra iteración: lo aprendido de UX y los contratos sin widgets
servirán de base, sin implementar ahora web ni copiar literalmente su layout.

## Fuente de verdad

- [Milestone M4](../../docs/milestones/M4-experiencia-completa.md)
- [ui-servicio.md](../../docs/design/ui-servicio.md)
- [interfaces-visuales.md](../../docs/design/interfaces-visuales.md)
- [observabilidad.md](../../docs/design/observabilidad.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Base entregada en 066

La [066](../066-m3-tui-base/README.md#estado-y-evidencia) entrega el panel de
logs por servicio y refresca un tramo reciente del archivo. La lectura es
acotada. Quedan por integrar el seguimiento continuo de 062 y la experiencia
de pausa, lectura estable, regreso al final e históricos.

## Implementación

- Configuración después de Sistema conserva la casilla persistente «Filtrar
  logging de monitoreo», activada por defecto. El filtro se aplica al buffer
  capturado antes de poblar la vista, sin tocar los archivos.
- ServiceLogSession integra FollowAsync de 062 por ID de servicio, independiente
  de las consultas HTTP y del refresco de inferencia. Cada servicio visitado
  conserva captura acotada durante la navegación y las lecturas históricas.
- Logs en orden cronológico, seguimiento al final, pausa propia y lectura estable
  al seleccionar/desplazar filas. «Actual / seguir» abandona el histórico y retoma
  el final actual. La pausa del monitor no pausa los logs ni la captura.
- Históricos por fecha, instancia y archivo registrado, con lectura de extensión
  fija en segundo plano y cancelación entre bloques. Las selecciones rápidas se
  serializan y cancelan la anterior. Un archivo eliminado se informa como no
  disponible; no se sustituye por otro.
- Buffers de hasta 2000 filas completas y 256 Ki unidades UTF-16, más una línea
  parcial acotada. Segmentación de líneas largas sin romper pares sustitutos,
  scroll horizontal/vertical y contador visible de descartes. La vista congelada
  conserva su texto mientras llegan datos nuevos al buffer de captura.
- Panel compartido cuando hay altura; Monitor y Logs como subpestañas del mismo
  servicio en terminal baja. Se conservan fase/progreso, identidad del servidor
  y hardware de alcance global. No se alteran las mejoras de 067 y 068.
- El diálogo de históricos usa Dialog.Result: Terminal.Gui procesa la activación
  de sus botones antes de los eventos individuales Button.Accepted. La selección
  se aplica una vez terminado el diálogo, con cancelación diferenciada.

## Trabajo y aceptación

- [x] Agregar Configuración después de Sistema con la casilla persistente «Filtrar logging de monitoreo», activada por defecto.

- [x] Usar seguimiento de 062: pestaña de servidor y alias default, cambio de log entre reinicios/rotación y señal visible de transición; esperar cuando el servicio esté detenido.
- [x] Verificar que al cambiar de servicio, la actividad y el log conservan el mismo destino mientras el hardware mantiene alcance global.
- [x] Permitir scroll, pausa visual, regreso al seguimiento y acceso a históricos; continuar captura en buffers acotados sin congelar tablas ni peticiones.
- [x] Destinar al log el espacio restante y moverlo a una subpestaña del mismo servidor cuando falte altura, conservando fase y progreso en Monitor; soportar líneas largas, Unicode, ausencia de archivo y pérdida de acceso.
- [x] Probar dos servicios, ráfagas de logs, resize, retención, reinicio y eliminación/recreación del mismo nombre con otra identidad; no enlazar silenciosamente servicios distintos.
- [x] Ejecutar la TUI publicada con logs reales y comprobar que salida/pausa no detienen el backend ni provocan crecimiento ilimitado.

- [x] Seleccionar históricos por instancia/archivo y volver inequívocamente al servicio actual, distinguiendo histórico, pausa visual, espera y desconexión.
- [x] Conservar posición/foco de lectura al llegar líneas; comunicar descartes del buffer si corresponde sin prometer conservación ilimitada.
- [x] Validar rotación, truncado, eliminación por retención, ráfagas y pérdida de acceso sin congelar gráficas ni hardware.
- [x] Revisar panel compartido y subpestaña en terminal baja; la identidad del servidor permanece visible. No sustituir el monitor por una consola textual.

## Dependencias

- [062: Seguir logs por identidad de servicio entre reinicios y rotación](../062-m3-seguimiento-logs/README.md)
- [066: Abrir la interfaz de terminal y navegar entre servicios conocidos](../066-m3-tui-base/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin mezclar logs de distintas instancias como una sola ejecución ni modificar configuración del servicio al navegar.

## Estado y evidencia

Implementación completa, pendiente de revisión y commit. Se mantiene el título,
alcance y dependencias originales. No se cierra M4 ni se absorbe la integración
ampliada de 070.

### Validación ejecutada

Entorno Windows win-x64 anonimizado, datos de prueba aislados en D:, sin descargas
ni inferencia. Logs producidos por procesos backend fixture reales; las métricas
de inferencia no se inventan cuando esos fixtures no exponen /slots.

Definir $Native como un directorio local de publicación en D:. Desde la raíz:

~~~powershell
dotnet build tests/Coquena.Tests -c Release --no-restore
dotnet publish src/Coquena/Coquena.csproj -c Release -r win-x64 -o $Native -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false
dotnet run --project tests/Coquena.Tests -c Release --no-build -- --service-log-follow-tests "$Native/coquena.exe"
dotnet run --project tests/Coquena.Tests -c Release --no-build -- --dashboard-tests
dotnet run --project tests/Coquena.Tests -c Release --no-build -- --service-log-hook-tests "$Native/coquena.exe"
git diff --check
~~~

- Build y publicación Native AOT sin advertencias ni errores en la versión final.
- Seguimiento: 71 comprobaciones, incluidas 47 administradas y 24 del producto
  nativo. Cubren dos servicios, snapshots de lectura estables, Unicode, controles
  de terminal, líneas largas y UTF-16 inválido, cotas de memoria, filtro, ráfagas,
  selección histórica, retorno al actual, captura durante consulta histórica,
  cancelación, archivo eliminado/recreado, acceso bloqueado y reintento, truncado,
  drenaje/orden entre reinicios y rechazo de servicio eliminado/recreado por nombre.
- Dashboard: 51 comprobaciones de regresión de observación y presentación.
- Logs/hooks: 32 comprobaciones, incluida la retención y protección de archivos.
- TUI Native AOT en terminal: dos servicios (global default y propio), cambio de
  destino coherente, hardware global, Configuración y filtro marcado por defecto.
  La persistencia de la casilla al reabrir también se verificó al restaurarla.
- Se recorrieron Monitor y Logs en 80×24 y el panel compartido en 120×40. Se
  ejercitaron cambios de tamaño con la TUI abierta sin caída del proceso.
- Selección de una instancia anterior en Históricos, identificación explícita de
  lectura fija y retorno al actual comprobados en el binario nativo corregido.
  Se corrigió el cierre sin selección detectado durante esta prueba interactiva.
- Lectura pausada por selección/scroll conservada ante una ráfaga de 10 000 líneas
  sobre un log fixture detenido. El contador informó 8535 filas descartadas y el
  regreso al seguimiento mostró la última línea de la ráfaga. Hardware y consultas
  siguieron actualizándose. Es evidencia de acotación, no un benchmark.
- Un reinicio explícito durante pausa cambió la instancia capturada sin mover el
  texto congelado; al reanudar se mostró la ejecución nueva. Espera con servicio
  detenido y continuación de captura también comprobadas.
- Esc restauró la terminal con código 0. La consulta posterior de procesos confirmó
  vivo el backend propio; sólo después se detuvieron explícitamente los fixtures.
- Enlaces locales, referencias del mapa de código y git diff --check correctos.
  No se modificaron diagramas PlantUML ni ejemplos JSON.

### Límites y hallazgos para revisión

- El buffer visual no es un archivo permanente: al agotar la cota descarta filas
  antiguas, aunque estuvieran ocultas por el filtro. Los históricos conservan sólo
  el tramo que cabe en ese buffer. Los archivos originales siguen sujetos a la
  retención existente, sin cambios. La documentación funcional concreta esta
  lectura acotada, el orden cronológico y la pausa independiente del monitor.
- El filtro también oculta reenvíos de inferencia: el mensaje del router no incluye
  ruta HTTP. Se conserva la preferencia solicitada y se explicita esa limitación.
- La prueba del diálogo reveló que el selector de recursos existente usa también
  Button.Accepted sin consultar Dialog.Result. Su posible efecto en ese recorrido
  es una hipótesis por evaluar; no se ejercitó ni modificó ese selector aquí.
- Memoria acotada por servicio visitado e histórico seleccionado; no se acredita
  rendimiento con catálogos masivos, almacenamiento remoto ni inferencia real.
  La ampliación integrada y revisión de UX de 070 conservan su propio alcance.
- Sin paquetes nuevos, publicación de release, push ni commit.
