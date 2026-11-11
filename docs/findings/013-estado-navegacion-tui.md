# Estado, navegación y resultados en la TUI

Fecha de registro: 10 de noviembre de 2026. Observado en el mismo recorrido que
[012](012-coherencia-visual-tui.md), sobre un ámbito de prueba aislado
([086](../../specs/086-m4-tui-presets-parametros-importacion-actualizaciones/README.md)).

## Problema observado

- **Volver desde Notificaciones.** Abrir Notificaciones marca la pestaña «En vivo»
  y «Volver» lleva a En vivo, no a la vista desde la que se abrió (por ejemplo,
  Definiciones › Actualizaciones). Reproduce el «Volver» confuso de
  [010](010-descargas-globales.md), que allí faltaba reproducir.
- **Fila resaltada y detalle.** Después de aceptar Nueva combinación, Recetas resalta
  la combinación anterior mientras el detalle y el marcador «•» corresponden a la
  nueva. Al volver a Recetas desde otra vista no queda ninguna fila resaltada.
- **Combinación en uso.** Presets, Parámetros y Actualizaciones trabajan sobre la
  combinación elegida en la sesión, pero su título sólo nombra el ámbito; la
  combinación se deduce del botón «Combinación…».
- **Resultado de las operaciones.** Comprobar, Actualizar e Importar se ejecutan en
  segundo plano y su resultado, correcto o fallido, sólo llega como aviso (▲/✓) en
  Notificaciones. La vista de origen no cambia ni muestra el resultado.
- **Mensaje de importación.** Al importar sólo parámetros, el aviso dice
  «presets seleccionados confirmados o conservados», aunque no se eligieron presets.
  El texto proviene de la salida de `import` en la CLI.

## Evidencia

Capturas locales del recorrido, fuera de Git. En la misma revisión se corrigieron
los casos en que la operación sólo podía fallar: Comprobar en una combinación aún
no preparada se deshabilita y explica por qué, Importar avisa antes de confirmar
si faltan servicio global y presets, y Clonar explica el nombre inválido y reabre
el formulario.

## A evaluar

- Si Notificaciones debe recordar la vista de origen o integrarse como panel que
  no cambia de pestaña.
- Cómo mantener sincronizados la fila resaltada, la combinación de la sesión y el
  detalle en Recetas.
- Dónde muestra cada vista el resultado de su última operación, sin duplicar el
  registro de Notificaciones.
- Si el mensaje de `import` debe depender de las categorías elegidas, en CLI y TUI.
