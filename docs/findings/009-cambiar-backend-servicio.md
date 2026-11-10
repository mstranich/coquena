# No se puede cambiar el backend de un servicio desde la TUI

Fecha de registro: 10 de noviembre de 2026. Señalado por el usuario durante la
revisión de la gestión de servicios en la TUI ([083](../../specs/083-m4-tui-gestion-servicios/README.md)).

## Problema observado

El alta de un servicio elige uno de los backends preparados en el ámbito, pero
después no hay forma de cambiarlo desde Servicios. La propuesta visual no define
ese recorrido.

## Idea del usuario

Un botón en Servicios y doble clic sobre un servicio abren un modal que lista los
backends disponibles del ámbito. Podría reutilizar el formulario de alta con los
demás campos en sólo lectura.

## A evaluar

- Qué operación de la CLI respalda el cambio y si la TUI debe usar la misma.
- Qué ocurre con un servicio en ejecución: reinicio explícito o efecto en el
  próximo inicio, sin cambiar una instancia activa de forma implícita.
- Cómo se comportan los consumidores y presets publicados con el backend anterior.
