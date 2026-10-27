# Definiciones de parámetros por proyecto

El catálogo describe argumentos del backend para validación y formularios. Pertenece
al ámbito de intención seleccionado, es reutilizable y se declara en `arg-defs.definitions` o se importa desde JSON mediante `arg-defs.imports`; no requiere
catálogo centralizado en home ni asociación a una máquina. No imponer flags oficiales
a forks con otra interfaz.

## Gestión de parámetros personalizados

CLI permite editar y mantener coherentes nombre, descripción, aliases, tipo,
opciones/default, ámbito, destino y aplicabilidad. No alterar por editar la instancia
activa ni borrar recursos asociados.

Un metaschema valida el JSON que define argumentos. Persona o IA puede redactarlo
desde documentación o --help del build objetivo. No hay garantía de extracción
automática exhaustiva; --version no certifica procedencia ni soporte de flags.

## Tipos y evidencia del backend

Las asignaciones usan claves sin prefijo:
```yaml
args:
  ctx-size: 32768
  metrics: true
```
Fragmento de args, no schema completo del proyecto.

Sin schema, uso libre: booleano YAML true emite flag sin valor y false lo omite;
"true"/"false" entre comillas se envían como valores literales. No confundir "false"
con orden de omisión. Convertir valores a argumentos sin ejecutar su contenido
como código de shell.

`args-mode` es permisivo por defecto; la raíz fija el modo general y cada bloque puede sobrescribirlo. Permisivo valida los conocidos y admite desconocidos. Estricto exige definición para todos. Ambos validan tipo, opciones, ámbito y dependencias conocidas:
entero, decimal, texto, enumeración, flag y booleano con valor son conceptos distintos.
Un flag no recibe un argumento booleano; la emisión de un booleano con valor sigue
el backend. Inválidos (y desconocidos en modo estricto) no se omiten con warning para ejecutar otra
configuración silenciosamente.

No deducir tipo de un valor aislado: 0/1 pueden ser números. Argumentos omitidos
conservan defaults del ejecutable, no se rellenan todos desde el catálogo.

### Diferencia entre integer y number

`integer` y `number` expresan restricciones distintas del schema, aunque una
implementación pueda usar la misma estructura de datos para representar ambos:

- `integer` exige notación entera, sin punto decimal ni exponente: una cantidad
  como `ctx-size` admite `8192`, pero rechaza `8192.0`, `8.192e3` y `8192.5`,
  aunque algunas de esas formas representen matemáticamente un entero.
- `number` admite notación entera, decimal y científica, siempre con valor finito:
  un parámetro como `temperature` puede admitir `1`, `0.7` o `7e-1`.

Compartir representación interna no vuelve equivalentes sus validaciones ni
obliga a mantener estructuras separadas. La distinción pertenece al contrato
del argumento; no se decide por cómo se almacena o emite el valor.

Ejemplo ilustrativo de definiciones y asignaciones válidas:

```yaml
arg-defs:
  definitions:
    ctx-size:
      type: integer
    temperature:
      type: number
presets:
  conversacion:
    args:
      ctx-size: 8192
      temperature: 0.7
```

| Asignación | Resultado con esas definiciones |
| --- | --- |
| `ctx-size: 8192` | Válida: entero. |
| `ctx-size: 8192.5` | Inválida: tiene parte fraccionaria. |
| `ctx-size: 8192.0` | Inválida: utiliza punto decimal. |
| `ctx-size: 8.192e3` | Inválida: utiliza notación científica. |
| `temperature: 1` | Válida: number también admite enteros. |
| `temperature: 0.7` | Válida: número con parte fraccionaria. |
| `temperature: 8192.0` | Válida: notación decimal. |
| `temperature: 8.192e3` | Válida: notación científica. |
| `ctx-size: "8192"` | Inválida: es texto, no un entero. |
| `temperature: "0.7"` | Inválida: es texto, no un número. |

La misma distinción se aplica al contenido de las expansiones explícitas
`|integer` y `|number`. Sin sufijo, la expansión conserva string. Las comillas
en un valor YAML ordinario también conservan texto: `"8192.0"` no es un número.
Las reglas de tipo y comillas del [formato](formato-proyecto.md#lectura-yaml)
siguen aplicándose.

## Ámbito de aplicación

Distinguir proceso/router de modelo/preset y destino CLI/INI u otro formato soportado.
Schema puede declarar cuándo aplica, dependencias entre argumentos y valores válidos.
Aplicabilidad por distribución/build/capacidades no es asociación a hardware local.

Un schema no adapta APIs de salud, eventos o reload. Forks no heredan capacidades
por usar la misma aceleración. Evidencia desconocida no es incompatibilidad probada.
El backend interpreta argumentos libres; Coquena siempre valida la estructura general.

## Asignaciones de recursos

Set guarda literal; bind vincula identidad de recurso del proyecto y resuelve su
ruta al componer. Un nombre corto sin prefijo no crea una referencia por coincidir
con un alias. Los selectores canónicos remotos completos en args de presets son
referencias directas, según [formato del proyecto](formato-proyecto.md#modelos-companions-y-referencias);
el envoltorio `literal` permite conservarlos como texto.
Un vínculo reemplaza el literal del mismo parámetro y viceversa; unset retira el
valor, no el recurso físico. No inferir método de especulación por el companion.

## Valores predeterminados para formularios

Metadatos permiten campos, ayudas, controles y opciones válidas en TUI/Web.
Default preasigna un campo al crear; al guardar es valor propio independiente.
Cambiar default no modifica presets existentes ni restaura valores quitados.
Sin schema se puede editar el mapa genérico. CLI da soporte de coherencia;
mantener definiciones no es el centro cotidiano del panel.

## Aplicación en Router Mode

Oficial puede recibir INI generado con secciones por preset, claves sin guiones y
rutas resueltas. Parámetros propios del router no pertenecen a opciones de modelo;
los argumentos CLI pueden prevalecer sobre el INI. Opciones exclusivas de INI no
se convierten automáticamente en flags CLI.

Si un argumento de modelo solo puede emitirse una vez en CLI, no representar valores
distintos por preset como si fueran independientes: diagnosticar composición imposible.
La representación/escapado corresponde al parser del build, no a reglas universales.

## Edición con asignaciones incompatibles

Validar schema y valores afectados antes de publicar. No borrar asignaciones
silenciosamente al cambiar un tipo. Si no puede publicarse un resultado válido,
conservar el anterior y mostrar ubicación/motivo para corrección explícita.
Schema válido no certifica carga o inferencia.

## Consulta del impacto

Consultar definiciones e impacto es local, sin descargar ni iniciar. Mostrar campo,
ámbito y asignaciones incompatibles. Salida redirigida sin controles de terminal.

## Eliminación de definiciones

Retirar schema no borra pesos ni valores de YAML; si deja de declararse schema efectivo y no se exige modo estricto,
se usa modo libre. Retirar una entrada mientras se exige modo estricto no vuelve válido
ese argumento. La política concreta de edición destructiva requiere impacto visible,
sin cascadas implícitas de archivos o ejecución.

## Restablecimiento de fábrica

No se restaura un catálogo global como condición para usar proyectos.
Restablecer definiciones de referencia es una edición explícita del schema del
proyecto, con impacto visible; no sustituir silenciosamente el schema de un fork.

## Comparación de versiones

Distribución determina cómo comparar versiones. Para bNNNN, comparar número de build,
con límites inclusivos; no extrapolarlo a externos. Versiones no son hashes de contenido.

## Disponibilidad por distribución y build

Soporte de un argumento, presencia de archivos y capacidad del ejecutable son ejes
distintos. No sustituir una regla exacta de aplicabilidad por otra general para
evitar su rechazo. Sin schema no se fuerza definición previa en un catálogo.

## Consulta del catálogo local

Definiciones efectivas corresponden al proyecto/combinación, reutilizables sin IDs
de la máquina de origen. Interfaz en [comandos](comandos.md#param).

## Origen del catálogo

Documentación y ayuda del backend son evidencia para autoría manual/asistida.
No presentar master como garantía para todos los releases.
El metaschema y la emisión se describen abajo; no se presupone framework de plugins o IA integrada.

## Aclaraciones

No valida calidad de respuesta, VRAM necesaria o compatibilidad funcional del proyector.
Libertad de argumentos no exime de validar rutas y propiedad antes de escribir.

## Composición y vocabulario declarativo

Imports son JSON locales explícitos relativos a la base, cargados en orden; después
se aplican definiciones inline. Las capas YAML posteriores prevalecen. Listas
reemplazan listas. No se ejecutan scripts ni se descargan catálogos implícitos.
El catálogo puede registrar su build de referencia, sin certificar soporte real.

Tipos: string, integer, number, boolean y array; restricciones enum, minimum,
maximum, pattern, items y alternativas any-of. Flag describe emisión de booleanos.
Emisión distingue CLI/INI, aliases, separadores, repetición y aridad: una lista no
implica repetir un flag. Se describen formas positiva/negativa de booleanos.

Relaciones admiten presencia, igualdad, pertenencia a listas, requisitos y conflictos.
Se validan args y referencias efectivos del mismo ámbito después de componer;
no se completan valores automáticamente. Ejemplo ilustrativo, no regla oficial MTP:

```yaml
arg-defs:
  definitions:
    metodo:
      type: string
      enum: [normal, experimental]
    auxiliar:
      type: string
      rules:
        - when:
            present: true
          requires:
            metodo:
              equals: experimental
```

Las reglas declaradas se comprueban en ambos modos dentro de su ámbito, también
cuando falta el argumento propietario. Así, `when: {present: false}` permite
exigir una alternativa ante su ausencia. When omitido sólo activa la regla si
el argumento está presente; no se materializan defaults para satisfacerla.
La gramática de condiciones y emisión conserva este vocabulario sin
convertirse en código ejecutable. JSON Schema valida documentos; referencias y
emisión son contratos adicionales de Coquena. Nuevos parámetros con reglas existentes
no requieren cambiar el binario; nuevas clases de regla sí requieren soporte.

## Metaschema de definiciones

El documento JSON importado admite sólo `definitions` (mapa requerido) y
`reference-build` (texto informativo opcional). Inline usa el mismo mapa de
definiciones. No se descarga un `$ref` ni se ejecuta un validador aportado.
El vocabulario es cerrado: clave desconocida es error, también en modo permisivo.
Claves de argumento siguen `[A-Za-z0-9][A-Za-z0-9-]*`, sin `--`.

| Campo de definición | Tipo y regla |
| --- | --- |
| type | string, integer, number, boolean o array; obligatorio salvo any-of. |
| description | Texto opcional. |
| aliases | Lista de nombres de argumento, única; no puede colisionar con nombres/aliases efectivos. |
| scope | server o preset; omisión permite ambos. |
| enum | Lista no vacía de valores escalares del tipo declarado. |
| minimum / maximum | Número finito; sólo integer/number, minimum ≤ maximum. |
| pattern | Texto regex sólo para string; sintaxis .NET sin backreferences/lookaround, tiempo máximo 100 ms. Timeout es validación no concluyente, nunca aceptación. |
| items | Definición de valor, requerida para array; sin aliases, scope, rules, applies-to ni emit. No arrays anidados. |
| any-of | Lista no vacía de definiciones de valor; excluye type y restricciones hermanas, salvo descripción/default y metadatos de emisión/ámbito/reglas. |
| default | Valor válido; sólo formularios, no se emite al omitir el argumento. |
| applies-to | Mapa opcional con distribution (string), min-build y max-build (bNNNN inclusivos), capabilities (lista de strings). Para externos no se inventa versión comparable. |
| emit | Mapa con destinos cli e ini, definidos abajo. |
| rules | Lista de reglas when/requires/conflicts. |

Una definición de valor admite type, enum, minimum, maximum, pattern, items y
any-of con las mismas restricciones. Array vacío es válido; su emisión es vacía.
Los imports se cargan por capa, luego inline; la última definición del mismo nombre
reemplaza completa la anterior. Null retira una definición heredada. Se validan
aliases, tipos y relaciones después de componer, sin heredar media definición de
emisión de otro build. Un alias se canonicaliza antes de detectar duplicados.

Cada regla admite `when`, `requires` y `conflicts`, y requiere al menos una de las
dos últimas. When omitido equivale a `{present: true}` del argumento propietario.
When admite `present` (booleano), `equals` (escalar) o `contains` (escalar en array),
exactamente uno. Requires/conflicts son mapas de argumento canónico a condición.
Todas las condiciones de requires deben cumplirse; ninguna de conflicts puede
cumplirse. No hay evaluación de scripts, recursión entre reglas ni búsqueda de
asignaciones que las satisfagan. Un argumento retirado con null no está presente;
false sí está presente. Referencias se validan por identidad y condición sobre
referencias compara la referencia tipada, no una ruta incidental de la máquina.
Una condición cuyo tipo no admite equals/contains es error de schema.

### Comportamiento de `when: {present: false}`

Una regla con `when: {present: false}` se activa cuando el argumento al que
pertenece está ausente del bloque efectivo. Coquena evalúa esa condición aunque
el argumento propietario no aparezca en `args`, tanto en modo permisivo como
estricto y después de componer las capas.

Si la condición se cumple, se comprueban `requires` y `conflicts`; incumplirlos
produce error de validación antes de preparar o ejecutar. Si el argumento está
presente, esa regla no se activa. La ausencia no provoca que se omita la evaluación
de la regla ni que se agreguen valores para satisfacerla.

`present` comprueba si el argumento tiene una asignación efectiva, no si su valor
es verdadero ni si terminará emitiéndose. False, cero y string vacío cuentan como
presentes. Omitir la clave sin herencia, o retirarla con null, la deja ausente.
Omitirla en un override conserva una asignación heredada y, por tanto, su presencia.
Un default del catálogo no crea una asignación.

Ejemplo ilustrativo para un backend que requiere puerto cuando no se proporciona
un archivo de configuración:

```yaml
arg-defs:
  definitions:
    config:
      type: string
      scope: preset
      rules:
        - when:
            present: false
          requires:
            port:
              present: true
    port:
      type: integer
      scope: preset
presets:
  ejemplo:
    args:
      port: 9931
```

| Args efectivos del preset | ¿Se activa la regla de config? | Resultado |
| --- | --- | --- |
| `{config: './server.conf'}` | No: config está presente. | Válido para esta regla; no exige port. |
| `{port: 9931}` | Sí: config está ausente. | Válido: port está presente. |
| `{}` | Sí: config está ausente. | Error: también falta port. |
| `{config: ''}` | No: string vacío cuenta como presente. | Válido para esta regla; no acredita que el backend acepte ese archivo. |

Estos tres bloques son alternativas para los `args` del preset `ejemplo` anterior:

```yaml
# Válido para esta regla: config presente, no se exige port.
args:
  config: ./server.conf
```

```yaml
# Válido: config ausente activa la regla y port satisface requires.
args:
  port: 9931
```

```yaml
# Inválido: config ausente activa la regla y también falta port.
args: {}
```

La regla pertenece a `config`, pero se evalúa aunque no se haya escrito ese
argumento. Al componer `{config: null}` sin ninguna asignación efectiva para port, el
resultado también falla por falta de port. Si `when` se omite, la regla sólo se
activa cuando el argumento propietario está presente.

Para un argumento booleano, `public: false` satisface `{present: true}` y
`{equals: false}`; no satisface `{present: false}`. Un flag false puede omitirse
al emitir CLI y seguir estando presente durante la validación: presencia y
emisión son comprobaciones distintas.

## Emisión determinista

`emit.cli` admite `name` (nombre sin --), `mode` (value, flag, repeat o join),
`separator` (texto para join), `true-value`, `false-value` y `negative-name`.
`emit.ini` admite las mismas claves salvo negative-name; name es clave INI exacta.
Cada destino declarado requiere mode explícito; name omitido conserva el nombre canónico.
Un destino ausente usa el adaptador conocido o emisión libre; no se deriva un
nombre INI desde un alias CLI de un fork. `flag` sólo admite boolean; `repeat` y
`join` sólo array; `value` un escalar. True/false-value son strings para boolean
con mode value; negative-name sólo permite flag false en CLI. Se rechazan
combinaciones de campos inaplicables. No hay aridad implícita para arrays.

Sin schema/adaptador, escalar string/número emite `--clave` y un token de valor;
true un flag y false nada. Para INI, escalar emite `clave=valor`, true `clave=true`
y false se omite; nombres contienen guiones si son la clave declarada. Listas
desconocidas exigen regla de emisión: no se adivina repetir ni concatenar. Modo
permisivo no garantiza poder representar cualquier forma de dato en un backend.

La emisión usa nombres canónicos ordenados ordinalmente; arrays conservan orden.
Null desasigna durante composición y no se emite ni se convierte en string
`null`. Un valor `{literal: ...}` acepta sólo escalar string/número/booleano y
suprime interpretación de referencias/interpolación, sin cambiar el tipo.
Los otros mapas, objetos o listas con null son inválidos como args.
Números se formatean sin cultura ni separadores de miles; boolean con value exige
representación explícita o adaptador conocido, sin convertirlo a flag.

| Valor / emisión | Tokens CLI |
| --- | --- |
| ctx-size: 8192 | `--ctx-size`, `8192` |
| texto: "false" | `--texto`, `false` |
| metrics: true / flag | `--metrics` |
| metrics: false / flag sin negative-name | Ninguno. |
| tags: [a, b] / repeat | `--tags`, `a`, `--tags`, `b` |
| tags: [a, b] / join separator=, | `--tags`, `a,b` |

Repeat significa repetir la clave sólo si el parser objetivo acumula; si el
argumento reemplaza el valor anterior, la definición no puede declarar una lista
acumulativa válida. INI repeat también requiere soporte explícito de claves
repetidas del parser. Join rechaza un elemento que contiene separator salvo que
el adaptador pruebe un escape reversible; no se pierde información silenciosamente.

CLI pasa tokens separados mediante ArgumentList, sin shell, comillas añadidas ni
concatenación de cadenas. Un espacio dentro de ruta no crea otro token. NUL es
inválido; saltos de línea sólo se admiten si el destino sabe representarlos.
INI exige codec del adaptador del build: se rechazan sección/clave con CR/LF,
`]`, `=` u otros caracteres que puedan crear otra entrada; el codec representa
strings/rutas con espacios y caracteres especiales según su parser. Si un valor
no tiene representación reversible acreditada, se diagnostica sin generar otro
significado. No se usa escape de shell como escape INI.

Los campos `enable`, `warmup` y `name` nunca se emiten como
args del backend. Las referencias se resuelven a una ruta de entrada por unidad,
sin repetir shards. Un schema de argumentos no acredita este codec ni las APIs.
