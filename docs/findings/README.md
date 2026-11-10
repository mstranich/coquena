# Hallazgos para evaluación

Este registro conserva problemas, restricciones y evidencia para su evaluación.
Una entrada no constituye una tarea, una decisión funcional nueva ni autorización
para implementar una solución. LeanSpec mantiene las entregas que se acuerden
después de evaluar los hallazgos.

| Hallazgo | Situación |
| --- | --- |
| [001: Hijos sobrevivientes con identidad incierta](001-hijos-identidad-incierta.md) | Registrado para evaluar el tratamiento operativo y su representación en interfaces. |
| [002: Coordinación entre monitor de descargas y limpieza](002-coordinacion-monitor-limpieza.md) | Carrera reproducida en código anterior a 054; recomendación pendiente de evaluación, sin corrección implementada. |
| [003: Publicación sin interacción](003-publicacion-sin-interaccion.md) | Defecto de CLI señalado por el usuario; contradice la regla vigente, que debe evaluarse antes de corregir. |
| [004: Parada forzada y plazo no configurable](004-parada-sin-plazo-configurable.md) | Defecto de CLI señalado por el usuario; causa de la parada forzada sin comprobar. |
| [005: Espacio usado no consultable](005-espacio-usado-no-consultable.md) | Defecto de CLI señalado por el usuario; sin comando para conocer el uso total de disco. |
| [006: La salida de la CLI orienta poco](006-verify-un-error-por-vez.md) | Defectos de CLI conocidos por el usuario y formalizados: `verify` muestra un error por vez; `update` y `upgrade` muestran JSON. |
| [007: Formato de la receta tras editar](007-formato-receta-tras-init.md) | Defecto de CLI señalado por el usuario; la edición conserva el estilo `{}` creado por `init`. |
| [008: Definiciones de argumentos y forks](008-aplicabilidad-de-argumentos.md) | Los rangos de builds no alcanzan a forks, `verify` no los comprueba, el prefijo `--` es fijo y el catálogo integrado no valida las recetas. |
| [009: Cambiar el backend de un servicio](009-cambiar-backend-servicio.md) | Pendiente de diseño señalado por el usuario; sin recorrido en la TUI. |
| [010: Descargas repartidas por ámbito](010-descargas-globales.md) | El usuario prefiere una única pantalla global; «Volver» confuso sin reproducir. |
| [011: Cancelar una descarga desde la TUI](011-cancelar-descarga-tui.md) | Defecto señalado por el usuario; sin reproducir ni diagnosticar. |
