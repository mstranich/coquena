---
status: draft
created: 2026-11-12
tags:
- m4
- yaml
- schema
created_at: 2026-11-12T03:19:47.499002100Z
updated_at: 2026-11-12T03:19:47.499002100Z
---

# Cerrar M4 revisando el formato YAML de las recetas

## Objetivo

Dejar el formato de receta claro para uso diario antes de cerrar
[M4](../../docs/milestones/M4-experiencia-completa.md): nombres de campos que
digan lo que hacen, reflejados en el schema que genera
[090](../090-m4-tui-editor-mejoras/README.md).

## Problema

- `target` sólo admite `global` o `self` e indica en qué servicio se publican los
  presets ([servicios y recetas](../../docs/design/servicios-recetas.md)), pero el
  nombre no lo dice: agentes y usuarios no lo asocian con el servicio que aloja
  los presets.
- `arg-defs` no describe que define los parámetros disponibles para los presets
  ([catálogo de parámetros](../../docs/design/catalogo-de-parametros.md)).

## Alcance propuesto

- Renombrar `target` y `arg-defs`, sin ninguna compatibilidad con los nombres
  anteriores: un nombre viejo es un campo no reconocido, como cualquier otro.
- Descripción y valores permitidos de los campos renombrados en el schema
  generado por [090](../090-m4-tui-editor-mejoras/README.md).
- Actualizar documentación funcional, ejemplos, recetas de prueba y textos de la TUI.

## Por concretar

- [ ] Nuevo nombre de `target` (propuestas: `publish-to`, `presets-service`).
- [ ] Valores del campo renombrado. Hoy el YAML admite sólo `global` (el servicio
  global predeterminado) y `self` (el servicio propio del proyecto, único); el
  nombre de un servicio global concreto sólo se elige desde la CLI. Decidir si el
  YAML también lo acepta y si `self` cambia de nombre (por ejemplo, `local`).
- [ ] Nuevo nombre de `arg-defs` (propuestas: `params`, `param-defs`).
- [ ] Si las recetas globales importadas se migran o se reimportan.

## Criterios de aceptación (borrador)

- [ ] Los nombres viejos se rechazan como campos no reconocidos.
- [ ] El schema generado refleja los nombres nuevos y rechaza los viejos.
- [ ] Pruebas de lectura, validación e importación con los nombres nuevos.
- [ ] Publicación Native AOT y ejecución del binario.
