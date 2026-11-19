---
status: archived
created: 2026-11-17
tags:
- m5
- cli
created_at: 2026-11-17T19:50:23.340544600Z
updated_at: 2026-11-18T03:11:37.777533800Z
completed_at: 2026-11-17T22:13:31.745387800Z
transitions:
- status: in-progress
  at: 2026-11-17T20:18:21.361129900Z
- status: complete
  at: 2026-11-17T22:13:31.745387800Z
- status: archived
  at: 2026-11-18T03:11:37.777533800Z
---
# Chat no interactivo con un único prompt

## Objetivo

Probar un preset desde scripts o desde otra herramienta: enviar un único prompt,
recibir la respuesta y sus métricas y terminar, sin terminal interactiva. Amplía
`chat` ([095](../095-m5-chat-prueba/README.md)) con la misma resolución de preset
y servicio.

## Alcance propuesto

- `chat <preset> -p|--prompt <texto>` envía una sola solicitud y termina; no lee
  la entrada ni dibuja la fila de telemetría.
- `--prompt -` lee el prompt completo desde la entrada estándar, para canalizar
  texto o archivos (`Get-Content nota.md | coquena chat code/chat -p -`).
- En texto, la respuesta se escribe en la salida estándar a medida que llega, sin
  agregados, de modo que pueda canalizarse a otra herramienta. Las métricas del
  turno van a la salida de error al terminar: tokens y velocidad de prompt y
  generación, caché, borradores aceptados y tiempo al primer token, con los mismos
  nombres que la telemetría de `chat`.
- `--json` escribe al terminar un único objeto JSON con el preset, el servicio, la
  respuesta, el razonamiento (si lo hubo), el motivo de fin y las métricas.
- `--reasoning` incluye el razonamiento del modelo en la salida de texto, antes de
  la respuesta y marcado como tal; sin la opción se omite. En el chat interactivo,
  `/reasoning` alterna lo mismo durante la sesión; por defecto conserva el
  comportamiento actual.
- Acepta `--service` / `--global` y `--load` como el chat interactivo; un servicio
  detenido o un preset no publicado se rechazan con su motivo y un código de salida
  distinto de cero.
- Funciona con la salida redirigida; Ctrl+C cancela la solicitud y termina.

## Decisiones

- Entrada estándar: `--prompt -` la acepta.
- Streaming: la respuesta en texto se escribe a medida que llega; las métricas van a
  la salida de error para no mezclarse con la respuesta canalizada. `--json` escribe
  al terminar.
- Razonamiento: `--reasoning` en la CLI y `/reasoning` en el chat interactivo.

## Plan

- [x] Separar del chat interactivo el envío de un turno y la lectura de sus métricas.
- [x] Opciones `-p/--prompt` (con `-`), `--json` y `--reasoning`, con textos en en-US y es-AR.
- [x] Comando `/reasoning` en el chat interactivo.
- [x] Documentar en `comandos.md` y en la ayuda.
- [x] Pruebas de salida en texto y JSON, errores y códigos de salida.
- [x] Publicación AOT y ejecución contra un servicio real.

## Verificación

| Prueba | Resultado |
| --- | --- |
| `ChatOnceTests` | Salida sólo con la respuesta en orden; razonamiento marcado antes de la respuesta con `--reasoning`; sin texto en modo JSON; JSON con respuesta, razonamiento, fin y métricas; métricas con los nombres de la telemetría |
| Suite completa | Termina con código 0, incluida la ayuda en 80 columnas |
| AOT `chat <preset> --global -p ...` | stdout `four`; stderr `done · prompt 7,1 tk/s · 15 tk · gen 17,7 tk/s · 53 tk · cache 0% · 1st 2,4 s`; salida 0 |
| AOT `-p -` con stdin y `--json` | Objeto con `response: pong`, razonamiento y métricas; caracteres no ASCII sin escapar |
| AOT `--reasoning` | `(reasoning)`, el razonamiento y luego la respuesta |
| AOT errores | `--json` sin `-p` y preset inexistente terminan con 2 y su motivo |
| `/reasoning` interactivo | Probado a mano por el usuario en una terminal |
