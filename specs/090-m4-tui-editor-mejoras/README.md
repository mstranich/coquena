---
status: complete
created: 2026-11-11
priority: medium
tags:
- m4
- tui
- dashboard
created_at: 2026-11-11T13:45:36.844634100Z
updated_at: 2026-11-13T01:57:48.280435900Z
completed_at: 2026-11-13T01:57:48.280435900Z
transitions:
- status: draft
  at: 2026-11-11T13:45:44.289589500Z
- status: planned
  at: 2026-11-12T12:43:21.817818700Z
- status: in-progress
  at: 2026-11-12T12:43:21.901022400Z
- status: complete
  at: 2026-11-13T01:57:48.280435900Z
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

- [x] Schema en `.coquena/recipe.schema.json`. Cada `coquena init` lo reemplaza si
      difiere y actualiza o agrega la referencia `$schema` de la receta, sin preguntar.

## Criterios de aceptación (borrador)

- [x] Pruebas de escritura con flujo, bloque, comentarios, CRLF y BOM; CLI y TUI
      comparten `RecipeEdit`; ninguna inserción en bloque genera llaves.
- [x] Pruebas de clonar (copia textual con comentarios, mapa en flujo) y de eliminar
      (misma capa, último preset, capa heredada, último argumento).
- [x] `verify`, Validar, las ediciones puntuales y la precondición de clonar usan
      `RecipeValidation` e informan todos los problemas.
- [x] Pruebas con varios errores en una receta, con líneas correctas tras apartar
      una entrada y referencias derivadas contadas junto a su causa; un error de
      sintaxis se informa con su línea.
- [x] Concordancia entre el schema generado y el lector (mismas formas en `RecipeShape`).
- [x] `init` crea la receta sin `{}` y con la referencia al schema; repetido,
      actualiza schema y referencia conservando BOM y finales de línea.
- [x] Un editor con soporte YAML detecta el schema tras `init` (VS Code, revisión manual).
- [x] Revisión visual en la TUI del Editor (revisión manual).
- [x] Publicación Native AOT (win-x64) y ejecución del binario.

## Evidencia

- Suite administrada completa (`Coquena.Tests` sin argumentos): sale con 0.
- `--dashboard-tests`: 249 comprobaciones, incluidas las del escritor, validación,
  schema, resaltado, Editor, autoría e `init`.
- `--recipe-tests` 323 y `--argument-tests` 182 comprobaciones.
- Se actualizaron tres pruebas previas que fallaban: orden de vistas del ámbito
  global, ancho de las filas de Actualizaciones (textos acortados) y el texto del
  catálogo de llama-server en `verify`.
- `dotnet publish -c Release -r win-x64` sin advertencias; con el binario: `init`,
  `param define`, `preset set`, `preset clone`, `preset remove` escriben en bloque;
  `init` repetido actualiza la referencia al schema; `verify` informa varios
  problemas con línea, cuenta referencias derivadas y sale con 2; `verify --json`
  devuelve siempre un objeto: `{"valid": true, "resources": [...]}` o
  `{"valid": false, "problems": [...]}` con salida 2.
- Un error de sintaxis YAML agrega «El resto del archivo no se pudo validar.».
- Suite contra el binario publicado (`Coquena.Tests` Native AOT con `coquena.exe`
  Native AOT): sale con 0. Se actualizaron pruebas que usaban comandos de M1 que la CLI
  ya no tiene (`preset create`, catálogo de parámetros sin `--global`) o un fixture
  que suponía que `coquena --version` fallaba.
- Ctrl+C: Coquena reactiva la interrupción al iniciar y antes de lanzar servidores,
  porque el estado «ignorar Ctrl+C» se hereda del proceso padre; `--runtime-tests` 95.
- Los errores de argumentos en ediciones ya no repiten la ubicación en el mensaje.
- Los mensajes de argumentos no representables usan datos del catálogo (valores o
  sintaxis del backend), no su descripción libre, que depende del idioma de origen.
