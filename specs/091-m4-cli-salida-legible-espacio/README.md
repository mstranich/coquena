---
status: draft
created: 2026-11-11
priority: medium
tags:
- m4
- cli
created_at: 2026-11-11T13:06:39.371700700Z
updated_at: 2026-11-11T13:07:23.454330600Z
transitions:
- status: draft
  at: 2026-11-11T13:07:22.724960500Z
---
# Hacer legible la salida de la CLI y consultar el espacio usado

## Objetivo

Que los comandos informen lo que necesita quien los usa: todos los errores de una
vez, salida legible y breve por defecto, progreso visible sin verbosidad y una forma
de conocer el espacio en disco.
Entrega de [M4](../../docs/milestones/M4-experiencia-completa.md).

## Problema

- `verify` muestra un error por vez; `update` y `upgrade` imprimen JSON
  ([hallazgo 006](../../docs/findings/006-verify-un-error-por-vez.md)).
- No hay comando para conocer el espacio total usado
  ([hallazgo 005](../../docs/findings/005-espacio-usado-no-consultable.md)).
- `import` informa «presets seleccionados confirmados» aunque no se eligieron
  presets ([hallazgo 013](../../docs/findings/013-estado-navegacion-tui.md)).
- Los procesos largos (`up`, descargas, instalaciones) son verbosos y no muestran de
  forma compacta qué paso está en curso; muchos mensajes usan jerga técnica.

## Alcance propuesto

- `verify` lista todos los problemas encontrados.
- `update` y `upgrade` con salida legible por defecto y `--json` como opción.
- Mensajes de `import` según las categorías elegidas.
- Consulta del espacio usado por modelos, backends, descargas y logs.
- Progreso compacto al estilo npm o Docker Compose: en una terminal interactiva, cada
  paso ocupa una línea con el spinner Braille, que se fija con `✔` o `✖` al terminar.
  Los pasos en paralelo se muestran como un bloque de líneas que se actualizan juntas.
  Con salida redirigida, líneas simples sin animación ni caracteres de control.
  Si un paso falla, se muestra su detalle completo.
- Un único spinner compartido por el CLI y la TUI (hoy vive en
  `src/Coquena/DashboardResources.cs`).
- Modo detallado (por ejemplo `--verbose`) que conserva la salida completa actual.
- Textos claros para el usuario con criterio Boy Scout: se reemplaza la jerga al tocar
  cada comando y cada texto nuevo se propone al usuario antes de cambiarlo.
- Glifos de terminal (`✔`, `✖`, `⚠`, `→`) y color de forma coherente, con una
  alternativa en texto cuando la terminal no los soporta o no hay color.

## Por concretar

- [ ] Dónde vive la consulta de espacio: comando propio o dentro de `clean`/`verify`.
- [ ] Compatibilidad: si algún consumidor depende hoy de la salida JSON.
- [ ] Qué comandos muestran progreso con spinner y cuáles son sus pasos.
- [ ] Nombre y alcance del modo detallado; si `--quiet` también hace falta.
- [ ] Actualizar [comandos](../../docs/design/comandos.md) y el manual en la release.

## Criterios de aceptación (borrador)

- [ ] Pruebas de salida legible y JSON, con casos de varios errores.
- [ ] Pruebas de progreso en terminal interactiva y con salida redirigida, incluido un paso fallido.
- [ ] Los textos cambiados fueron propuestos al usuario y aprobados antes de aplicarse.
- [ ] Publicación Native AOT y ejecución del binario.
