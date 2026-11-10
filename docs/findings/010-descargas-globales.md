# Descargas repartidas por ámbito en la TUI

Fecha de registro: 10 de noviembre de 2026. Señalado por el usuario durante la
revisión de la gestión de descargas en la TUI ([083](../../specs/083-m4-tui-gestion-servicios/README.md)).

## Problema observado

Cada ámbito tiene su propia sección Descargas y el indicador superior pide elegir
un ámbito antes de abrirla. Según el usuario, «Volver» desde Descargas lleva siempre
a En vivo y resulta confuso. No se localizó un botón «Volver» en esa pantalla; sólo
lo tienen el log histórico y Notificaciones, así que falta reproducir el caso.

## Preferencia del usuario

Una sola pantalla de Descargas con un solo acceso, que liste las operaciones de
todos los ámbitos y distinga el ámbito de cada una, sin «Volver».

## Evidencia

La TUI ya lee las transferencias de Global y de los proyectos conocidos para el
indicador superior. No se midió el costo con muchos proyectos ni con rutas lentas.

## A evaluar

- Dónde se ubica la pantalla única: pestaña, entrada fija del árbol o página propia.
- Cómo se muestra un ámbito inaccesible o con estado inválido sin ocultar sus operaciones.
- Si la regla de [descargas](../design/descargas.md) por ámbito cambia o sólo su presentación.
