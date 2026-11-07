---
status: complete
created: 2026-10-21
priority: high
tags:
- tui
- dashboard
- m4
depends_on:
- 065-m3-actividad-tareas
- 066-m3-tui-base
created_at: 2026-10-21T23:26:06.862426300Z
updated_at: 2026-11-07T14:04:15.371659700Z
completed_at: 2026-11-07T13:42:05.248511800Z
transitions:
- status: in-progress
  at: 2026-11-07T13:05:10.733521Z
- status: complete
  at: 2026-11-07T13:42:05.248511800Z
---
# Visualizar prompts, generación de tokens y tareas de cada preset

## Objetivo

Entregar el panel central donde se vea la actividad de llama-server por servicio y entrada, desde prompt hasta salida.

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
- [tui-servicio.salt.puml](../../docs/design/tui-servicio.salt.puml)

Las reglas funcionales de los documentos enlazados prevalecen. Esta tarjeta define trabajo y evidencia, sin crear una definición arquitectónica alternativa.

## Base entregada y reparto con 065

La [066](../066-m3-tui-base/README.md#primera-evidencia-de-curvas-de-inferencia)
ya presenta curvas de prompt y generación, tokens procesados, caché, media y
pico observado de una tarea atribuible en el preset enfocado. La evidencia
corresponde a un build. En ese build el contador de contexto del slot también
crece durante la generación y no acredita un porcentaje de avance de prompt.

La [065](../065-m3-actividad-tareas/README.md) proporciona identidad,
atribución, cifras y retención de tareas en el modelo de lectura. La 068 usa
ese modelo para mostrar y recorrer tareas y presets.

## Trabajo y aceptación

- [x] Priorizar las entradas activas y sus series temporales arriba del log. Mostrar servicio, entrada/preset y modelo correlacionado, slot, tarea y fase; conservar estas identidades al navegar y distinguir instancias.
- [x] Separar resumen por entrada (acumulados, medias del backend, actividad/cola agregada y período) del detalle por tarea, según el Salt actualizado y observabilidad. Mostrar totales compatibles sin sumar medias como velocidad instantánea; señalar huecos y ausencia de fuentes por tarea. Distinguir generaciones del hijo e IDs reutilizados.
- [x] Extender las curvas existentes al detalle de las tareas observadas. Mostrar tokens procesados/cacheados, progreso cuando exista denominador válido, tiempo de fase, tokens de salida y tasa con su fuente y ventana.
- [x] Diferenciar cola, prompt, generación, finalización y fallo cuando estén observados; preservar cifras desconocidas/no disponibles y su motivo, sin deducir inactividad de silencio de logs.
- [x] Mostrar tareas simultáneas y finalizadas, con scroll, selección estable y último valor observado. Aplicar al histórico visual el límite configurable de finalizadas del modelo de 065, conservando visibles las activas.
- [x] Adaptar columnas y gráficos al ancho/alto sin perder servicio, entrada, fase ni progreso disponible. Ampliar logs o pausar visualización no detiene la captura de actividad.
- [x] Acreditar con peticiones reales de prueba la transición prompt→generación→fin, dos presets/entradas y cambio de instancia; contrastar cifras con la fuente. No basta animar fixtures.

- [x] Acreditar el ciclo de experimentación con YAML: observar peticiones, retirar con `down`, editar la receta, aplicar con `up` y observar nuevas peticiones. Probar tanto cerrar y reabrir el panel como mantenerlo abierto mientras los comandos se ejecutan desde otra terminal. Reconciliar las entradas y separar generaciones e instancias sin arrastrar tasas entre intentos. La evaluación de los intentos la hace el usuario a partir de las cifras del monitor; esta tarjeta no entrega un comparador de ejecuciones o configuraciones.
- [x] Dar acceso a tiempos, promedio y pico observado/máximo de medias sin sobrecargar vista compacta. No representar máximo de tokens como longitud final ni prometer mejora por usar borrador.
- [x] Probar varias peticiones reales y cambio del hijo manteniendo log/hardware usables; no implementar un sistema formal de benchmarks por esta tarjeta.

## Dependencias

- [065: Correlacionar actividad de prompts y generación por entrada, slot y tarea](../065-m3-actividad-tareas/README.md)
- [066: Abrir la interfaz de terminal y navegar entre servicios conocidos](../066-m3-tui-base/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Sin visor del texto de conversaciones, botones para cargar modelos ni promesa de granularidad que el build no exponga. Los límites de fuente se muestran y se documentan para revisión.

## Estado y evidencia

Implementación aprobada por el usuario tal como está el 2026-11-07. Se acepta
la presentación actual porque aporta la información necesaria. El rediseño
visual se realizará cuando estén todos los elementos en pantalla; no bloquea
el cierre de esta tarjeta. El usuario autorizó un único commit conjunto de
todos los archivos modificados. La evidencia técnica se registra a continuación.
Los datos públicos están anonimizados: rutas reales, salidas de terminal y
recursos concretos del entorno de prueba permanecen fuera de Git.

### Comportamiento implementado

- Resumen por entrada encima de las tareas y del log: estado, actividad y cola,
  medias del backend y acumulados de prompt, caché y generación. Enter abre el
  detalle desplazable con todas las métricas reconocidas, fuente, unidad,
  alcance y fecha. El período del backend se conserva como no informado;
  no se inventa un total de tasas del servicio.
- Lista de tareas activas primero y recientes después, con selección por
  identidad completa. Enter abre la última instantánea observada: modelo,
  servicio, instancia, generación del hijo, slot/tarea, tokens, caché, contexto
  real del slot, bandera de especulación y límite de predicción informado.
  Las filas cerradas distinguen salida del slot, pérdida de observación y
  reemplazo del hijo; el resultado de la petición permanece sin confirmar.
- Cada tarea conserva hasta 24 columnas UTC de dos segundos. Cambiar de tarea
  recupera su propia curva, sin heredar tasas ni picos. El límite de histórico
  de 065 retira conjuntamente filas y series finalizadas, conservando activas.
  El detalle explica la fórmula de tasas, medias, picos y tiempos medibles.
- Se conservan las métricas reconocidas de llamadas de decode, ocupación media,
  máximo de contexto y especulación: propuestos, aceptados, verificaciones y
  aceptados por posición. Las posiciones mantienen identidades distintas.
  Ausente y cero se distinguen; no se promete una mejora de rendimiento.
- La generación del hijo se verifica antes y después de consultar métricas y
  slots. Un cambio durante la consulta descarta la muestra mezclada. El alcance
  de las métricas incluye PID y creación del hijo cuando se pueden verificar.
- Ampliar concentra el espacio en logs; Monitor vuelve a las tareas y curvas.
  Pausar congela la presentación de actividad y logs; los observadores continúan
  capturando y el hardware permanece visible. Reanudar muestra el estado vigente.
  Consultar detalles tampoco suspende la captura. Esc vuelve o cierra el panel.
- El ciclo de experimentación utiliza las operaciones existentes de publicación:
  editar el YAML, retirar y reaplicar desde otra terminal no requiere cerrar el
  panel. Una apertura nueva inicia otra sesión de observación. No hay comparador
  de intentos ni persistencia de series entre aperturas.

### Validación ejecutada

- Compilación Release y publicación Native AOT win-x64 del producto y del runner,
  sin paquetes nuevos, con warnings de IL/trimming tratados como errores.
- 51 comprobaciones del dashboard: concurrencia, identidades reutilizadas,
  retención conjunta de filas/curvas, selección, generación nueva sin herencia,
  presentación compacta, límites de tasas y parser de especulación por posición.
  Incluyen sustitución del hijo durante /metrics: la muestra se descarta y
  las consultas siguen siendo GET sin reload ni autoload.
- 68 comprobaciones Native AOT de observación, incluidas consultas del producto
  publicado y mediciones reales de hardware/procesos en Windows.
- 14 comprobaciones del ensayo real Native AOT con el build b12058-3f8af15a7
  y stories15M-q4_0.gguf local: tres
  tareas concurrentes en dos presets, tasas independientes, cancelación desde
  el cliente, últimos valores observados y metadatos de modelo/contexto.
  Se ejercitaron up, down, edición de YAML y up, sustitución del hijo y del
  router en el mismo observador. El reemplazo del router usa otro puerto y
  otro log exclusivos del fixture.
- El stream de una petición controlada confirmó prompt, generación y fin.
  Sus 64 tokens coincidieron con el incremento del contador de generación de
  la entrada. Ese stream es evidencia del cliente de prueba; no se utiliza
  para atribuir respuestas ni resultados de otros clientes al monitor.
- Ejecución interactiva del producto Native AOT en PTY 80x24 y 120x32. Se
  observaron dos entradas reales, curvas y tasas por tarea, sustitución de
  tareas al reaplicar y últimos valores tras finalizar. Se probaron detalle
  con Enter, regreso con Esc, ampliación de logs, pausa/reanudación y cierre.
  Las salidas comprobadas devolvieron 0 y restauraron terminal y cursor.

### Incidencias y límites para revisión

- Tres intentos iniciales del runner Native AOT terminaron con excepciones no
  capturadas por acceso a carpetas de fixtures y consulta de salud dentro del
  aislamiento. El usuario observó tres avisos de Windows de saturación de búfer.
  La correlación con esos fallos está registrada; no se dispone de un dump que
  pruebe una corrupción de memoria ni su causa nativa exacta. El runner ahora
  conserva diagnóstico y código 1 mediante manejo explícito de errores en estos
  recorridos. Una prueba con recurso ausente acreditó esa salida controlada.
  El servicio aislado se limpia también si su arranque falla. Las baterías
  finales se ejecutaron con acceso permitido a procesos y HTTP local.
- Los prompts del GGUF pequeño terminan demasiado rápido para acreditar una
  curva P visible al refresco de dos segundos. Sus fases se contrastaron con
  el stream propio; el monitor conserva n/d si no observó intervalos medibles.
  No se acredita porcentaje global, ETA, TTFT ni resultado de peticiones ajenas.
- El contexto real del slot de ese modelo fue menor que el configurado. Se
  muestra el valor de /slots; no se sustituye por el argumento solicitado.
- La inferencia real no activó especulación. Los ceros y bandera desactivada
  son datos observados; la separación de posiciones positivas se prueba con
  fixtures. No se acredita una comparación de rendimiento ni calidad de salida.
- La evidencia es Windows y PTY, con dos tamaños iniciales. No acredita resize
  dinámico en una terminal de escritorio, otros builds/forks, Linux ni macOS.
  Se mantienen el presupuesto HTTP de cinco segundos y los límites previos
  de catálogo/slots; no se realizó un ensayo de alta concurrencia.
- Se verificaron 49 enlaces locales de los documentos modificados y
  `git diff --check` no informó errores. No se modificó la herramienta global
  instalada. El commit conjunto de cierre fue autorizado por el usuario.

### Reproducción

Definir $TestRoot como una carpeta de pruebas local en D:, $BackendExe como el
build acreditado y $SmallModel como el GGUF local. Cada ensayo de inferencia
requiere una carpeta nueva y una home aislada. No hay descargas implícitas.

```powershell
dotnet publish src/Coquena -c Release -r win-x64 --no-restore -o "$TestRoot/product" -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false
dotnet publish tests/Coquena.Tests -c Release -r win-x64 --no-restore -p:PublishAot=true -o "$TestRoot/tests" -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false
& "$TestRoot/tests/Coquena.Tests.exe" --dashboard-tests
& "$TestRoot/tests/Coquena.Tests.exe" --observation-tests "$TestRoot/product/coquena.exe"
$env:COQUENA_HOME = "$TestRoot/home"
$env:COQUENA_FIXTURE_TOKENS = '20000'
& "$TestRoot/tests/Coquena.Tests.exe" --inference-router-tests $BackendExe $SmallModel "$TestRoot/inference-new"
```

Mientras se ejecuta ese último ensayo, abrir otra terminal con la misma home,
situarse en inference-new y ejecutar el producto publicado con `tui`.
El runner utiliza el recorrido CLI compartido con una interacción de prueba
que acepta únicamente las operaciones sobre su propio fixture.

Referencia de campos: [README del build contrastado](https://github.com/ggml-org/llama.cpp/blob/3f8af15a7/tools/server/README.md#available-metrics).
