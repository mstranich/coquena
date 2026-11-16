---
status: complete
created: 2026-11-12
tags:
- m5
- tui
- cli
created_at: 2026-11-12T03:19:48.954534500Z
updated_at: 2026-11-16T13:09:23.384456100Z
completed_at: 2026-11-16T13:09:23.384456100Z
transitions:
- status: planned
  at: 2026-11-14T19:01:06.668758200Z
- status: in-progress
  at: 2026-11-14T19:01:06.892874200Z
- status: complete
  at: 2026-11-16T13:09:23.384456100Z
---
# Probar un preset con un chat

## Objetivo

Ofrecer un laboratorio rápido para enviar mensajes a un preset y observar su
comportamiento con las métricas de En vivo reorganizadas alrededor de la prueba.
El caso principal es una comprobación con unos pocos prompts simples para ver
respuesta y telemetría, no una conversación larga ni un arnés o agente. No se
requiere limitar artificialmente el número de mensajes.
Entrega de
[M5](../../docs/milestones/M5-rendimiento.md).

## Alcance propuesto

- Elegir preset desde el árbol servicio → proyecto → preset, como en En vivo.
  El modelo es información del preset; no se selecciona un GGUF independiente.
- Mostrar conversación, entrada de mensajes y métricas mientras transcurre la
  respuesta, según el [contrato funcional](../../docs/design/benchmarking.md#laboratorio-interactivo).
- Reutilizar fases, tokens, tasas, curvas, recursos y diagnóstico de En vivo con
  sus alcances, separando petición, entrada, servicio y hardware compartido.
- Priorizar tiempos y tasas de prompt/generación, reutilización de caché de prompt
  y aceptación especulativa disponible. Acreditar fuente, denominador y período;
  no confundir caché KV con caché MoE ni acumulados con cifras de la petición.
- Disponible desde la TUI y desde la CLI.
- Vista Chat en la TUI, con acceso rápido desde el botón Chat de
  En vivo para el preset enfocado.
- `chat <preset> [--service <nombre|default|self> | --global] [--load]` para la prueba rápida por CLI;
  la referencia usa el nombre publicado completo del preset dentro del servicio.
- Sesión CLI interactiva con `/exit` o Ctrl+C para salir. Línea de telemetría
  permanente debajo del área de escritura, actualizada sin alterar texto ni cursor.

## Decisiones confirmadas

- Chat de prueba por sesión, con contexto mientras está abierta y sin historial
  persistente. No guardar el diálogo como resultado de benchmark.
- Usar una entrada publicada sin iniciar servicios o publicar implícitamente.
- Mostrar métricas disponibles con fuente y alcance; no evaluar calidad automáticamente.
- La política de sesión no cambia la retención de logs del backend.

## Decisiones de interacción tomadas

- Pantalla CH (pestaña Herramientas, categoría única Chat) según la
  [vista previa](../../docs/design/tui/vista-previa.html) y su wireframe.
- Contrato: `/v1/chat/completions` de llama-server con `stream`, `timings_per_token`
  y `return_progress`; métricas de la petición desde `timings` y `prompt_progress` de
  la propia respuesta. Sin parámetros de generación: rigen los del preset.
- Enviar pasa a Detener durante la respuesta; cambiar de destino o Nueva sesión
  vacía la conversación.
- Servicio detenido: modal (TUI) o pregunta (CLI) antes de iniciarlo.
- CLI con entrada o salida redirigidas: se rechaza.

## Decisiones de interacción abiertas

- Límites de contexto y su presentación cuando la conversación excede el contexto del slot.
- Contrato de correlación entre petición y tarea observada; sin evidencia, mostrar
  actividad de la entrada como contexto, sin adjudicarla al mensaje enviado.
- Campos y compactación de la línea de telemetría; comprobar edición con texto
  largo, redimensionado, ausencia de métricas y recepción de respuesta.

## Fundamento de observabilidad

El [estudio](../../docs/milestones/M5-estudio-rendimiento.md) distingue esta prueba
de los tokens sintéticos de llama-bench y de una carga HTTP controlada. El cliente
conoce envío y respuesta, pero los observadores actuales no acreditan por sí solos
el vínculo con su slot. No trasladar conteo de fragmentos SSE como tokens.

## Criterios de aceptación (borrador)

- [x] Vista previa TUI revisada antes de implementar.
- [ ] Conversación real con un preset publicado.
- [ ] Recorrido rápido con pocos prompts permite seguir procesamiento, generación,
  caché y especulación disponibles; las métricas ausentes se identifican sin inventarlas.
- [ ] Botón Chat de En vivo abre Chat con el mismo servicio/preset.
- [x] Invocación CLI selecciona el mismo destino con `chat proyecto/clave`, la clave
  local o `servicio/proyecto/clave`; el servicio sale del destino de la receta o de `--service`.
- [ ] CLI interactiva: telemetría debajo de la entrada durante edición y respuesta,
  sin perder texto/cursor ni imprimir una nueva línea por cada refresco.
- [ ] `/exit` y Ctrl+C salen desde edición y espera de respuesta; se restaura
  el terminal y se cancela la petición propia sin detener el servicio.
- [x] No presentar desconexión del cliente como cancelación acreditada del backend.
- [ ] Preset elegido con su jerarquía y modelo informativo; respuesta y métricas
  disponibles observables simultáneamente, con evidencia de fuente y alcance.
- [ ] Prueba con tráfico ajeno y telemetría ausente: no atribuir acumulados,
  tareas o recursos compartidos como cifras exclusivas del mensaje.
- [x] Cierre de sesión sin historial persistente de conversación.
- [x] Publicación Native AOT y ejecución del binario.

## Evidencia

- `--dashboard-tests`: 258 comprobaciones, incluidas las de `ChatTests` (SSE con el
  formato de llama-server, contexto de sesión, error HTTP, cancelación sin acreditar
  la del backend, anchos 80×24 y pestaña Herramientas). `RecipeWriterTests` falla
  también en el commit base; se verificó omitiéndolo de forma temporal.
- Publicación Native AOT `win-x64` sin advertencias; el binario ejecuta
  `chat --help`, rechaza sintaxis inválida (código 2) y entrada redirigida.
- CLI contra un servicio activo, sin enviar mensajes: `chat news/<clave>`, la clave
  sola dentro del proyecto, `--global` y `servicio/proyecto/clave` resuelven el mismo
  destino; un nombre no publicado lista los presets publicados con su servicio.
- La publicación Native AOT final ejecuta `chat --help` con la sintaxis vigente.
- Prueba manual del usuario en la TUI: reveló un cierre por enumeración concurrente
  del historial durante la respuesta, corregido con un candado compartido.
- Cierre: el usuario dio la tarea por terminada tras su prueba manual. Los criterios
  sin marcar no tienen evidencia registrada en esta tarjeta (conversación real
  documentada, `--load` con carga real, Ctrl+C durante la respuesta, botón Chat de
  En vivo, tráfico ajeno); se cerró con `--force`.
