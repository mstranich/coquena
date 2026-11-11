---
status: planned
created: 2026-11-11
priority: medium
tags:
- m4
- tui
- dashboard
created_at: 2026-11-11T13:06:38.643450100Z
updated_at: 2026-11-11T14:52:23.070647100Z
transitions:
- status: draft
  at: 2026-11-11T13:07:22.720223900Z
- status: planned
  at: 2026-11-11T14:52:23.070647100Z
---
# Notificaciones como pestaña desde la barra superior

## Objetivo

Notificaciones pasa a ser una pestaña que abre el indicador `▲`, como Descargas
([087](../087-m4-tui-descargas/README.md)), en lugar de una página que marca En vivo. Entrega de [M4](../../docs/milestones/M4-experiencia-completa.md).

## Problema

Abrir Notificaciones marca la pestaña En vivo y «Volver» lleva siempre a En vivo,
no a la vista de origen ([hallazgo 013](../../docs/findings/013-estado-navegacion-tui.md)).

## Comportamiento esperado

- `▲` es una pestaña: se resalta cuando está activa y sigue mostrando el contador
  cuando no. Cambiar a otra pestaña y volver conserva el estado de cada una.
  No existe «Volver».
- La barra inferior de recursos queda visible y viva.
- Las acciones de cada aviso (Ver log, Reintentar, Actualizaciones, Ver aviso)
  siguen funcionando; la que lleva a otra vista cambia de pestaña y la selecciona.

## Por concretar

- [ ] Avisos que llegan con la pestaña activa: agregar la fila sin mover el foco.
- [ ] Si Marcar leídas sucede al abrir, al cerrar o sólo con su botón.
- [ ] Wireframe Z y [vista previa](../../docs/design/tui/vista-previa.html).

## Criterios de aceptación (borrador)

- [ ] Cambiar a Notificaciones y volver conserva el estado de cada pestaña.
- [ ] La barra inferior queda visible y viva.
- [ ] Teclado y clic; pruebas, publicación Native AOT y revisión visual.
