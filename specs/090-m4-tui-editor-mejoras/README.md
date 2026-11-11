---
status: draft
created: 2026-11-11
priority: medium
tags:
- m4
- tui
- dashboard
created_at: 2026-11-11T13:45:36.844634100Z
updated_at: 2026-11-11T13:45:44.515240200Z
transitions:
- status: draft
  at: 2026-11-11T13:45:44.289589500Z
---
# Mejoras y correcciones en el Editor

## Objetivo

Corregir y mejorar el Editor YAML de la TUI y el formato de los borradores que le
llegan desde las acciones de autoría. Entrega de [M4](../../docs/milestones/M4-experiencia-completa.md).

## Problema

- El resaltado deja sin color números seguidos de coma y textos sin comillas
  dentro de mapas en flujo ([hallazgo 012](../../docs/findings/012-coherencia-visual-tui.md)).
- Eliminar un preset genera `chat:` y `null` en la línea siguiente
  ([hallazgo 014](../../docs/findings/014-procedencia-y-filtros-autoria.md)).
- Tras `init`, editar conserva el estilo `{}` en flujo
  ([hallazgo 007](../../docs/findings/007-formato-receta-tras-init.md)). La edición
  puntual es compartida con la CLI: el formato que se defina vale para ambas.
- Las acciones de autoría insertan elementos nuevos con estilo de flujo, como si
  fuera JSON (`{…}`), en recetas escritas en bloque. Observado al clonar un preset
  en la TUI.

## Alcance propuesto

- Definición de resaltado YAML que cubra mapas y listas en flujo.
- Regla de formato de salida de la edición puntual (bloque o flujo, `clave: null`),
  conservando comentarios y el resto del archivo. Los elementos nuevos, como un
  preset clonado, se escriben en bloque con la indentación del archivo.
- Las mejoras y correcciones del Editor que el usuario detecte al usarlo.

## Por concretar

- [ ] Inventario de mejoras pedidas por el usuario.
- [ ] Estilo esperado y cuándo se convierte un `{}` vacío.

## Criterios de aceptación (borrador)

- [ ] Pruebas de edición con flujo, bloque, comentarios y BOM; mismo resultado
      desde CLI y TUI.
- [ ] Revisión visual del resaltado; publicación Native AOT.
