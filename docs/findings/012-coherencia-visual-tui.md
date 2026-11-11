# Coherencia visual entre vistas de la TUI

Fecha de registro: 10 de noviembre de 2026. Observado al recorrer con clics, a 80×24,
Recetas, Nueva combinación, Presets, Parámetros, Actualizaciones, Importar y el Editor
sobre un ámbito de prueba aislado ([086](../../specs/086-m4-tui-presets-parametros-importacion-actualizaciones/README.md)).
El usuario señaló inconsistencias de uso y prefirió registrarlas para corregirlas en conjunto.

## Problema observado

Las vistas resuelven de forma distinta los mismos elementos:

- **Selección.** Presets y Parámetros resaltan la fila elegida en naranja; Recetas
  y Nueva combinación, en verde agua.
- **Casillas.** Las listas de Nueva combinación e Importar dibujan `[X]`/`[ ]`; los
  controles de casilla del mismo diálogo usan `☐`. Recetas usa `[x]` en minúscula.
- **Botones.** Recetas separa los botones con dos espacios; Presets y Parámetros los
  dibujan contiguos. Un botón deshabilitado («Ver en vivo») queda desplazado respecto
  de los activos y «Detalle…» pasa sola a otra línea en Parámetros.
- **Rótulos de una combinación.** Recetas la lista como «coquena.yaml …e.yaml + .env»
  y su detalle usa «→»; el selector «Combinación…» de Presets muestra
  «coquena.yaml + override.yaml» y omite los `.env`.
- **Términos internos.** Importar nombra las categorías `presets`, `model`, `params`
  y `service`; una categoría vacía queda como «model ·». La lista de colisiones vacía
  no dice que no hay ninguna, «Conservar / Reemplazar» sigue activo sin nada que
  alternar y «Detalle de categorías…» no entra en el diálogo a 24 filas.
- **Rótulos redundantes.** Nueva combinación muestra la entrada de entorno como
  «.env .env».
- **Resaltado del Editor.** Dentro de un mapa `{…}`, un número seguido de coma
  (`8192,`) y los textos sin comillas no reciben color; el último valor del mapa sí.
- **Diálogos.** Los modales tienen tamaños y posiciones distintos: Combinación… ocupa
  casi toda la pantalla y tapa el árbol; los formularios son compactos y se alinean
  a la izquierda o al centro según la vista.

## Evidencia

Capturas locales del recorrido, fuera de Git. Durante la misma revisión se corrigió
la altura de los diálogos de mensaje, que ocultaba la última línea (por ejemplo, el
texto de Eliminar definición o el motivo de un error).

## A evaluar

- Si la vista previa fija una convención única para selección, casillas, separación
  de botones y estados deshabilitados, y cómo se aplica a las vistas existentes.
- Un formato común para nombrar una combinación (YAML en orden y `.env`) en listas,
  detalle y selectores.
- Rótulos en español para las categorías de importación, sin cambiar los valores de
  `--select` de la CLI.
- Si el resaltado YAML debe cubrir los valores dentro de mapas y listas en flujo.
