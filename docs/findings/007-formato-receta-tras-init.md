# Editar una receta creada por init la deja en una sola línea

Fecha de registro: 5 de noviembre de 2026. Solicitado por el usuario durante la
revisión del manual de usuario ([077](../../specs/077-manual-usuario-y-reorganizacion-docs/README.md)),
que lo considera un defecto de la CLI.

## Problema observado

`coquena init` crea un `coquena.yaml` con el contenido `{}`. Las ediciones posteriores
con `preset set`, `preset bind`, `model set` y `model companion set` conservan ese
estilo: toda la receta queda en una sola línea con formato de JSON. También se escribe
`"files": null`, un campo que el usuario no pidió.

La receta sigue siendo válida, pero es difícil de leer, y cada cambio modifica la única
línea del archivo, lo que empeora los diffs. Esto contradice el objetivo de que la
receta sea un documento que se versiona y se comparte.

## Evidencia

Con el binario Native AOT 0.3.0, ejecutar `init` y luego esos comandos produjo una
línea que comienza con `{"companions": {"vision": {"files": null, ...`. En cambio, una
capa escrita a mano en estilo de bloque conservó ese estilo al editarla con `preset set`,
aunque la clave nueva quedó entre comillas (`"ctx-size": 2048`).

## A evaluar

- Si `init` debe crear un archivo vacío o con estilo de bloque en lugar de `{}`.
- Si la edición debe usar siempre estilo de bloque al agregar claves.
- Por qué se escribe `files: null` y si las claves nuevas necesitan comillas.

El manual propone este recorrido en «Configure a preset from the CLI».
