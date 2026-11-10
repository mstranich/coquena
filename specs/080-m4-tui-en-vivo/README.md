---
status: complete
created: 2026-11-09
priority: high
tags:
- tui
- dashboard
- m4
created_at: 2026-11-09T21:07:28.144149Z
updated_at: 2026-11-09T23:06:30.979998800Z
completed_at: 2026-11-09T23:06:30.979998800Z
transitions:
- status: in-progress
  at: 2026-11-09T21:09:03.183384700Z
- status: complete
  at: 2026-11-09T23:06:30.979998800Z
---
# Llevar la pestaña En vivo a la propuesta tal cual se ve

## Objetivo

Que la pestaña ● En vivo y las pantallas que cuelgan de ella se vean y se comporten
como en la [vista previa](../../docs/design/tui/vista-previa.html) a
80×24, con una refactorización de fondo si hace falta. La entrega se juzga
comparando lado a lado la TUI con la vista previa, no por la cantidad de datos
mostrados. Entrega de [M4](../../docs/milestones/M4-experiencia-completa.md).

## Alcance

Las cuatro pantallas que la vista previa ubica bajo la pestaña En vivo, más los
estados por los que pasa A según la selección y lo observado:

| | Pantalla | Se llega desde |
|---|---|---|
| A | En vivo | pestaña ● En vivo |
| I | Log histórico (instancia, RegEx, nivel, anterior/siguiente) | Histórico en el log |
| D | Hardware (dispositivo, curvas uso · vram · +ram, resumen) | barra inferior |
| Z | Notificaciones | indicador ▲ de la barra superior |

Estados de A que deben quedar resueltos con la misma estructura:

- servicio seleccionado y entrada seleccionada (detalle de entrada con prompt/gen,
  caché, aceptación, slots/decode, contexto por slot y tabla de tareas);
- servicio detenido o desconocido, entrada descargada o vencida (◌);
- sin entradas observadas y sin medición: la zona se reduce, nunca muestra n/d;
- log siguiendo, pausado y con descartes.

Las barras superior (pestañas, ⇣ y ▲) e inferior (CPU, RAM, GPU por dispositivo)
son parte del alcance porque enmarcan todas estas pantallas.

Fuera de alcance: las pestañas ◇ Definiciones y ≡ Configuración y sus pantallas.
Si alguna se ve incompleta al navegar, no bloquea esta tarjeta. Las acciones de
receta (Up, Down, preparación privada) no aparecen en A y salen de En vivo.

## Fuentes

- Propuesta: [README](../../docs/design/tui/README.md), wireframes
  [A](../../docs/design/tui/A-en-vivo.salt.puml),
  [I](../../docs/design/tui/I-log-historico.salt.puml),
  [D](../../docs/design/tui/D-hardware.salt.puml),
  [Z](../../docs/design/tui/Z-notificaciones.salt.puml).
- Reglas vigentes: [interfaces-visuales](../../docs/design/interfaces-visuales.md),
  [ui-servicio](../../docs/design/ui-servicio.md),
  [observabilidad](../../docs/design/observabilidad.md),
  [privacidad](../../docs/design/privacidad.md).
- Base: [079](../079-m4-tui-refactor-diseno/README.md), que dejó En vivo por zonas
  sin llegar al aspecto de la vista previa.

## Criterios de aceptación

- [x] A coincide con la vista previa en zonas, orden de filas, colores de la paleta,
      glifos, alineación y botones (■ Detener, ⟳ Reiniciar; ❚❚ Pausar, ⤓ Seguir,
      Histórico).
- [x] Zonas por color de fondo, sin bordes ni líneas; foco turquesa y selección
      terracota como define la propuesta.
- [x] Cada estado de A listado en Alcance se reproduce con el binario y conserva
      la estructura sin desbordes a 80×24.
- [x] Con un servicio en inferencia se ven prompt/gen con pico, contexto por slot
      y la tabla de tareas actualizándose.
- [x] I, D y Z se abren desde su punto de entrada, se ven como la vista previa y
      vuelven a A.
- [x] En terminales mayores el espacio extra va a log, gráficos y tablas; barras y
      títulos conservan su altura.
- [x] Pruebas pertinentes en verde y Native AOT publicado y ejecutado contra un
      servicio en línea.
- [ ] Capturas anonimizadas de A (en inferencia), I, D y Z revisadas con el
      usuario junto a la vista previa.

## Notas

- Si una pantalla necesita un dato sin fuente (por ejemplo avisos para Z o
  aceptación de especulación), se informa y se decide antes de inventarlo.

## Avance 2026-11-09

- En vivo, Hardware, Log histórico y Notificaciones se dibujan con filas de marcado
  en la notación de la vista previa (`{clase|texto}`, `{clase@acción|texto}`):
  `Observation/Markup.cs`, `Observation/LivePresentation.cs`, `DashboardCanvas.cs`.
- Up, Down y Preparación privada pasaron a Definiciones.
- Hardware conserva 5 min por métrica (`HardwareHistory`, máximo por columna).
- Notificaciones lista lo observado en la sesión: cambios de estado del servicio
  seleccionado y errores de operaciones. No hay fuente persistente de avisos.
- ⇣ muestra 0: la TUI todavía no tiene fuente de descargas.
- Verificado: pruebas del dashboard (96 comprobaciones), Native AOT publicado y
  ejecutado a 80×24 contra un servicio en línea (A con servicio y entrada).
- Sin verificar: I, D y Z no se recorrieron con clics; A durante una inferencia;
  terminal mayor; capturas con el usuario. La suite completa falla antes, en
  `help fits 80 columns`, igual que en `HEAD`.

## Cierre 2026-11-09

El usuario revisó A en uso real y dio la tarjeta por cerrada. Durante la revisión se
agregaron: contexto por slot que persiste hasta la siguiente generación, columnas
TTFT (`prompt eval time` del log del backend, por slot y tarea) y FIN, «pico · fin»
en las curvas de una tarea terminada y caché calculada sobre prompt + cacheados.

El usuario verificó con el binario los estados de A distintos de servicio en línea,
el recorrido de I, D y Z y una terminal mayor que 80×24.

No verificado al cierre: capturas anonimizadas versionadas.
