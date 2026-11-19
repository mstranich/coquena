# Hallazgos para evaluación

Este registro conserva problemas, restricciones y evidencia para su evaluación.
Una entrada no constituye una tarea, una decisión funcional nueva ni autorización
para implementar una solución. LeanSpec mantiene las entregas que se acuerden
después de evaluar los hallazgos.

| Hallazgo | Situación |
| --- | --- |
| [001: Hijos sobrevivientes con identidad incierta](001-hijos-identidad-incierta.md) | Registrado para evaluar el tratamiento operativo y su representación en interfaces. |
| [003: Publicación sin interacción](003-publicacion-sin-interaccion.md) | Defecto de CLI señalado por el usuario; contradice la regla vigente, que debe evaluarse antes de corregir. |
| [004: Parada forzada y plazo no configurable](004-parada-sin-plazo-configurable.md) | Defecto de CLI señalado por el usuario; causa de la parada forzada sin comprobar. |
| [008: Definiciones de argumentos y forks](008-aplicabilidad-de-argumentos.md) | Los rangos de builds no alcanzan a forks, `verify` no los comprueba, el prefijo `--` es fijo y el catálogo integrado no valida las recetas. |
| [009: Cambiar el backend de un servicio](009-cambiar-backend-servicio.md) | Pendiente de diseño señalado por el usuario; sin recorrido en la TUI. |
| [012: Coherencia visual entre vistas de la TUI](012-coherencia-visual-tui.md) | Casillas, botones, rótulos, términos, diálogos y resaltado resueltos en [089](../../specs/089-m4-tui-coherencia-consistencia/README.md) y [090](../../specs/090-m4-tui-editor-mejoras/README.md); quedan el color de selección y el `.env` redundante en Nueva combinación. |
| [013: Estado, navegación y resultados en la TUI](013-estado-navegacion-tui.md) | Combinación en uso y mensaje de importación resueltos en [089](../../specs/089-m4-tui-coherencia-consistencia/README.md) y [091](../../specs/091-m4-cli-salida-legible-espacio/README.md); quedan volver desde Notificaciones y el resultado de la última operación en cada vista. |
| [015: Descargas de otros proyectos sin navegación](015-descargas-otros-proyectos.md) | Decisión del usuario: Descargas muestra sólo Global y el proyecto actual; falta navegar a otros proyectos. |
| [016: Elegir una cuantización tras una espera impide descargar](016-seleccion-cuantizacion-expira.md) | Defecto señalado por el usuario; mecanismo confirmado en el código, sin reproducir con un repositorio real. |

## Archivados

Hallazgos resueltos por una entrega; se conservan como antecedente.

| Hallazgo | Resolución |
| --- | --- |
| [002: Coordinación entre monitor de descargas y limpieza](002-coordinacion-monitor-limpieza.md) | Resuelto en [092](../../specs/092-m4-reproducir-monitor-limpieza/README.md): la publicación conserva su resultado y el monitor ya no compite con la limpieza. |
| [005: Espacio usado no consultable](005-espacio-usado-no-consultable.md) | Resuelto en [091](../../specs/091-m4-cli-salida-legible-espacio/README.md): `usage`/`df` informan el espacio usado por ámbito. |
| [006: La salida de la CLI orienta poco](006-verify-un-error-por-vez.md) | Resuelto en [091](../../specs/091-m4-cli-salida-legible-espacio/README.md): salida para personas en todos los comandos, JSON sólo con `--json`; `verify` informa varios errores ([090](../../specs/090-m4-tui-editor-mejoras/README.md)). |
| [007: Formato de la receta tras editar](007-formato-receta-tras-init.md) | Resuelto en [090](../../specs/090-m4-tui-editor-mejoras/README.md): `init` ya no escribe `{}` y la edición conserva el estilo. |
| [010: Descargas repartidas por ámbito](010-descargas-globales.md) | Resuelto en [087](../../specs/087-m4-tui-descargas/README.md): Descargas es una pestaña única con Global y el proyecto actual, sin «Volver». |
| [011: Cancelar una descarga desde la TUI](011-cancelar-descarga-tui.md) | Resuelto en [087](../../specs/087-m4-tui-descargas/README.md): toda operación activa o detenida ofrece Cancelar, incluida la preparación de un backend. |
| [014: Procedencia de presets y filtros de actualización](014-procedencia-y-filtros-autoria.md) | Resuelto en [089](../../specs/089-m4-tui-coherencia-consistencia/README.md): capas numeradas por valor y filtros elegibles de una lista; el `null` de eliminar se corrigió en [090](../../specs/090-m4-tui-editor-mejoras/README.md). |
