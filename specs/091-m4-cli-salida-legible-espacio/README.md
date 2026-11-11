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
vez, salida legible por defecto y una forma de conocer el espacio en disco.
Entrega de [M4](../../docs/milestones/M4-experiencia-completa.md).

## Problema

- `verify` muestra un error por vez; `update` y `upgrade` imprimen JSON
  ([hallazgo 006](../../docs/findings/006-verify-un-error-por-vez.md)).
- No hay comando para conocer el espacio total usado
  ([hallazgo 005](../../docs/findings/005-espacio-usado-no-consultable.md)).
- `import` informa «presets seleccionados confirmados» aunque no se eligieron
  presets ([hallazgo 013](../../docs/findings/013-estado-navegacion-tui.md)).

## Alcance propuesto

- `verify` lista todos los problemas encontrados.
- `update` y `upgrade` con salida legible por defecto y `--json` como opción.
- Mensajes de `import` según las categorías elegidas.
- Consulta del espacio usado por modelos, backends, descargas y logs.

## Por concretar

- [ ] Dónde vive la consulta de espacio: comando propio o dentro de `clean`/`verify`.
- [ ] Compatibilidad: si algún consumidor depende hoy de la salida JSON.
- [ ] Actualizar [comandos](../../docs/design/comandos.md) y el manual en la release.

## Criterios de aceptación (borrador)

- [ ] Pruebas de salida legible y JSON, con casos de varios errores.
- [ ] Publicación Native AOT y ejecución del binario.
