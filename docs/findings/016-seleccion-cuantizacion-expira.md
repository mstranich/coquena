# Elegir una cuantización después de unos minutos impide descargar

Fecha de registro: 13 de noviembre de 2026. Señalado por el usuario durante la
revisión del wording de la CLI ([091](../../specs/091-m4-cli-salida-legible-espacio/README.md)).

## Problema observado

El usuario abrió la lista de cuantizaciones de un modelo, la dejó varios minutos sin
elegir y, al elegir, recibió un mensaje que le impidió descargar. El mensaje sugiere
que falló una conexión, aunque el usuario sólo tardó en decidir. Quien elige no
debería notar ninguna diferencia por el tiempo que tarda en hacerlo.

## Evidencia

No se reprodujo con un repositorio real. La lectura del código confirma el mecanismo:

- `ModelSelectionMenu.SelectAsync` crea un `RemoteGgufInspector` antes de explorar y
  lo conserva mientras espera la elección.
- El inspector inicia un cronómetro al crearse y limita la sesión a
  `InspectionLimits.SessionSeconds` (120 s). El tiempo que el menú espera la elección
  cuenta para ese límite.
- Después de elegir, `ModelResolver.ValidateAsync` inspecciona los archivos de la
  variante. Los que ya inspeccionó la exploración salen de la memoria, sin consultar
  el tiempo; un archivo nuevo, como el resto de los shards de un modelo dividido,
  comprueba el tiempo restante y falla con `inspection-limit` («Tiempo de exploración
  agotado; exploración parcial.»).
- El menú muestra ese error y vuelve a ofrecer la lista. Como el cronómetro sigue
  corriendo, cada nueva elección de esa variante falla igual: hay que salir y repetir
  el comando.

No hay una conexión abierta que expire: cada rango se pide con una petición nueva.

## A evaluar

- Si el presupuesto de tiempo debe medir sólo el trabajo de red, sin la espera de la
  persona, o reiniciarse al validar la elección.
- Si una validación que excede el presupuesto debe repetir la exploración necesaria de
  forma transparente, en lugar de devolver la elección al menú.
- Qué ven la TUI y `up` en el mismo caso, porque usan el mismo menú.
