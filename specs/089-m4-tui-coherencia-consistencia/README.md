---
status: draft
created: 2026-11-11
priority: medium
tags:
- m4
- tui
- dashboard
created_at: 2026-11-11T13:45:36.614033500Z
updated_at: 2026-11-11T13:45:44.513577400Z
transitions:
- status: draft
  at: 2026-11-11T13:45:44.287384500Z
---
# Unificar la coherencia visual y de navegación entre vistas de la TUI

## Objetivo

Fijar una convención única para los elementos que hoy cada vista resuelve a su
manera y aplicarla a todas las pantallas, junto con los problemas de estado y
navegación que no resuelven [087](../087-m4-tui-descargas/README.md) y
[088](../088-m4-tui-notificaciones/README.md).
Entrega de [M4](../../docs/milestones/M4-experiencia-completa.md).

## Problema

Las vistas difieren en color de selección, casillas, separación de botones,
estados deshabilitados, rótulos de una combinación, términos internos en inglés y
tamaño de los diálogos ([hallazgo 012](../../docs/findings/012-coherencia-visual-tui.md)).
Además, Recetas desincroniza fila resaltada y detalle, P/Q/U no nombran la
combinación en uso y los resultados de las operaciones sólo llegan como aviso
([hallazgo 013](../../docs/findings/013-estado-navegacion-tui.md)). Presets atribuye
todo el preset a la última capa y Comprobar pide filtros como texto libre
([hallazgo 014](../../docs/findings/014-procedencia-y-filtros-autoria.md)).

## Alcance propuesto

- Convención documentada en [pantallas](../../docs/design/tui/README.md) y la vista
  previa: selección, casillas, botones y deshabilitados, rótulo de combinación y
  familias de diálogo.
- Aplicarla a todas las vistas existentes, incluidos L y V.
- Modales con superficie elevada, borde redondeado de una celda y altura según su
  contenido, según [pantallas](../../docs/design/tui/README.md); hoy se dibujan sin
  borde ni sombra y se confunden entre sí cuando se superponen.
- Rótulos en español para las categorías de Importar, sin cambiar `--select`.
- Sincronizar selección y detalle en Recetas; nombrar la combinación en P, Q y U.
- Mostrar en cada vista el resultado de su última operación.
- Procedencia por argumento en P y filtros de Comprobar elegibles de una lista.
- Las inconsistencias que el usuario detecte al revisar se suman a esta entrega.

## Por concretar

- [ ] Inventario de diferencias por vista contra la convención acordada.
- [ ] Qué partes de 014 entran aquí y cuáles requieren decisión propia.
- [ ] Orden respecto de 087 y 088, que convierten Descargas y Notificaciones en pestañas.
- [ ] Configuración como pestaña con sólo un ícono, para ganar espacio en la barra superior.

## Criterios de aceptación (borrador)

- [ ] Todas las vistas usan la misma convención a 80×24 y en terminal mayor.
- [ ] Revisión visual con el usuario y pruebas de diseño bajo estrés por vista.
- [ ] Publicación Native AOT y ejecución del binario.
