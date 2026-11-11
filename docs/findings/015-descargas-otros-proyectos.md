# Descargas de otros proyectos sin navegación hacia ellos

Fecha de registro: 11 de noviembre de 2026. Señalado por el usuario al acotar la
pestaña única de Descargas ([087](../../specs/087-m4-tui-descargas/README.md)).

## Problema observado

La TUI lee las transferencias de Global y de todos los proyectos conocidos, y el
indicador superior permite elegir cualquiera de esos ámbitos. Un ámbito de proyecto
que no es el de la carpeta donde se abrió la TUI no tiene, sin embargo, un recorrido
para abrir ese proyecto. Mostrar sus descargas sin poder navegar a él deja la operación
fuera del contexto donde se gestiona el resto del proyecto.

## Preferencia del usuario

Por ahora, Descargas muestra sólo el ámbito Global y el del proyecto actual; los demás
proyectos conocidos no se listan. Ver una descarga vigente de otro proyecto requeriría
primero un botón para navegar a la carpeta de ese proyecto y, desde ahí, a su descarga.

## Evidencia

Decisión del usuario sobre la tarjeta 087; no se revisó cómo se descubren los proyectos
conocidos ni el comportamiento de la TUI abierta fuera de un proyecto.

## A evaluar

- Cómo se navega desde la TUI a otro proyecto conocido y qué ocurre con la sesión actual.
- Si una descarga vigente en otro proyecto debe avisarse sin listarse, y dónde.
- Cómo se relaciona con el resto de las vistas que hoy aceptan elegir otros ámbitos.
