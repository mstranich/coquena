---
status: complete
created: 2026-10-21
priority: high
tags:
- tui
- dashboard
- m4
depends_on:
- 055-m3-publicacion-presets
- 067-m3-tui-hardware-graficos
- 068-m3-tui-prompts-tokens
- 069-m3-tui-logs
created_at: 2026-10-21T23:26:07.035047300Z
updated_at: 2026-11-09T11:44:13.389781500Z
completed_at: 2026-11-09T11:44:13.389781500Z
transitions:
- status: in-progress
  at: 2026-11-09T10:58:31.286889100Z
- status: complete
  at: 2026-11-09T11:44:13.389781500Z
---
# Integrar y acreditar el dashboard Servicios completo

## Objetivo

Entregar una pantalla semigráfica de terminal utilizable para vigilar el servidor y sus presets durante inferencia real.

El recorrido incluye abrir `coquena tui` desde una carpeta con receta: una pestaña
local identificada por `name` o por la carpeta ofrece Up/Down y, si declara servicio
propio, los controles de ciclo, monitor y logs. No requiere un inicio/publicación
previo por CLI ni un directorio `.coquena/` preexistente. Se conserva la semántica
de publicación de CLI, incluido target global/self y retirada de destinos registrados.

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

## Base entregada en 066

La [066](../066-m3-tui-base/README.md#estado-y-evidencia) entrega un monitor
ejecutable en Native AOT y registra una prueba controlada con un cliente
externo. Esa evidencia sirve de referencia para repetir el recorrido con
las ampliaciones, varios presets y servicios propios y compartidos.

## Trabajo y aceptación

- [x] Abrir una receta nueva con y sin `name`, con y sin servicio propio, sin estado previo: pestaña local visible, nombre correcto y acciones correspondientes. Abrir/cerrar no inicia ni publica.
- [ ] Iniciar el servicio declarado desde la pestaña local, consultar sus logs y publicar/retirar la receta desde Up/Down. Verificar destino, confirmaciones, estado observado y conservación de contribuciones ajenas.
- [ ] Verificar la integración de actividad, series temporales, hardware compartido y logs. Contrastar orden de pestañas y acceso a Logs dentro del servicio al reducir la altura.
- [ ] Partir de un proyecto con target self y varios presets; iniciar/publicar mediante CLI y abrir tui. Enviar peticiones desde un cliente de prueba y observar las fases y cifras disponibles mientras ocurre inferencia.
- [ ] Repetir con servicio compartido por consumidores, dos entradas de modelos distintos usando recursos acordados y actividad simultánea cuando el backend lo soporte; verificar correlación y ausencia de métricas mezcladas.
- [ ] Probar cambio de preset/entrada observado, recarga de publicación, reinicio del servidor, reconexión y telemetría parcial; mantener información vigente y distinguir incertidumbre.
- [ ] Comprobar resize, teclado, foco, scroll, falta de color, terminal pequeña, buffers acotados y aislamiento de errores; mostrar identidad, fase/progreso y cifras aun al simplificar gráficos.
- [ ] Registrar evidencia visual de la aplicación Native AOT en terminal real, pasos reproducibles y comparación con fuentes. Capturas estáticas o dashboard sin actividad no acreditan el recorrido.
- [x] Comprobar que cerrar la TUI deja el servicio atendiendo y no modifica intención ni publicación.
- [x] Acreditar con un backend real y recursos locales acordados el arranque y el reinicio desde los controles ya entregados en 066. Verificar el estado observado, la confirmación de reinicio y el regreso al monitor tras la operación.

## Integración ampliada y milestone abierto

El primer monitor se acredita en 066. Aquí se contrastan las ampliaciones y los
escenarios compartidos; no se posterga hasta esta tarjeta la integración básica,
las peticiones reales ni la revisión de UX. Completar 070 no cierra M4, que sigue
abierto a nuevas tarjetas acordadas una por una.

- [ ] Repetir matriz de 066 tras ampliaciones: fases, avance conocido, consumo, curvas, promedio/pico y logs siguen accesibles durante inferencia real.
- [ ] Contrastar presets con mismos pesos y distinta configuración, con/sin especulación cuando sea viable, y modelos diferentes. Identificar caché, ventanas y concurrencia que impiden comparaciones directas.
- [ ] Incluir una petición externa además de pruebas propias: el stream del cliente propio no demuestra telemetría universal.
- [ ] Revisar con el usuario jerarquía, foco estable, datos desconocidos, resize y vuelta desde histórico/logs; registrar hallazgos de UX y técnicos.
- [ ] Reportar cobertura por fuente/build y consecuencias para contratos reutilizables en Web UI posterior, sin implementar web ni exigir secciones aún no acordadas.

## Dependencias

- [055: Publicar y retirar presets mediante el INI del servicio target](../055-m3-publicacion-presets/README.md)
- [067: Mostrar hardware y gráficas temporales legibles en la terminal](../067-m3-tui-hardware-graficos/README.md)
- [068: Visualizar prompts, generación de tokens y tareas de cada preset](../068-m3-tui-prompts-tokens/README.md)
- [069: Integrar logs continuos con selección de servicio y navegación](../069-m3-tui-logs/README.md)

## Validación prevista

Ejecutar pruebas pertinentes de los escenarios anteriores y registrar resultados reproducibles. Las entregas de runtime requieren publicar Native AOT win-x64 y ejecutar el binario publicado, con estado y recursos de prueba en D:. Evaluar dependencias en ese camino antes de adoptarlas. Las tarjetas visuales requieren uso en terminal real; mocks y snapshots aislados no acreditan integración. Utilizar recursos locales o fixtures, sin descargas costosas implícitas.

## Límites

Dashboard y operación de la receta local en M4. Los editores, la experimentación
asistida y la Web UI tienen recorridos propios dentro del milestone.

## Estado y evidencia

Cerrada por aprobación explícita del usuario de la implementación tal como está,
el 2026-11-09. Se conserva la evidencia ejecutada y las casillas sin acreditar;
el cierre no convierte esos criterios en comprobaciones realizadas ni cierra M4.

### Validación preliminar — 2026-11-09

Entorno Windows anonimizado, publicaciones y estado aislados en D:, sin descargas
de recursos, cambios al producto ni modificación de servicios habituales.

- Producto y runner publicados Native AOT win-x64 con advertencias de IL/trimming
  tratadas como errores. Se ejecutó el producto publicado con `--version`.
  El runner requirió restaurar el target win-x64 desde la caché usando una fuente
  local vacía; después se publicó sin restore.
- Runner nativo: 51 comprobaciones de dashboard, 68 de observación, 71 de
  seguimiento de logs y 32 de logs/hooks; todas finalizaron correctamente.
- Fixture interactivo Native AOT sin color en PTY: servicios globales antes del
  propio, hardware simulado visible, proceso vivo distinguido de API de catálogo
  no disponible, cifras antiguas señaladas y salida con Esc y código 0.
  Se registraron 15 capturas, máximo de una simultánea y una captura lenta de
  aproximadamente seis segundos. No es evidencia de inferencia ni de resize de
  escritorio; tampoco es una medición de rendimiento generalizable.
- En esta etapa preliminar no se alteraron las casillas de aceptación: faltaba ejecutar la matriz con recursos
  locales acordados, incluido el segundo modelo, los controles de inicio/reinicio
  sobre backend real y la revisión visual con el usuario.

### Aclaración de alcance — 2026-11-09

El usuario corrigió la interpretación centrada en una matriz de recursos: el
recorrido debe empezar en la pestaña del proyecto que contiene la receta y permitir
iniciar su servicio, publicar/retirar sus presets y leer sus logs. Se incorporó
esa aceptación explícita; la consulta inicial de recursos no bloquea este trabajo.
Las verificaciones preliminares anteriores se conservan como regresión y no
acreditan por sí solas este recorrido.

### Integración de la receta local y validación — 2026-11-09

- La TUI descubre `coquena.yaml` sin exigir estado previo y selecciona la pestaña
  local. Usa `name` o la carpeta; la receta consumidora conserva su pestaña sin
  inventar un servicio. El primer inicio explícito registra el servicio declarado.
- Up/Down comparten publicación, preparación y confirmaciones con la CLI. El
  frontend aporta diálogos, selección de recursos, arranque y cancelación;
  no lee decisiones mediante la consola. El destino global/self permanece separado
  del servicio propio. La opción de preparación privada aplica `--private`
  explícitamente para permitir referencias externas según la política existente.
- Una receta editada o inválida mantiene la pestaña y permite Down sobre los
  destinos registrados. Se releen nombre, destino e identidad propia durante la
  sesión; un cambio de identidad separa también la sesión de logs.
- Producto y runner publicados Native AOT win-x64 sin advertencias ni errores.
  Suites nativas: dashboard 63, publicación 50, observación 68, seguimiento de
  logs 71 y servicios por ámbito 39. Las 63 incluyen descubrimiento sin estado,
  consumidor sin servicio, nombre por carpeta, orden global/local, cancelación,
  arranque delegado y retirada con YAML inválido.
- Producto Native AOT en PTY 80×24 sin color: receta consumidora con nombre y
  receta propia sin nombre, ambas sin `.coquena/`. La consulta no creó estado.
  Iniciar desde la segunda preparó el servicio fixture y respondió HTTP 200;
  sus logs quedaron accesibles. El fixture no expone telemetría de inferencia.
- Backend real local b12058 / 3f8af15a7 y stories15M-q4_0.gguf, sin descargas:
  arranque desde Iniciar, publicación `news/summary` desde Up, logs del servicio,
  petición sintética externa y retirada desde Down. El catálogo remoto confirmó
  el alta y la baja; Down dejó health 200. Cancelar el reinicio conservó la
  instancia y confirmarlo por teclado creó otra; se regresó al monitor desde Logs.
  También se ejercitó Up con el destino detenido: el diálogo identificó nombre y
  ámbito, la aceptación inició el backend y completó la publicación. Repetir Up
  por CLI confirmó que ya no había cambios pendientes.
  Al cerrar con Esc, salida 0 y restauración de terminal; salud, identidad,
  publicación y hash de la receta se conservaron, tanto con catálogo vacío como
  con `news/summary` publicado. Después se detuvo explícitamente
  cada servicio de prueba.
- La inferencia fue breve: no acredita una curva sostenida, porcentaje, ETA ni
  contraste entre modelos distintos. El backend externo del ensayo declara
  explícitamente su INI operativo; no se amplía la detección de capacidades de
  otros builds o forks.
- Se comprobaron 42 enlaces locales, 313 rutas del mapa y `git diff --check`.
  No cambiaron diagramas PlantUML ni ejemplos JSON documentales.

### Reproducción del ensayo local

Definir `$TestRoot` en D:, `$BackendExe` como el build local acreditado y
`$SmallModel` como el GGUF local. `$TestRoot/news` debe ser nuevo. El setup sólo
escribe la receta y su identidad para declarar el INI externo; no inicia procesos.
El caso sin estado previo se acredita por separado en las pruebas de dashboard
y mediante el fixture de servicio descrito arriba.

~~~powershell
dotnet publish src/Coquena -c Release -r win-x64 --no-restore -o "$TestRoot/product" -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false
dotnet publish tests/Coquena.Tests -c Release -r win-x64 --no-restore -p:PublishAot=true -o "$TestRoot/tests" -p:IlcTreatWarningsAsErrors=true -p:TrimmerSingleWarn=false
& "$TestRoot/tests/Coquena.Tests.exe" --dashboard-tests
& "$TestRoot/tests/Coquena.Tests.exe" --publication-tests
& "$TestRoot/tests/Coquena.Tests.exe" --dashboard-project-setup "$TestRoot/news" $BackendExe $SmallModel
$env:COQUENA_HOME = "$TestRoot/home"
Set-Location "$TestRoot/news"
& "$TestRoot/product/coquena.exe" tui
~~~

Elegir Preparación privada para esas rutas externas. Recorrer Iniciar, Up, Logs,
Down y Reiniciar; los diálogos identifican destino y resultado. Cerrar la TUI no
detiene el backend. Al terminar las comprobaciones, detener explícitamente el
servicio aislado con `service self stop` desde esa carpeta y la misma home.

### Estado de revisión y límites

La documentación funcional incorpora la pestaña local y sus controles por
aclaración explícita del usuario. Concreta la opción privada mediante una regla
de persistencia existente. También corrige una descripción desactualizada de
ocupación desconocida: el documento responsable de servidores exige detener la
publicación cuando no puede comprobarse actividad; la TUI reutiliza esa operación.

El usuario aprobó la implementación tal como está y autorizó cerrar la tarjeta
y realizar un único commit con todos los archivos juntos. El contraste ampliado
con dos modelos/consumidores y el resize dinámico de escritorio permanecen sin
acreditar. La aprobación no añade evidencia visual ni pruebas de esos puntos u
otras plataformas a las ejecuciones por PTY registradas.
La fila de preparación privada ocupa una línea adicional que debe evaluarse en
esa revisión de altura y jerarquía. La selección modal de múltiples recursos usa
ahora `Dialog.Result`, como el selector de logs; ese caso interactivo no se
ejercitó con variantes remotas. Sin paquetes nuevos ni actualización de la
herramienta global. Entrega conjunta en un único commit autorizado, sin push.
