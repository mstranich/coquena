# Procedencia de presets y filtros de actualización en la TUI

Fecha de registro: 10 de noviembre de 2026. Observado en el mismo recorrido que
[012](012-coherencia-visual-tui.md)
([086](../../specs/086-m4-tui-presets-parametros-importacion-actualizaciones/README.md)).

## Problema observado

- **Procedencia por argumento.** Con `coquena.yaml` y un override, Presets muestra
  «chat en override.yaml» para todo el preset, aunque `context` provenga de
  `coquena.yaml` y sólo `temperature` del override. La columna RECETA no indica de
  qué capa sale cada valor.
- **Borrador de Eliminar.** Eliminar escribe la desasignación en la última capa con
  un formato poco habitual (`chat:` y `null` en la línea siguiente) en vez de
  `chat: null`.
- **Filtros de Comprobar.** Actualizaciones pide los filtros como texto libre
  («referencias») con la sintaxis de selectores de la CLI, sin ofrecer los recursos
  de la combinación para elegir.

## Evidencia

Capturas locales del recorrido, fuera de Git. `PresetRows` toma el origen del valor
fusionado del preset; `ResourceSelector.Parse` valida los filtros escritos.

## A evaluar

- Si P debe mostrar la capa de origen por argumento y en qué columna.
- El formato que la edición puntual compartida con la CLI debe producir para `null`.
- Si Comprobar ofrece una lista de recursos de la combinación, conservando el texto
  libre como alternativa.
