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
# Escritura y validación del YAML de recetas, y Editor básico

## Objetivo

Que todo lo que escribe o valida una receta YAML lo haga bien, desde la CLI y
desde la TUI. El Editor de la TUI es un editor básico para salir del paso:
guardar, validar, Up/Down y edición de texto. No compite con editores externos
ni IDEs, pero lo poco que hace lo hace bien. Entrega de
[M4](../../docs/milestones/M4-experiencia-completa.md).

## Problema

- **Escritura en flujo, como JSON.** `RecipeEdit` inserta texto en el archivo
  original y conserva comentarios y BOM, pero el valor que inserta lo arma
  `RecipeEditCli` (`Render`, `Map`, `Literal`) siempre en flujo: llaves, corchetes
  y claves con comillas JSON. Afecta a todas las acciones de autoría (preset,
  param, model, backend) de la CLI y de la TUI, que comparten ese camino.
- **Clonar reconstruye en lugar de copiar.** `preset clone` arma el preset a
  partir de los valores leídos; pierde comentarios, comillas y forma del original.
- **Eliminar deja `null` donde no corresponde.** `remove` escribe siempre `null`
  en la última capa. Es correcto cuando el preset se hereda de una capa inferior
  (null retira lo heredado), pero si el preset está en la misma capa debería
  borrarse la clave. Cuando corresponde `null`, queda `chat:` con `null` en la
  línea siguiente ([hallazgo 014](../../docs/findings/014-procedencia-y-filtros-autoria.md)).
- **`init` escribe `{}`** y la primera edición convierte la raíz entera a flujo
  ([hallazgo 007](../../docs/findings/007-formato-receta-tras-init.md)). El lector
  ya acepta un archivo sólo con comentarios como mapa vacío.
- **Validación duplicada.** `verify` (`VerifyCli.ValidateRecipe` más intenciones
  de modelos y backend) y Validar del Editor (`RecipeDrafts.Validate`) repiten la
  misma secuencia en dos copias que pueden divergir.
- **Un error por vez.** La validación corta en la primera excepción (unos 120
  `throw` en `src/Coquena/Recipes`); el usuario corrige y vuelve a validar.
- **Clonar sobre una receta inválida** no lo advierte de forma clara.
- **No hay schema** de la receta para editores externos y agentes.
- **Resaltado incompleto:** números seguidos de coma y textos sin comillas en
  mapas en flujo quedan sin color ([hallazgo 012](../../docs/findings/012-coherencia-visual-tui.md)).

## Alcance

1. **Escritura en bloque**, única para CLI y TUI: elementos nuevos en bloque con
   la indentación del archivo, comillas YAML sólo cuando hacen falta, `clave: null`
   en la misma línea. Donde el archivo ya usa flujo, la inserción respeta ese estilo.
2. **Clonar copiando el texto** del preset de origen y reindentándolo.
3. **Eliminar:** borrar la clave si el elemento está en la misma capa; `null` si
   se hereda de una capa inferior.
4. **Validador único** de la receta, que recibe lo que el usuario ve (borradores
   o archivos guardados) y devuelve una lista de diagnósticos con archivo, línea
   y ruta. Lo usan Validar del Editor, la precondición de clonar y `verify`, que
   sigue el orden: YAML → estructura de archivos → checksum si se pide.
5. **Varios diagnósticos por validación**, hasta donde YamlDotNet lo permita: un
   error de sintaxis YAML se informa solo, con su línea; los errores de
   estructura, campos, valores y argumentos se acumulan.
6. **Editor:** lista de diagnósticos con salto a cada línea.
7. **Precondición de clonar:** validar lo que el usuario ve, es decir, los
   borradores abiertos en el Editor aunque haya salido sin guardar, porque el
   clon se inserta ahí. Si hay errores, no clonar y volver al Editor con la lista.
8. **Schema:** JSON Schema generado desde las mismas reglas del lector, escrito
   por `init` en `.coquena` y marcado como no editable a mano. La receta creada
   por `init` lo referencia con un comentario `yaml-language-server: $schema=…`
   en lugar de `{}`.
9. **Resaltado** de mapas y listas en flujo.

## Fuera de alcance

- Renombrar `target` y `arg-defs`:
  [094](../094-m4-cierre-formato-yaml/README.md).
- Apply/Withdraw desde el Editor: no hay una forma clara de hacerlo con buena UX;
  siguen en la pestaña Presets.
- Recuperarse de errores de sintaxis YAML con otro parser.

## Por concretar

- [ ] Nombre exacto del schema en `.coquena` y si se regenera al actualizar Coquena.

## Criterios de aceptación (borrador)

- [ ] Pruebas de escritura con flujo, bloque, comentarios y BOM; mismo resultado
      desde CLI y TUI; ninguna inserción en bloque genera llaves.
- [ ] Pruebas de clonar (copia textual, reindentado, entre capas) y de eliminar
      (misma capa y capa heredada).
- [ ] `verify`, Validar y la precondición de clonar usan el mismo validador.
- [ ] Pruebas con varios errores en una receta: se informan todos los que no
      sean de sintaxis; un error de sintaxis se informa con su línea.
- [ ] Prueba de concordancia entre el schema generado y el lector.
- [ ] `init` crea la receta sin `{}` y con la referencia al schema; un editor
      con soporte YAML la valida.
- [ ] Revisión visual del resaltado y de la lista de diagnósticos.
- [ ] Publicación Native AOT y ejecución del binario.
