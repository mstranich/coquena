---
status: complete
created: 2026-11-17
tags:
- m5
- cli
created_at: 2026-11-17T15:14:50.553223800Z
updated_at: 2026-11-17T18:44:38.380479100Z
completed_at: 2026-11-17T18:44:38.380479100Z
transitions:
- status: in-progress
  at: 2026-11-17T15:14:55.285812Z
- status: complete
  at: 2026-11-17T18:44:38.380479100Z
---
# Línea de estado al seguir logs

## Objetivo

Al seguir los logs de un servicio (`service logs -f`, `logs -f`), ver al pie el
estado vivo del servicio sin abrir la TUI ni otra terminal, del mismo modo que
`chat` muestra su fila de telemetría debajo de la entrada.

## Alcance propuesto

- Con la salida en una terminal interactiva, `logs -f` reserva la última fila para
  una línea de estado que se redibuja en el lugar; las líneas del log se escriben
  por encima, sin mezclarse con ella.
- La línea muestra, separados por ` · `: el servicio, su estado y salud; uso y VRAM
  de cada GPU del sistema, antes que CPU y RAM del proceso; el preset cargado o «sin presets cargados»; las tareas en curso
  con su fase y velocidad (tk/s) cuando el router informa `/slots`.
- Se actualiza con la misma observación que usa `ps -w`, incluyendo `/slots`, cada
  0,5 s (2 s en `ps -w` y En vivo), con la espera de reconexión configurada.
- Una tarea en su slot sin tokens generados se muestra en fase PROMPT, aunque su
  velocidad todavía no se conozca.
- Si el servicio se detiene o reinicia, el seguimiento de logs continúa como hoy y
  la línea indica «detenido» o «sin observación» hasta recuperar la conexión.
- Las líneas de advertencia y error del log se muestran con el glifo y el color de
  En vivo (▲ ámbar, ✗ rojo), con la misma clasificación; `NO_COLOR` los desactiva.
- Con la salida redirigida o canalizada (`> archivo`, `| grep`), no hay línea de
  estado y la salida es idéntica a la actual.
- La línea se recorta al ancho de la terminal con «…» y se borra al salir con
  Ctrl+C, dejando la terminal limpia.

## Fuera de alcance

- Opciones para elegir campos o desactivar la línea; se evaluarán con el uso.
- La línea en `logs show`, `logs list` o la salida reciente sin `-f`.

## Plan

- [x] Extraer de `chat` el dibujo de una fila fija al pie con texto por encima.
- [x] Componer la línea desde `ServiceObservation` e `InferenceTasks`.
- [x] Integrar en `logs -f` sólo con salida interactiva; textos en en-US y es-AR.
- [x] Documentar en `comandos.md` y en la ayuda.
- [x] Pruebas de composición, recorte y salida redirigida sin línea.
- [x] Publicación AOT y ejecución contra un servicio real.

## Verificación

| Prueba | Resultado |
| --- | --- |
| `LogStatusTests` | Composición sin observación, en marcha, con presets cargados y detenido; recorte con «…»; líneas completas por encima de la fila, línea partida entre escrituras y borrado al salir |
| Suite completa | Termina con código 0 |
| AOT `logs -f --global` en terminal | Muestra `default-service · HTTP 200 · CPU 0% · RAM 300,88 MiB · no presets loaded · no tasks` al pie; Ctrl+C borra la fila |
| AOT `logs -f --global` con una solicitud en curso | Al principio decía «no tasks» porque `ServiceWatch` no pedía `/slots`; con `observeInference` muestra `coding/Ornith-1.5-35B-A3B · s0 GENERATION 41,2 tk/s` |
