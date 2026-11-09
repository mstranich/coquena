# M4 — Experiencia completa en Terminal UI

**Estado:** abierto, con entregas incrementales y refinamiento de UX. La primera entrega de TUI se cerró en [066](../../specs/066-m3-tui-base/README.md), donde consta la evidencia disponible y sus límites. **Depende de:** [M3](M3-definiciones-declarativas.md).

## Objetivo

Desarrollar una Terminal UI con buena experiencia de usuario sobre recetas, servicios y consumidores de M3, comenzando por observar inferencia real y ampliando secciones de forma incremental. La Web UI queda para después de M4, porque la TUI es la interfaz prioritaria y permite operar en un VPS. La TUI usa
los mismos contratos y capacidades de la CLI; no crean catálogos ni ciclos de
ejecución propios. No es una segunda reescritura del producto.

El soporte completo de proyectos con YAML pertenece a M3; M4 no se utiliza para
posponer partes de ese ciclo necesarias para que el proyecto funcione mediante CLI.

## Forma de trabajo y evolución

M4 permanece abierto. Se agregan, corrigen y refinan tarjetas una por una según
el uso y revisión de UX. Las tarjetas actuales no son un inventario exhaustivo
ni su finalización cierra automáticamente el milestone. Cada entrega conserva
alcance concreto; nuevas funciones requieren acuerdo, sin ampliar por iniciativa
propia el trabajo autorizado.

La primera experiencia debe permitir entender una respuesta mientras se genera:
fase, avance disponible, consumo, curvas de tokens, promedio, pico observado y
logs. Los wireframes de docs/design/ son aproximaciones de intención que orientan
jerarquía y navegación, no diseños completos o inmutables. La revisión de UX es
parte de la aceptación junto a las pruebas técnicas.

La TUI, sus contratos reutilizables y lo aprendido de UX serán base de Web UI en
otra iteración. No se implementa web ahora ni se supone traslado literal de layout.

## Horizonte de experiencia

- Entregar el dashboard Servicios en Terminal UI y ampliar la experiencia con acciones de control.
- Experiencia avanzada de edición en TUI: editores/formularios de proyecto, presets y argumentos, aprovechando schemas
  cuando existan y conservando edición libre donde corresponda.
- Borradores recuperables por sesión, conflictos con cambios externos, guardado
  explícito y creación de overrides de diferencias; separación entre archivos
  compartidos y estado local ignorado por Git.
- Selección de YAML y comparación visual de configuración elegida frente a la
  realmente activa, sin cambios implícitos sobre el servidor.
- Flujo visual de exploración de modelos, lectura de información y descarga,
  creación/prueba de presets en un router vivo y persistencia de la receta útil.
  Incluye el alta/matching de archivos locales sobre el soporte de proyecto de M3;
  no difiere a esta entrega la capacidad del núcleo para reutilizarlos.
- Paridad de acciones de instancia y recuperación, observación por eventos y
  reconexión; cerrar interfaces no detiene servidores desacoplados.

## Navegación entre catálogos

Acreditar el recorrido desde un proyecto recién reconocido a otro proyecto
conocido y al catálogo global. Iniciar una transferencia en el primero,
cambiar de espacio y comprobar que se conserva. Probar entradas de ambos
proyectos en el mismo servidor global y luego operar explícitamente un
servidor propio con otro backend. Verificar que cambiar de catálogo conserva
el ámbito de recursos y que el monitor identifica qué servidor se observa.
Comprobar que la barra de hardware sigue visible al alternar entre catálogos,
monitor, logs y herramientas, y que se compacta con el ancho de la terminal.

## Dashboard Servicios

El recorrido empieza con los servicios globales y la pestaña de la receta local,
incluido un proyecto todavía sin estado preparado o sin servicio propio.
La pestaña local ofrece Up/Down y, cuando declara servicio, sus controles y logs.
La 070 integra ese recorrido con la observación. En cada servidor se sigue el avance de varias entradas con
gráficos arriba y logs abajo, o desde Logs dentro del servidor si falta altura.
La 066 evalúa Terminal.Gui v2 en Native AOT antes de fijar la dependencia.

Los [wireframes de Presets, Buscar modelos y Configuración](../design/interfaces-visuales.md#dashboards-de-observación)
orientan las herramientas al final de la navegación. Sus recorridos se
descomponen al acordar las entregas de edición, exploración y preferencias.
La aceptación de experimentación conecta preparación del preset, publicación
en caliente, inferencia de prueba, observación de la entrada y decisión de
ajustar, guardar YAML o retirar la prueba.
En 068 se acredita el ciclo mediante edición externa de YAML y comandos
`down`/`up`, con el panel abierto o reabierto entre intentos. No incluye un
comparador de ejecuciones o configuraciones dentro del monitor.

La secuencia comienza por 066. Los IDs y directorios históricos no fijan orden.

| Orden | Tarjeta | Entrega | Dependencias de ejecución |
| --- | --- | --- | --- |
| 1 | [066](../../specs/066-m3-tui-base/README.md) | Primer monitor real: fases, avance, curvas, promedio/pico, consumo y logs | 044, 052, 062, 063, 064 |
| 2 | [065](../../specs/065-m3-actividad-tareas/README.md) | Profundizar correlación y robustez de tareas sobre el monitor | 052, 064, 066 |
| 3 | [067](../../specs/067-m3-tui-hardware-graficos/README.md) | Ampliar hardware por dispositivo y legibilidad de gráficas | 042, 063, 066 |
| 4 | [068](../../specs/068-m3-tui-prompts-tokens/README.md) | Ampliar detalle, concurrencia e histórico de inferencias | 065, 066 |
| 5 | [069](../../specs/069-m3-tui-logs/README.md) | Completar históricos y endurecer continuidad/navegación de logs | 062, 066 |
| 6 | [070](../../specs/070-m3-dashboard-integrado/README.md) | Integrar pestaña de receta local, controles de ciclo/publicación y dashboard ampliado | 055, 067, 068, 069 |

Las dependencias de ejecución de la tabla incluyen la reorganización acordada.
El adaptador local rechaza `leanspec rel add`: las nuevas relaciones 066→062/063/064
y 065→066 están declaradas en el cuerpo de las tarjetas, pero no en sus metadatos.
No interpretar el grafo actual como el orden completo; reconciliarlo mediante CLI
cuando el comando esté disponible, sin modificar manualmente frontmatter.

La 066 contiene intención, matriz de fuentes, semántica de avance/promedio/pico,
wireframes y aceptación detallados para futuras sesiones. Evalúa la dependencia
visual con publicación/ejecución Native AOT win-x64 antes de adoptarla. Reutiliza
062–064 e implementa la observación mínima requerida sin depender de las
ampliaciones 065/067/068/069. La 070 contrasta el dashboard ampliado en terminal
real con varios presets, consumidores y peticiones; no es la primera integración
ni determina el cierre de M4.

## Cobertura restante y entregas

M4 desarrolla y refina el alcance de TUI acordado sobre M3. La planificación de
tarjetas debe mantener trazabilidad de las funcionalidades para que no se pierdan
al dividir el trabajo; una pantalla no acredita por sí sola un flujo completo.

El benchmarking local de rendimiento y consumo forma parte de la visión acordada.
Al descomponer M3/M4 se ubicará su primera entrega; su entrega se acordará en tarjetas concretas sin condicionar el primer monitor a una plataforma formal de benchmarking.
No se impone como condición de aceptación de M3 por este documento. Se reutiliza
el procedimiento de peticiones/tokens/timings estudiado, sin exigir /metrics para
tasas básicas. RAM/VRAM requiere instrumentación comprobada y compatible con
Native AOT; no se exige concurrencia de gran escala en la primera aproximación.

La calculadora estimativa de recursos permanece como posibilidad futura, no como
requisito automático de M4. Navegación entre proyectos tipo IDE no debe convertirse
en un IDE completo. «Completar las features» significa el alcance acordado y
descompuesto, no incorporar indefinidamente cualquier idea futura.

## Acciones de terminal a partir de la CLI

El mapa usa la [superficie canónica de comandos](../design/comandos.md). Ubica las
acciones por contexto para refinar pantallas y tarjetas de M4.

| Pantalla o menú | Controles visuales | Operaciones CLI |
| --- | --- | --- |
| Servidor · Monitor | Estado en vivo, definición, iniciar/detener/reiniciar según estado | `ps --watch`, `service show/start/stop/restart` |
| Servidor · Acciones | Crear global, designar default, dar de baja servicio detenido | `service create`, `service default set`, `service remove` |
| Servidor · Logs | Seguir, elegir histórico, abrir registro; pausa y scroll locales | `logs follow/list/show` |
| Presets · Editor | Seleccionar, inspeccionar, asignar literal o vínculo, quitar override, clonar y eliminar definición | `preset list/show/set/bind/unset/clone/remove` |
| Presets · Prueba | Publicar/retirar preset seleccionado; publicar/retirar contribución de receta desde acciones de proyecto | `preset apply/withdraw`, `up/down` |
| Buscar modelos · Inventario | Ver, agregar archivo local, asignar referencia, descargar y proteger o retirar instalación, con selector modelo/companion | `model [companion] list/show/add/set/download/protect/unprotect/remove` |
| Buscar modelos · Transferencias | Ver progreso, pausar, reanudar y cancelar operación seleccionada | `download list/show/pause/resume/cancel` |
| Proyecto · Definición | Seleccionar backend y editar definiciones YAML; consultar el impacto del restablecimiento global y confirmarlo | `backend show/set`, `param list/show/define/set/unset/remove/reset` |
| Proyecto · Acciones | Inicializar, importar al global con selección explícita, listar u olvidar combinación | `init`, `import`, `combination list/forget` |
| Proyecto o global · Mantenimiento | Inventario/preparación/protección de backends; consultar novedades, preparar actualización, verificar y previsualizar limpieza | `backend list/download/protect/unprotect/remove`, `update`, `upgrade`, `verify`, `clean` |
| Configuración | Recargar valores efectivos, guardar y restablecer cada grupo | `config observer-retry show/set/unset`, `config log-retention show/set/unset` |

El monitor y los logs conservan la aceptación de consulta de las tarjetas
066–070. Los controles de ejecución, edición y mantenimiento del mapa se
descomponen con sus respectivos recorridos. Los alias de CLI comparten control.
Para validar preferencias, comprobar lectura cruzada CLI/TUI, asignación y
restablecimiento repetidos, valores inválidos y override de retención por
servicio. El tamaño del histórico visual queda como preferencia del observador
por concretar; el mapa no le atribuye un comando existente.

## Preparación y aceptación

- Concretar flujos y diseño visual revisables sobre contratos reconciliados,
  sin otro cuestionario abierto para detalles rutinarios.
- Verificar búsqueda/descarga, edición, prueba, guardado y reproducción como
  recorridos completos, incluyendo fallos y recuperación.
- Mantener el servidor desacoplado y diferenciar estado observado, borradores,
  intención guardada y composición aplicada.
- Comprobar dependencias visuales y telemetría en el camino Native AOT antes de
  adoptarlas, con publicación y ejecución para los destinos acordados.
- No presumir métricas, eventos o recarga en cualquier fork.
- Crear tarjetas al acordar entregas; este horizonte no crea tareas ni autoriza
  paquetes, código o commits por sí mismo.

La edición asistida no es una dependencia de M3. M3 permite usar un editor externo y validar/componer YAML, además de comandos puntuales que persisten asignaciones. M4 utiliza esos contratos para edición visual, sin exigir construir un editor de texto generalista ni un IDE completo.

La prioridad de edición es combinar servidor, modelos y presets en TUI.
El buscador y los formularios de argumentos/parámetros se ordenan después de ese
recorrido. El catálogo y la validación declarativa ya existen como contrato de M3;
la experiencia avanzada de autoría de parámetros no bloquea su aceptación y su
inclusión concreta en M4 se decide al descomponer esa experiencia.
