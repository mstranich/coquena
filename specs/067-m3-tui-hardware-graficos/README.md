---
status: complete
created: 2026-10-21
priority: high
tags:
- tui
- dashboard
- m4
depends_on:
- 042-unidades-legibles
- 063-m3-estado-metricas
- 066-m3-tui-base
created_at: 2026-10-21T23:26:06.764688800Z
updated_at: 2026-11-07T12:33:08.472224700Z
completed_at: 2026-11-07T12:33:08.472224700Z
transitions:
- status: in-progress
  at: 2026-11-07T01:02:31.776347900Z
- status: complete
  at: 2026-11-07T12:33:08.472224700Z
---
# Mostrar hardware y gráficas temporales legibles en la terminal

## Objetivo

Hacer visible consumo de CPU, RAM y cada GPU/VRAM con cifras y gráficas que mantengan su alcance y disponibilidad.

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
- [glosario.md](../../docs/design/glosario.md)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Base entregada en 066

La [066](../066-m3-tui-base/README.md#estado-y-evidencia) ya entrega barra común,
gráficos acotados de CPU, RAM, GPU y VRAM, numeración por adaptador y un
componente reutilizable de dos líneas. Sistema muestra el inventario y el
sondeo; la barra y los gráficos de GPU usan los adaptadores correlacionados con
CUDA o Vulkan. Esta tarjeta refina y acredita esa base.

## Trabajo y aceptación

- [x] Alinear visualmente muestras de recursos con fases; conservar fecha/resolución efectiva sin afirmar causalidad o consumo exclusivo de un preset.
- [x] Detallar múltiples GPU, RAM/VRAM y alcance proceso/máquina/dispositivo; no sumar ni atribuir varias veces recursos compartidos.
- [x] Refinar ejes, escalas, leyendas y detalle de fuente/unidad/ventana. Separar cadencia de refresco y muestreo; señalar vigencia, huecos y datos ausentes sin interpolar ni mostrar ceros falsos.
- [x] Verificar que la barra y las series comparten muestras y conservan continuidad al cambiar de espacio, servicio o panel. Aislar fuentes lentas y fallos por instancia.
- [x] Revisar terminal grande y compacta, resize y lectura sin color con múltiples dispositivos, permisos insuficientes y proveedores ausentes o lentos. Medir cotas de captura y memoria.
- [x] Repetir en Native AOT con cifras reales y contrastar fuente, alcance y disponibilidad de cada medición.

## Dependencias

- [042: Unificar tamaños y memoria con escala 1024 y presentación legible](../042-unidades-legibles/README.md)
- [063: Consultar servicios y hardware con métricas atribuibles y salida finita](../063-m3-estado-metricas/README.md)
- [066: Abrir la interfaz de terminal y navegar entre servicios conocidos](../066-m3-tui-base/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin sumar totales compartidos como consumo de cada servicio, benchmark ni guardar series históricas fuera de la sesión.

## Estado y evidencia

Implementación realizada y validada con Native AOT win-x64. La revisión acepta
la evidencia disponible y sus límites documentados.
El contenido público está anonimizado; rutas, dispositivos y salidas crudas del
equipo de prueba se conservan sólo localmente.

### Comportamiento implementado

- Las series usan un eje UTC común con columnas de dos segundos. Conservan hasta
  24 columnas y distinguen lectura cero, ausencia de lectura y datos antiguos.
  La fase de la tarea enfocada usa el mismo eje. Los cambios de tarea reinician
  su tramo; las series de máquina continúan entre servicios, Sistema y paneles.
- La selección de GPU conserva la correlación por identidad con CUDA o Vulkan
  confirmado, según aclaración del usuario. Un dispositivo temporalmente ausente
  conserva su número y sus huecos. Los adaptadores sin confirmar quedan en el
  inventario de Sistema.
- El usuario aprobó marcar como antigua una cifra con más de cuatro segundos.
  La marca conserva el valor y la fecha en el detalle; cada punto histórico
  mantiene su tiempo. Recursos expone unidades, fuentes, ventanas, alcances,
  indisponibilidad, duración de captura e intervalo efectivo.
- CPU/RAM de máquina, GPU/VRAM compartida, memoria comprometida de instancia y
  RAM residente de procesos verificados se presentan con alcances separados.
- El refresco es de un segundo y cada fuente conserva al menos dos segundos
  entre inicios reales de captura. Las capturas de una fuente no se solapan.
  Se admiten hasta cuatro capturas de servicios en vuelo; sus resultados se
  contrastan con la selección vigente. Una fuente lenta conserva huecos y
  no bloquea el refresco ni la consulta de otra instancia.
- Windows llegó a entregar una agregación PDH de actividad GPU superior a la
  escala porcentual. Esa lectura pasa a no disponible con motivo, mientras
  VRAM y los demás dispositivos conservan sus propias observaciones.
- Se documentaron las decisiones acordadas y la lectura inválida. También se
  corrigió la ubicación de la barra en interfaces-visuales.md para reflejar
  el borde inferior acordado y acreditado previamente en 066.

### Verificaciones ejecutadas

- Compilación Release y publicación Native AOT del producto y del runner win-x64,
  con warnings de IL/trimming tratados como errores. Dependencias obtenidas de
  caché con una fuente local vacía y publicaciones posteriores sin restore.
- 35 comprobaciones del dashboard y correlación. Incluyen dos GPU con huecos
  independientes, fase y recursos sobre el mismo eje, lectura cero, muestras
  repetidas o atrasadas, cambio de ancho, ventana vencida, antigüedad y rechazo
  de porcentajes inválidos.
- 68 comprobaciones de observación en el runner Native AOT, incluyendo consultas
  del producto publicado, hardware real, instancias verificadas y separación
  de memoria residente y comprometida.
- Cuatro comprobaciones de inferencia real con el build 12058 / 3f8af15a7 y un
  GGUF local pequeño. Se ejercitaron dos presets del mismo modelo, concurrencia,
  tasas independientes, cancelación y finalización observada. En una ejecución
  paralela, la TUI publicada mostró generación real y su marca G sobre el eje
  de hardware. La duración breve de las peticiones limitó las tasas visibles
  en esa ejecución de la TUI.
- Uso interactivo por PTY con el binario Native AOT en 80x24, 120x32 y 140x40,
  con hardware real y un fixture de dos GPU confirmadas más otra sin confirmar.
  Se revisaron lectura sin color, Recursos, desplazamiento de detalle, cambios
  global/proyecto/Sistema y salida con Esc. Las ejecuciones verificadas
  terminaron con código 0 y restauraron pantalla alternativa y cursor.
- El fixture inyecta una lectura de GPU que envejece mientras otra se renueva,
  permiso insuficiente, captura fallida y una demora de seis segundos. En una
  sesión de 108 capturas se midió una sola captura simultánea, una máxima de
  6012 ms y un pico de RAM del proceso inferior a 54 MiB. Son mediciones del
  fixture y de ese entorno, sin garantía de rendimiento en otros equipos.
- El histórico mantiene 24 columnas después de 10000 inserciones y la prueba
  acota sus asignaciones adicionales a menos de 16 KiB.

### Límites de la evidencia

El resize del histórico está probado por cambios de ancho y se usó la TUI con
distintos tamaños iniciales. Los intentos de cambiar el tamaño del PTY durante
la sesión conservaron sus dimensiones o devolvieron un error de tamaño de
búfer. Falta acreditar la transición visual en una terminal de escritorio.

Durante un lanzamiento inicial del runner desde una consola de escritorio,
Windows mostró un aviso de saturación de búfer. La causa no quedó determinada
en los registros disponibles. El fallo no se reprodujo con los binarios finales
en las ejecuciones por PTY; ese lanzamiento de escritorio requiere contraste.

No se acreditan cambios físicos de driver/GPU ni permisos reales restringidos
en el equipo. Los casos adversos se ejercitaron mediante fixtures. La retención
de gráficos continúa limitada a la sesión. No se modificó la herramienta global
instalada ni se creó commit.

### Reproducción

Definir $TestRoot como una carpeta local de validación en D:. Las subcarpetas
de fixture interactivo deben ser nuevas. Se requiere una terminal interactiva
para ese recorrido y dependencias previamente disponibles en caché.

~~~powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet publish src/Coquena -c Release -r win-x64 --no-restore -o "$TestRoot/product" -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false
dotnet publish tests/Coquena.Tests -c Release -r win-x64 --no-restore -p:PublishAot=true -o "$TestRoot/tests" -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false
& "$TestRoot/tests/Coquena.Tests.exe" --dashboard-tests
& "$TestRoot/tests/Coquena.Tests.exe" --observation-tests "$TestRoot/product/coquena.exe"
$env:NO_COLOR = '1'
& "$TestRoot/tests/Coquena.Tests.exe" --dashboard-fixture "$TestRoot/visual-new" synthetic
git diff --check
~~~

El comando publish de estos ejemplos genera archivos locales en $TestRoot.
