---
status: archived
created: 2026-11-17
priority: medium
tags:
- m5
- tui
- cli
created_at: 2026-11-17T04:30:00.757997200Z
updated_at: 2026-11-18T03:11:37.772490Z
completed_at: 2026-11-17T12:39:31.191557500Z
transitions:
- status: in-progress
  at: 2026-11-17T04:33:53.549215900Z
- status: complete
  at: 2026-11-17T12:39:31.191557500Z
- status: archived
  at: 2026-11-18T03:11:37.772490Z
---
# Cargar, descargar y retirar presets desde En vivo

## Objetivo

Operar un preset publicado sin salir de En vivo ni pasar por Definiciones:
cargarlo o descargarlo de la memoria del router y retirarlo del servicio.
Hoy sólo existe `withdraw`; cargar se hace indirectamente con `chat --load` y
descargar no tiene comando. Entrega de
[M5](../../docs/milestones/M5-rendimiento.md).

## Alcance propuesto

- CLI: `load <preset>` y `unload <preset>` con `[--service <nombre|default|self> | --global]`,
  con la misma referencia que `chat`: el nombre publicado completo del preset dentro
  del servicio.
- `load` pide la carga al router y espera a que quede cargado o falle, informando
  el resultado; `unload` pide la descarga y espera a que deje de estar cargado.
- No inician servicios ni publican de forma implícita: un preset no publicado o un
  servicio detenido se rechazan con el motivo.
- TUI, En vivo: con un preset enfocado, junto a Chat, ofrecer Cargar o Descargar
  según su estado y Retirar. Retirar usa `withdraw` del proyecto dueño del preset,
  resuelto igual que Chat; si no se encuentra, el botón queda deshabilitado.
- Mientras la acción corre, sus pasos y su resultado se muestran en la sección,
  sin modal, como Up en Recetas.
- Documentar en [comandos](../../docs/design/comandos.md), `--help`,
  [interfaces visuales](../../docs/design/interfaces-visuales.md), wireframe de En vivo
  y vista previa.

## Decisiones confirmadas

- Descargar un preset que atiende peticiones pide confirmación; en la CLI, sin
  interacción disponible se rechaza.
- Mientras una carga o descarga está en curso, Cargar, Descargar y Retirar quedan
  deshabilitados; no se cancela la carga.
- `load` y `unload` operan un único preset por invocación.

## Plan

- [x] Agregar la descarga al router (`POST /models/unload`) y la espera de estado.
- [x] Comandos `load` y `unload` con ayuda y localización en-US/es-AR.
- [x] Botones en En vivo y resolución del proyecto dueño para Retirar.
- [x] Documentación funcional, wireframe y vista previa.

## Pruebas

- [x] Router simulado: carga, descarga, rechazo, fallo de carga y límite de espera.
- [x] CLI: preset no publicado y servicio detenido rechazados con motivo.
- [x] TUI: botones según estado, deshabilitados mientras corre una operación.
- [x] Publicación Native AOT y recorrido real contra un servicio en ejecución.
