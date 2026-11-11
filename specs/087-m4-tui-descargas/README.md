---
status: planned
created: 2026-11-11
priority: medium
tags:
- m4
- tui
- dashboard
created_at: 2026-11-11T13:06:38.360310900Z
updated_at: 2026-11-11T14:52:20.697070100Z
transitions:
- status: draft
  at: 2026-11-11T13:07:22.718423500Z
- status: planned
  at: 2026-11-11T14:52:20.697070100Z
---
# Descargas como pestaña única y categorizada

## Objetivo

Descargas pasa a ser un único lugar para todas las transferencias: una pestaña que
abre el indicador `⇣` de la barra superior, con las operaciones de todos los ámbitos
agrupadas y ordenadas. Entrega de [M4](../../docs/milestones/M4-experiencia-completa.md).

## Problema

Cada ámbito tiene su página Descargas dentro de Definiciones; el indicador obliga a
elegir ámbito y el usuario debe saber dónde se inició una transferencia
([hallazgo 010](../../docs/findings/010-descargas-globales.md)). Navegar a una página y
volver pierde la vista de origen ([hallazgo 013](../../docs/findings/013-estado-navegacion-tui.md)).
Una descarga lanzada desde «+ Backend» no ofreció Cancelar
([hallazgo 011](../../docs/findings/011-cancelar-descarga-tui.md)).

## Comportamiento esperado

- `⇣` es una pestaña, como En vivo, Definiciones y Configuración: se abre con clic o
  teclado, se resalta cuando está activa y sigue mostrando el progreso cuando no.
  Cada pestaña conserva su estado (vista, selección, desplazamiento y foco) al cambiar
  a otra. No existe «Volver».
- La barra inferior de recursos queda visible y viva, como en las demás pestañas.
- Una sola lista con las transferencias de Global y de los proyectos conocidos,
  agrupada por **tipo de recurso** (modelos, companions, backends) y **ámbito**, con
  encabezado y contador por grupo, y ordenada por **estado y progreso**.
- Cada fila indica ámbito, recurso, avance, velocidad o estado y sus acciones:
  Pausar, Reanudar, Cancelar y Reintentar según corresponda. Toda operación activa o
  pausada ofrece Cancelar, incluidas las de la preparación de un backend.
- Un ámbito inaccesible o con estado inválido se informa sin ocultar los demás.
- Descargas deja de ser un nodo del árbol de Definiciones.

## Por concretar

- [ ] Anidación de los grupos (tipo dentro de ámbito o ámbito dentro de tipo) y
      cuáles se pliegan por defecto.
- [ ] Orden exacto entre estados y desempate por avance o por hora.
- [ ] Cuántas terminadas quedan a la vista y si se limpian desde la pestaña.
- [ ] Atajo de teclado de la pestaña y si abre siempre en su estado anterior.
- [ ] Wireframe N según la [vista previa](../../docs/design/tui/vista-previa.html) y la regla
      de [pantallas](../../docs/design/tui/README.md).
- [ ] Si cambia la regla de [descargas](../../docs/design/descargas.md) por ámbito o sólo
      su presentación; costo de leer muchos proyectos o rutas lentas.
- [ ] Reproducir el caso de 011 y comprobar que Cancelar equivale a `download cancel`.

## Criterios de aceptación (borrador)

- [ ] Cambiar a Descargas y volver a cualquier pestaña conserva su estado; no hay «Volver».
- [ ] La barra inferior queda visible y viva en la pestaña.
- [ ] Todas las transferencias aparecen agrupadas y ordenadas según lo acordado, con
      su ámbito; las acciones actúan sobre la operación y el ámbito correctos.
- [ ] Cancelar funciona en descargas iniciadas desde cualquier recorrido.
- [ ] Teclado y clic; fidelidad al boceto a 80×24 y en terminal mayor.
- [ ] Pruebas con varios ámbitos, publicación Native AOT y revisión visual.

## Exclusiones

- El hallazgo 009 (cambiar el backend de un servicio). Sin cambios en el motor.
