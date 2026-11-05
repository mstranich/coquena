# La CLI no informa el espacio total usado

Fecha de registro: 5 de noviembre de 2026. Solicitado por el usuario durante la
revisión del manual de usuario ([077](../specs/077-manual-usuario-y-reorganizacion-docs/README.md)),
que lo considera un defecto de la CLI.

## Problema observado

No se encontró un comando que muestre cuánto disco usa Coquena, por ámbito o por tipo
de recurso. `clean --dry-run` informa sólo el espacio recuperable, y `clean --global`
y su variante de proyecto se consultan por separado. Un usuario no puede saber cuánto
ocupan sus modelos y backends sin inspeccionar las carpetas.

El problema pesa más porque cada ámbito conserva sus propios recursos: dos proyectos
pueden guardar el mismo modelo, según [servicios y recetas](../docs/design/servicios-recetas.md#ámbitos-y-propiedad).

## Evidencia

Revisión de `coquena --help` y ejecución de `clean --dry-run` con el binario Native AOT
0.3.0, que mostró `Recuperable: 0 B` sin el total usado. No se comprobó la salida de
`model list` o `backend list --json` en busca de tamaños por recurso.

## A evaluar

- Si el total pertenece a un comando nuevo, a `ps` o a `clean --dry-run`.
- Si debe incluir los proyectos conocidos, dado que cada uno guarda sus recursos en su
  carpeta y alguno puede no estar accesible.
- Cómo distinguir recursos propios, externos y archivos temporales.

La deduplicación de modelos es una mejora posible, registrada en el
[Roadmap](../docs/milestones/README.md#later); no depende de este hallazgo.
