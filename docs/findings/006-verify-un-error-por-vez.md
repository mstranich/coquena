# La salida de la CLI orienta poco al usuario

Fecha de registro: 5 de noviembre de 2026. Formalizado a pedido del usuario durante la
revisión del manual de usuario ([077](../../specs/077-manual-usuario-y-reorganizacion-docs/README.md)).
El usuario ya conocía el comportamiento y lo considera un defecto de la CLI.

## Problema observado

Una receta con dos errores estructurales, un `target` inválido y una clave desconocida,
produjo un único diagnóstico en `coquena verify`. El usuario debe corregir y volver a
ejecutar el comando para descubrir el siguiente error. Esto reduce la utilidad de
`verify` como orientación al escribir una receta.

## Evidencia

Con el binario Native AOT 0.3.0, esa receta informó sólo `structure/unknown-key`, con
código de salida 2. El diagnóstico sí indica archivo, línea, columna y clave.
No se revisó si la validación se detiene en el primer error o si acumula varios y la
salida muestra sólo uno.

## A evaluar

- Qué errores pueden acumularse. Un YAML mal formado impide seguir, y un error de
  estructura en una capa puede invalidar la validación de la combinación.
- Si los demás comandos que validan recetas deben mostrar la misma lista.

## Salida JSON de update y upgrade

`coquena update` y `coquena upgrade` muestran JSON sin formato, con identificadores y
artefactos, en lugar de un resumen de qué cambiaría. El usuario debe interpretar ese
documento para saber si hay novedades. El comportamiento es preexistente y el usuario
lo formaliza aquí para no olvidarlo.

Comprobado con el binario Native AOT 0.3.0 en ambos comandos. Falta evaluar si la salida
legible debe ser la predeterminada, reservando JSON para `--json` como en otros comandos.

El manual documenta el comportamiento actual en «Check a recipe».
