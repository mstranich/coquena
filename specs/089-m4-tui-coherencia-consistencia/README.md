---
status: complete
created: 2026-11-11
priority: medium
tags:
- m4
- tui
- dashboard
created_at: 2026-11-11T13:45:36.614033500Z
updated_at: 2026-11-12T02:47:18.533959300Z
completed_at: 2026-11-12T02:47:18.533959300Z
transitions:
- status: draft
  at: 2026-11-11T13:45:44.287384500Z
- status: planned
  at: 2026-11-11T21:45:33.574288500Z
- status: in-progress
  at: 2026-11-11T21:45:33.818330200Z
- status: complete
  at: 2026-11-12T02:47:18.533959300Z
---
# Unificar la coherencia visual y de navegación entre vistas de la TUI

## Objetivo

Fijar una convención única para los elementos que hoy cada vista resuelve a su
manera y aplicarla a todas las pantallas, junto con los problemas de estado y
navegación que no resuelven [087](../087-m4-tui-descargas/README.md) y
[088](../088-m4-tui-notificaciones/README.md).
Entrega de [M4](../../docs/milestones/M4-experiencia-completa.md).

## Problema

Las vistas difieren en color de selección, casillas, separación de botones,
estados deshabilitados, rótulos de una combinación, términos internos en inglés y
tamaño de los diálogos ([hallazgo 012](../../docs/findings/012-coherencia-visual-tui.md)).
Además, Recetas desincroniza fila resaltada y detalle, P/Q/U no nombran la
combinación en uso y los resultados de las operaciones sólo llegan como aviso
([hallazgo 013](../../docs/findings/013-estado-navegacion-tui.md)). Presets atribuye
todo el preset a la última capa y Comprobar pide filtros como texto libre
([hallazgo 014](../../docs/findings/014-procedencia-y-filtros-autoria.md)).

## Alcance entregado

Reglas en [pantallas](../../docs/design/tui/README.md) y la vista previa.

- Convención: un espacio entre botones, deshabilitados en su lugar, casillas `☑`/`☐` y
  Configuración como ícono `≡` en el extremo derecho de la barra superior.
- Modales con superficie elevada, borde redondeado, título sobre el borde, altura según
  su contenido y botones de acción con el mismo aspecto en todos.
- Spinner Braille compartido en las esperas visibles y en el botón que las inició: Up y
  Down (En vivo y Recetas), Iniciar/Detener/Reiniciar, Actualizaciones, Mantenimiento y
  búsqueda en Hugging Face.
- Árbol de Definiciones reordenado, con Presets después de las entidades que relaciona.
- Desplegable de combinación en Servicio, Presets, Parámetros y Actualizaciones, en
  reemplazo del modal «Combinación…»; compartido con Recetas. En Global lista sus
  intenciones nombradas por origen («global + nombre») y «Todas las intenciones».
- Rótulo único de combinación; los archivos homónimos se distinguen por su ruta.
- Capas numeradas por valor en Servicio, Presets y Parámetros de un proyecto
  ([hallazgo 014](../../docs/findings/014-procedencia-y-filtros-autoria.md), procedencia).
- Filtros de Comprobar y Verificar elegibles de una lista de casillas, en lugar de
  referencias escritas (hallazgo 014, filtros).
- Importar: categorías en español sin cambiar `--select`, lista de colisiones vacía
  explícita, Conservar/Reemplazar deshabilitado sin colisiones y estado sólo con avisos
  vigentes o errores.
- Textos aprobados por el usuario en Actualizaciones, Mantenimiento, Nueva combinación,
  Sistema y los avisos de operación en curso.

## Fuera de esta entrega

El resaltado YAML en flujo y el formato de `null` de la edición puntual pertenecen a
[090](../090-m4-tui-editor-mejoras/README.md). Quedan para evaluación, sin compromiso de tarea:

- Color de selección unificado y `.env` redundante en Nueva combinación
  ([hallazgo 012](../../docs/findings/012-coherencia-visual-tui.md)).
- Resultado de la última operación en cada vista y volver desde Notificaciones
  ([hallazgo 013](../../docs/findings/013-estado-navegacion-tui.md)).
- Alternativa de glifos para terminales que no los muestran.
- Nombrar una intención global por su proyecto de origen y no por su `name:`.

## Evidencia

- `dotnet run --project tests/Coquena.Tests -c Release`: exit 0. Una corrida aislada
  falló en pruebas de backends no tocadas (`BackendPublicationTests`) y las siguientes
  pasaron.
- `dotnet publish src/Coquena -c Release -r win-x64 -p:PublishAot=true
  -p:IlcTreatWarningsAsErrors=true`: sin errores; `coquena --version` → 0.3.0.
- Vista previa: ninguna pantalla supera 80 columnas; wireframes con `plantuml -checkonly`.
- Revisión visual del usuario en la TUI a 80×24 durante la entrega (modales, Up,
  Importar, Actualizaciones, desplegable de combinación en Global).
- Las vistas de En vivo y los modales no tienen pruebas de dibujo automatizadas.
