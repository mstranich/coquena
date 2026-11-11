---
status: complete
created: 2026-11-11
priority: medium
tags:
- m4
- tui
- dashboard
created_at: 2026-11-11T13:06:38.643450100Z
updated_at: 2026-11-11T19:04:51.084540700Z
completed_at: 2026-11-11T19:04:51.084540700Z
transitions:
- status: draft
  at: 2026-11-11T13:07:22.720223900Z
- status: planned
  at: 2026-11-11T14:52:23.070647100Z
- status: in-progress
  at: 2026-11-11T18:18:35.898747Z
- status: complete
  at: 2026-11-11T19:04:51.084540700Z
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
- Cada aviso ofrece su acción: Actualizaciones tras Comprobar o Actualizar, Reintentar
  tras otra operación fallida (salvo Quitar y Olvidar, cuyo plan confirmado puede haber
  cambiado), Ver log para un servicio y Ver detalle en los demás. Enter sobre la fila
  muestra el aviso. La acción que lleva a otra vista cambia de pestaña y la selecciona;
  Reintentar se ofrece una sola vez por aviso.
- Un aviso que llega con la pestaña activa se agrega arriba sin mover la selección ni
  el foco, y cuenta como no leído.
- Abrir o dejar la pestaña no marca nada como leído: sólo «Marcar leídas», que se
  muestra junto al contador «N sin leer» mientras haya avisos sin leer.
- Esc no cambia de pestaña en Notificaciones ni en Descargas.

## Por concretar

- [x] Avisos que llegan con la pestaña activa: agregar la fila sin mover el foco.
- [x] Marcar leídas sólo con su botón, como muestran el wireframe Z y la vista previa.
- [x] Wireframe Z y [vista previa](../../docs/design/tui/vista-previa.html): ya muestran la
      pestaña resaltada, «N sin leer» y ningún «Volver».

## Criterios de aceptación (borrador)

- [x] Cambiar a Notificaciones y volver conserva el estado de cada pestaña.
- [x] La barra inferior queda visible y viva.
- [x] Teclado y clic; pruebas, publicación Native AOT y revisión visual.

## Evidencia

| Verificación | Resultado |
|---|---|
| `--dashboard-tests` | 225 comprobaciones; la pestaña ▲ se resalta activa y conserva el contador; botón de cada aviso según su acción |
| Suite completa | Termina con código 0; `HelpTests` vuelve a pasar tras ajustar la ayuda de `config inference-history` a 80 columnas |
| Publicación win-x64 Native AOT | Sin advertencias; la ayuda del binario muestra la sección Configuración corregida |
| Navegación con teclado | Confirmada por el usuario |
| Reintentar y Actualizaciones en el binario | Probadas por el usuario |
