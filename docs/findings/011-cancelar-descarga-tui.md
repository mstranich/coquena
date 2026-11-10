# Una descarga en curso no se puede cancelar desde la TUI

Fecha de registro: 10 de noviembre de 2026. Señalado por el usuario durante la
revisión de la gestión de descargas en la TUI ([083](../../specs/083-m4-tui-gestion-servicios/README.md)).

## Problema observado

Durante una descarga el usuario no encontró ningún botón para cancelarla.

## Evidencia

Descargas dibuja «Cancelar» en toda operación que no esté terminada, fallida o
cancelada. No se reprodujo el caso ni se diagnosticó la causa.

## Hipótesis sin comprobar

Una descarga lanzada desde «+ Backend» ocurre dentro de la preparación del backend,
y Descargas podría no mostrarla como una operación en curso con sus controles.

## A evaluar

- Reproducir el caso indicando desde dónde se inició la descarga.
- Si la cancelación desde la TUI debe tener el mismo efecto que `download cancel`.
