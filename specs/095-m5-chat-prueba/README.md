---
status: draft
created: 2026-11-12
tags:
- m5
- tui
- cli
created_at: 2026-11-12T03:19:48.954534500Z
updated_at: 2026-11-12T03:19:48.954534500Z
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
- `chat --preset <service>/<preset>` para la prueba rápida por CLI;
  la referencia usa el nombre publicado completo del preset dentro del servicio.
- Sesión CLI interactiva con `/exit` o Ctrl+C para salir. Línea de telemetría
  permanente debajo del área de escritura, actualizada sin alterar texto ni cursor.

## Decisiones confirmadas

- Chat de prueba por sesión, con contexto mientras está abierta y sin historial
  persistente. No guardar el diálogo como resultado de benchmark.
- Usar una entrada publicada sin iniciar servicios o publicar implícitamente.
- Mostrar métricas disponibles con fuente y alcance; no evaluar calidad automáticamente.
- La política de sesión no cambia la retención de logs del backend.

## Decisiones de interacción abiertas

- Contrato de conversación, límites de contexto y parámetros de generación.
- Streaming, interrupción de respuesta y presentación de errores.
- Reinicio de contexto y comportamiento al cambiar de preset o de instancia.
- Contrato de correlación entre petición y tarea observada; sin evidencia, mostrar
  actividad de la entrada como contexto, sin adjudicarla al mensaje enviado.
- Comportamiento con entrada/salida redirigidas pendiente de definir.
- Campos y compactación de la línea de telemetría; comprobar edición con texto
  largo, redimensionado, ausencia de métricas y recepción de respuesta.

## Fundamento de observabilidad

El [estudio](../../docs/milestones/M5-estudio-rendimiento.md) distingue esta prueba
de los tokens sintéticos de llama-bench y de una carga HTTP controlada. El cliente
conoce envío y respuesta, pero los observadores actuales no acreditan por sí solos
el vínculo con su slot. No trasladar conteo de fragmentos SSE como tokens.

## Criterios de aceptación (borrador)

- [ ] Vista previa TUI revisada antes de implementar.
- [ ] Conversación real con un preset publicado.
- [ ] Recorrido rápido con pocos prompts permite seguir procesamiento, generación,
  caché y especulación disponibles; las métricas ausentes se identifican sin inventarlas.
- [ ] Botón Chat de En vivo abre Chat con el mismo servicio/preset.
- [ ] Invocación CLI selecciona el mismo destino con `--preset <service>/<preset>`.
- [ ] CLI interactiva: telemetría debajo de la entrada durante edición y respuesta,
  sin perder texto/cursor ni imprimir una nueva línea por cada refresco.
- [ ] `/exit` y Ctrl+C salen desde edición y espera de respuesta; se restaura
  el terminal y se cancela la petición propia sin detener el servicio.
- [ ] No presentar desconexión del cliente como cancelación acreditada del backend.
- [ ] Preset elegido con su jerarquía y modelo informativo; respuesta y métricas
  disponibles observables simultáneamente, con evidencia de fuente y alcance.
- [ ] Prueba con tráfico ajeno y telemetría ausente: no atribuir acumulados,
  tareas o recursos compartidos como cifras exclusivas del mensaje.
- [ ] Cierre de sesión sin historial persistente de conversación.
- [ ] Publicación Native AOT y ejecución del binario.
