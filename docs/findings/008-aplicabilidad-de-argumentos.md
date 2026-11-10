# Las definiciones de argumentos no cubren forks por completo

Fecha de registro: 5 de noviembre de 2026. Solicitado por el usuario durante la
revisión del manual de usuario ([077](../../specs/077-manual-usuario-y-reorganizacion-docs/README.md)).

## Intención

Las definiciones de argumentos buscan no depender de Coquena ante nuevas versiones de
llama.cpp y aceptar argumentos de forks. `applies-to` permite declarar desde qué build
y hasta cuál vale un argumento, y sumar una capa de definiciones sirve para comprobar
rápido la compatibilidad con un release.

## Problema observado

**Los forks quedan sin validación por versión.** `min-build` y `max-build` sólo se
comparan en builds oficiales `bNNNN`. Con otro backend,
[ArgumentValidation](../../src/Coquena/Recipes/ArgumentValidation.cs) registra
`argument-applicability-unknown`, sin rechazar el argumento. Esto coincide con
[parámetros](../design/catalogo-de-parametros.md#metaschema-de-definiciones), que
indica no inventar una versión comparable para externos. Justamente los forks son uno
de los motivos de estas definiciones.

**Verify no comprueba la aplicabilidad.** Una definición fuera del build del servicio
hace fallar `up` con `argument-not-applicable`, pero `verify` informó «Auditoría rápida
OK» sobre la misma combinación, con los recursos preparados. La comprobación rápida
exige hoy intentar una publicación.

**El prefijo `--` es fijo.** El usuario señala que un argumento no siempre usa doble
guion. [ArgumentEmission](../../src/Coquena/Recipes/ArgumentEmission.cs) antepone `--` al
emitir por CLI, y `emit.cli.name` se define como nombre sin `--`. Un fork que use `-x`,
otro prefijo o ninguno no puede representarse. El usuario propone indicar el prefijo en
la definición, con `--` por defecto y admitiendo un guion, ninguno u otro texto.

**El catálogo integrado no valida las recetas.** `param show ctx-size --global` lo
declara `integer`, pero una receta con `ctx-size: abc` pasó `verify` y `up` publicó el
preset. En cambio, una definición propia en `arg-defs` sí rechaza un valor inválido.
El usuario aclara que «conocidos», en [parámetros](../design/catalogo-de-parametros.md),
son los argumentos que el proyecto llama.cpp define al momento de compilar Coquena. Por
lo tanto, el modo permisivo debería validarlos, y este comportamiento es un defecto. La
redacción de ese documento quedó ambigua.

**Una definición de la receta debe prevalecer sobre el catálogo.** El usuario reconoce
como defecto que redefinir un argumento no tenga esa garantía: lo definido en la receta
debe pisar lo que trae el binario. Es lo que permite adaptarse a un cambio de llama.cpp
o de un fork sin una nueva versión de Coquena.

## Evidencia

Con `verify`, hoy la receta ya prevalece: redefinir `ctx-size` con `maximum: 10` rechazó
20, y un argumento propio pudo usar `c`, alias de `ctx-size` en el catálogo. Pero esto
ocurre porque el catálogo no se aplica; no hay una precedencia implementada que lo
garantice cuando se corrija. No se comprobó con `up`.

El rechazo y el resultado de `verify` se comprobaron con el binario Native AOT 0.3.0,
un backend oficial b12100 y `max-build: b11000`. El caso de forks surge de la lectura
del código; no se ejecutó con un ejecutable externo.

Validación por alias, comprobada con el binario Native AOT 0.3.0: una definición propia
con `aliases: [o]` rechazó `o: 3` por superar `maximum: 2`, sin repetir la validación
para el alias. Escribir `c` y `ctx-size` en un mismo preset falló con
`emission-key-collision`, y `c: abc` y `ctx-size: abc` resultaron el mismo preset.

## A evaluar

- Cómo identificar versiones de un fork: declaradas en la receta, por `distribution`,
  por `capabilities` u otro criterio.
- Si el diagnóstico `argument-applicability-unknown` llega a verse en la CLI.
- Si `verify` debe comprobar la aplicabilidad con el build resuelto.
- Si el prefijo pertenece a `emit.cli`. Afecta sólo a la emisión CLI, como
  `service.server.args`: los argumentos de presets se publican como claves INI.
- La intención del usuario: evitar escribir archivos INI, que se regeneran desde el YAML,
  y no duplicar validaciones. Una definición con sus aliases debe servir para todas las
  formas de un argumento, incluidos los de servidor, que no van al INI.
- Por qué el catálogo integrado no se aplica a las recetas.
- Al corregirlo, garantizar el orden: receta sobre catálogo, incluidos aliases y reglas.
- Aclarar «conocidos» en la documentación funcional.
- Cómo se relaciona con `aliases`. Hoy un alias es otro nombre aceptado en la receta,
  como `c` para `ctx-size`, y no cambia lo emitido; sus claves siguen
  `[A-Za-z0-9][A-Za-z0-9-]*`, sin guiones iniciales.
