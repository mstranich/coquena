# Formato del proyecto

Una carpeta con `coquena.yaml` es un proyecto. El nombre del archivo es lowercase.
La receta define un servicio, un consumidor o ambos. El estado del proyecto vive
en `.coquena/`; la importación global usa `COQUENA_HOME`.
No se declara un campo de versión del formato. Se rechazan claves estructurales
desconocidas, claves duplicadas y documentos mal formados; el modo permisivo sólo
afecta nombres de argumentos, no la estructura del documento.

## Receta

Claves raíz admitidas: `name`, `target`, `args-mode`, `service`, `arg-defs`,
`models`, `companions` y `presets`. No hay bloques raíz `backend`, `server` o
`project`. `service` admite exclusivamente `backend`, `server` y `recovery`;
`service.server` admite `args-mode`, `args`, `logs`, `hooks` y `health`.
El bloque service es un objeto único opcional, nunca una colección: una receta
no puede definir más de un servicio. Su backend es obligatorio en la definición
efectiva, aunque un override pueda omitirlo para heredarlo. Un documento vacío
equivale a `{}` para editar; no alcanza para iniciar o publicar presets.

`name` es el único nombre de receta y también identifica el servicio que ésta
defina. Omitido, se deriva de la carpeta al primer registro. Prefija los nombres
publicados de presets como `name/preset`, sin hacerlos hijos de service ni alterar
las claves de modelos, companions o parámetros. Service y presets son entidades
de primer nivel con registro independiente. `.coquena/local/` conserva los nombres
utilizados; cambiar carpeta o YAML no renombra entidades existentes.
`target` selecciona el servicio donde se publican los presets, independientemente
del servicio que pueda definir la propia receta:

| Selección | Destino |
| --- | --- |
| `target: global` | Servidor global predeterminado. También se usa al omitir target. |
| `target: self` | Único servicio definido en esta receta; requiere que exista el bloque service. |

El campo target sólo acepta los strings lowercase `global` y `self`. Un nombre
de servidor, `default` o cualquier otro valor se rechazan. Global identifica el
ámbito y default designa al servicio elegido dentro de ese ámbito; un servicio
propio de proyecto no puede ser el default global. La elección del default se
administra globalmente, sin escribir su nombre en la receta consumidora.

Seleccionar un destino no transfiere la propiedad de los presets al servicio ni
lo inicia por sí solo. La primera preparación del destino global sigue la
[provisión del servidor global](servidores.md#servidor-global-predeterminado).
Un alias incoherente se diagnostica sin elegir un sustituto.

Nombres, importación y ámbito en [servicios y recetas](servicios-recetas.md).
Cada preset admite `enable`, `warmup`, `args-mode` y `args`; modelos admiten
`repository`, `revision`, `quantization` y `files`; companions añaden `type`.
`repository` exige `publicador/repositorio`; revision omitida usa la revisión
predeterminada del repositorio, que se fija a commit al resolver. `quantization`
y `files` remotos explícitos son alternativas, no filtros acumulados.
Sin repository, files es una lista no vacía de rutas locales y no admite revision
ni quantization. No mezclar rutas locales y remotas en una unidad.
Con repository se requiere quantization o files para seleccionar la unidad.
Companion requiere type según los roles del [catálogo de recursos](modelos-y-companions.md).

| Bloque | Campos y tipos |
| --- | --- |
| service.backend | version y variant (strings del oficial), o executable (ruta string), excluyentes. |
| service.server.args | Mapa de argumento a escalar, lista tipada o envoltura literal, según parámetros. |
| service.server.logs | directory (ruta string), retention con max-bytes (entero positivo). Default en estado del servicio y retención global. |
| service.server.hooks | start-failed con command (ruta string) y args (lista de strings, default vacía). |
| service.server.health | url (string), timeout-seconds (entero positivo) e interval-ms (entero); defaults/plazos en servidores. |
| service.recovery | allow-draft-last-good (booleano, default false): permite promover borrador aplicado y sano, sin convertir guardado en evidencia. |
| arg-defs | imports (lista de rutas JSON) y definitions (mapa); defaults vacíos. |

Los mapas estructurales no admiten claves fuera de su vocabulario. Ningún campo
con default se materializa en YAML sólo por leerlo; la validación efectiva usa
defaults sin escribir intención. Un servicio no tiene varios backends efectivos.

## Lectura YAML

Se acepta un documento YAML 1.2 por archivo, mapas con claves string únicas,
secuencias y escalares. Se rechazan tags personalizados, anchors, aliases, merge
keys `<<`, claves complejas y documentos múltiples; no ejecutan constructores.
Booleanos son `true`/`false`; `yes`, `no`, fechas y `on` se tratan como texto.
Números usan decimal independiente de la cultura; NaN e infinito son inválidos.
Strings conservan comillas/tipo de origen: `"8192"` no pasa a entero por validación.
`null`, `~` y valor vacío desasignan; `""` sigue siendo texto vacío.

La estructura de cada capa se valida antes de combinar; los requisitos efectivos
se validan después. Se rechaza una clave desconocida aunque otra capa la retire.
Un límite de lectura produce diagnóstico, nunca un documento truncado aceptado:
16 MiB por archivo, profundidad máxima 64, 100 000 nodos y 64 capas YAML o env.
La edición puntual preserva comentarios y texto ajenos a la asignación; si el
parser no puede localizar una edición inequívoca, no reserializa todo el archivo:
informa la ubicación para edición manual. Se releen bytes antes de reemplazar.

Ejemplo estructural: repositorios y archivos ilustrativos, no receta descargable.

```yaml
name: ejemplo
target: self
args-mode: permissive

service:
  backend:
    version: latest
    variant: cuda@12
  server:
    args:
      host: "127.0.0.1"
      port: 9931
      metrics: true
      models-max: 2
    logs:
      directory: ./logs
      retention:
        max-bytes: 104857600
    hooks:
      start-failed:
        command: ./scripts/notificar.exe
        args: ["fallo-de-inicio"]

arg-defs:
  imports:
    - ./schemas/llama-server.json
  definitions:
    ctx-size:
      type: integer
      minimum: 0
      scope: preset

models:
  principal:
    repository: publicador/modelo-GGUF
    revision: main
    quantization: Q4_K_M
  local:
    files:
      - ./weights/modelo-local.gguf

companions:
  multimodal:
    type: mmproj
    files:
      - ./weights/mmproj.gguf

presets:
  conversacion:
    enable: true
    warmup: false
    args:
      model: model://principal
      mmproj: companion://multimodal
      ctx-size: 8192
  experimental:
    enable: false
    args-mode: permissive
    args:
      model: model://local
      opcion-experimental: fast
```

`args-mode` acepta `permissive` (default) o `strict`. La raíz establece el default
del proyecto; cada bloque con `args` puede sobrescribirlo mediante un campo hermano.
`enable` vale true por defecto. Un preset deshabilitado conserva su definición,
pero no se prepara ni publica; sus referencias no exigen preparar recursos por ese uso.

`warmup` es un booleano del preset, hermano de `args`, y vale false por defecto.
Declara precarga: tras publicar una entrada nueva o modificada, o al iniciar el
servicio que la ofrece, true solicita cargar su modelo sin esperar una petición.
False publica sin solicitar carga; no descarga una entrada que ya esté cargada.
Un preset deshabilitado no se publica ni precarga, aunque declare warmup true.
Consultar estado no vuelve a ejecutar la precarga. No garantiza residencia permanente
ni que todos los presets que la solicitan quepan simultáneamente en memoria.

Este campo de Coquena no se emite como el argumento `--warmup` de llama.cpp:
ese argumento controla una pasada inicial de inferencia, no la publicación ni la
orden de cargar una entrada. Carga usa RAM y, según configuración y backend, VRAM;
no equivale a colocar necesariamente todos los pesos en GPU.
Publicación, carga y diagnóstico siguen el contrato de
[servidores](servidores.md#aplicación-dinámica-y-memoria).

## Backend

`version` y `variant` seleccionan exclusivamente el backend oficial. La variante
conserva su calificador, como `cuda@12` o `cuda@13`. Son filtros de selección, no
órdenes de volver a descargar recursos ya preparados válidos. El lock fija el build.
No hay repositorio configurable de backends.

La alternativa local es excluyente:

```yaml
service:
  backend:
    executable: ./bin/llama-server.exe
```

El ejecutable aportado admite ruta relativa a la base o absoluta; conserva
propiedad del usuario y no admite filtros, seguimiento, update ni upgrade gestionados.
No se infiere compatibilidad con INI, router o APIs por su nombre o por su catálogo
de argumentos. Las capacidades se comprueban según [servidores](servidores.md).
Cada capa que declara `service.backend` reemplaza ese bloque completo; las otras
claves de `service` se combinan por clave.

## Modelos, companions y referencias

Las claves de `models` y `companions` son nombres de referencia de la receta;
no son rutas ni nombres de preset. Se resuelven en el proyecto y conservan la
selección canónica y la identidad estable en inventario/resoluciones.
Para selecciones remotas son aliases: dos claves que resuelven la misma selección
concreta comparten registro e instalación física. Los aliases no crean propietarios
distintos de los archivos.

Para una declaración local sin `repository`, la clave es el nombre del registro:
`models.my-model.files: [./weights/modelo.gguf]` equivale al alta local
`model add ./weights/modelo.gguf --name my-model`. Se referencia mediante
`model://my-model`. En `companions`, la clave cumple la misma función y `type`
declara el rol auxiliar. Repetir el mismo nombre y unidad reutiliza el registro;
otro nombre para los mismos archivos colisiona, igual que un alta externa por CLI.
La receta compartida conserva nombres y rutas, no UUID locales. Una ruta relativa
requiere que quien recibe la receta aporte los archivos respecto de su propia base;
para obtener pesos desde un origen remoto se declara una selección remota.

Una selección remota declara `repository`, `revision` y `quantization` o `files`
remotos explícitos para desambiguar. Los shards necesarios forman una unidad;
no se descarga el repositorio completo. El commit y archivos concretos quedan en
la resolución. Un bloque con sólo `files` locales declara recursos aportados, sin
adopción ni redescarga implícitas. Rigen las [reglas de modelos](modelos-y-companions.md).

Un modelo remoto admite como forma corta un escalar `usuario/repositorio:cuantización`:

```yaml
models:
  principal: unsloth/gemma-4-12b-it-GGUF:Q4_0
```

Equivale a `repository: unsloth/gemma-4-12b-it-GGUF` y `quantization: Q4_0`,
con la misma resolución de revisión que el bloque largo cuando omite revision.
No selecciona automáticamente companions. La referencia del preset permanece
`model://principal`; el escalar no sustituye las referencias de recursos en args.
Para declarar revisión o archivos explícitos se utiliza el bloque largo.
La resolución concreta conserva commit, archivos y shards con las mismas reglas.

`args` contiene tanto literales como referencias `model://nombre` y
`companion://nombre`. Se resuelven a la ruta de entrada de la unidad antes de emitir
CLI/INI; no se expanden todos sus shards como argumentos separados. Un nombre sin
prefijo sigue siendo literal, salvo un selector canónico remoto completo
`usuario/repositorio:cuantización` o `usuario/repositorio#tipo:cuantización`.
Estos selectores pueden usarse directamente como valores de `args` de un preset,
sin declarar un alias en `models` o `companions`; identifican el mismo recurso
que una referencia a un alias de esa selección. No se infiere una referencia de
un repositorio sin cuantización. Los prefijos de aliases son `model://` y
`companion://`, en singular. Para escribir como texto un selector canónico o una
cadena con prefijo reservado se utiliza el envoltorio `literal`:

```yaml
args:
  texto:
    literal: "model://no-es-una-referencia"
```

No hay sección `bindings` ni campo especial de modelo principal en el preset:
`preset bind` escribe la referencia en el argumento correspondiente; `set` escribe
un literal. Una referencia inexistente es error incluso en modo permisivo.

Las claves de modelos/companions locales son relativas al contexto del proyecto:
`model://principal` identifica su modelo `nombre-proyecto/principal`, sin exigir
escribir ese prefijo en el YAML. La importación global lo promociona a `principal`.
Las publicaciones remotas mantienen su identidad remota. Las reglas de nombres y
promoción se describen en [servicios y recetas](servicios-recetas.md#importación-global).

Un companion no equivale a una modalidad. El `mmproj` de llama.cpp puede contener
componentes de visión y audio en el mismo archivo. Su argumento recibe una sola
ruta: repetirlo reemplaza el valor, no acumula proyectores. No se admite una lista
para ese parámetro en su definición oficial. Las listas de otros parámetros siguen
su contrato específico de emisión; Coquena no fusiona proyectores.

Evidencia del comportamiento actual, que debe contrastarse con el build elegido:
[parser](https://github.com/ggml-org/llama.cpp/blob/master/common/arg.cpp) y
[carga multimodal](https://github.com/ggml-org/llama.cpp/blob/master/tools/mtmd/mtmd.cpp).

## Capas e interpolación

`-f/--file` selecciona capas ordenadas. Sin selección se usa `coquena.yaml`.
La base es la carpeta del primer archivo, salvo base explícita. Todas las rutas
relativas parten de esa base. No se detectan overrides automáticamente.

Los mapas se combinan por clave, gana la última asignación; las listas se reemplazan
completas, sin concatenación implícita. Omisión conserva lo heredado; null desasigna
una clave o entrada completa, sin borrar archivos. `service.backend` es la excepción atómica
indicada arriba. Los presets admiten modificaciones parciales por nombre.

La interpolación es una funcionalidad de Coquena, no de YAML: un lector YAML
ordinario ve `${VAR}` como texto. Coquena procesa estas expresiones después de
leer los valores YAML, sin modificar la sintaxis del lenguaje.
Se usa la sintaxis de [interpolación de Compose](https://docs.docker.com/reference/compose-file/interpolation/):
`$VAR`, `${VAR}`, `${VAR:-default}`, `${VAR-default}`, `${VAR:?error}`,
`${VAR?error}`, `${VAR:+replacement}`, `${VAR+replacement}` y `$$` literal.
Los operadores con `:` distinguen también el valor vacío. Se interpola por archivo,
antes de combinar, sólo sobre valores, no sobre claves; no se ejecuta shell.

Los `.env` se seleccionan explícitamente en orden; el último prevalece y el entorno
del proceso tiene prioridad. A diferencia de Compose, una variable ausente sin
default produce error, según la regla de seguridad de [definiciones](definiciones-declarativas.md).
Las rutas admiten valores absolutos y relativos, también producidos por expresiones.
Cada comando que trabaja con la receta evalúa las variables que necesita; importar
no convierte sus valores expandidos en literales de intención. Una ruta resuelta o
una última ubicación conocida en el estado local no reemplaza esa evaluación.
La interpolación no debe interpretar un resultado como una nueva estructura YAML.
La expansión sin tipo explícito conserva tipo string, aunque el campo o schema
espere un número o booleano. El sufijo de Coquena `|string`, `|integer`, `|number`
o `|boolean`, inmediatamente antes del cierre, indica y valida el tipo del resultado:
`${CTX|integer}`, `${CTX:-4096|integer}`, `${METRICS:-true|boolean}`.
Integer exige notación entera sin punto decimal ni exponente; number admite
notación entera, decimal y científica con valor finito. Boolean acepta únicamente
`true` y `false`, sin convertir `0` y `1` a booleanos. Un valor inválido produce error.
El tipo debe pertenecer a una única expansión que ocupe todo el escalar; no se
admite en expresiones anidadas ni en fragmentos como `prefijo-${CTX|integer}`.
El carácter `|` dentro de una expansión queda reservado para el sufijo de tipo.
No se adivinan listas u objetos desde texto. El resultado tipado se valida después
contra el campo y el schema, sin conversiones adicionales para satisfacerlos.

Interpolación se aplica a strings simples o dobles; comillas simples y el bloque
`literal` conservan el texto sin expansión. El sufijo explícito también se interpreta
dentro de comillas dobles; las comillas permiten expresar la expansión en un mapa
YAML compacto. Sin sufijo, tanto simples sin comillas como dobles conservan string.
En `.env` los valores almacenados siguen siendo texto; un sufijo, si se usa, valida
su contenido, pero el uso en YAML declara su propio tipo. Un resultado no se vuelve a interpolar.
El schema, incluidas las alternativas `any-of`, valida el tipo resultante; no
elige una conversión ni prueba otros tipos para volver válida una asignación.
Expresiones anidadas en defaults/reemplazos se evalúan hasta profundidad 32.
`$(` no ejecuta nada ni forma una expansión de variable.

### Ejemplos de interpolación tipada

Con `ETIQUETA=00123`, `CTX=8192`, `TEMP=0.7` y `METRICS=false` en el entorno:

```yaml
presets:
  ejemplo:
    args:
      etiqueta: ${ETIQUETA}
      ctx-size: ${CTX|integer}
      temperature: ${TEMP|number}
      metrics: ${METRICS|boolean}
      descripcion: "contexto-${CTX}"
      texto-literal: '${CTX|integer}'
```

`etiqueta` conserva el string `00123`, `ctx-size` es el entero `8192`,
`temperature` es el número `0.7` y `metrics` es el booleano false.
`descripcion` es el string `contexto-8192`; `texto-literal` conserva literalmente
`${CTX|integer}` por sus comillas simples. Los argumentos son ilustrativos;
su aplicabilidad y emisión dependen del schema y del backend seleccionados.

| Expresión | Valor de variable | Resultado |
| --- | --- | --- |
| `${CTX}` | `8192` | String; no satisface una definición integer. |
| `${CTX\|integer}` | `8192` | Entero. |
| `${CTX\|integer}` | `8192.0` o `8.192e3` | Error: no es notación entera. |
| `${CTX\|number}` | `8192.0` o `8.192e3` | Número. |
| `${METRICS\|boolean}` | `false` | Booleano false. |
| `${METRICS}` | `false` | String `false`; no es orden de omisión de un flag. |
| `${METRICS\|boolean}` | `0`, `1` o `sí` | Error: booleano inválido. |
| `${CTX:-4096\|integer}` | Variable ausente o vacía | Entero `4096`. |
| `${CTX\|integer}` | `texto` | Error antes de preparar o ejecutar. |

Si una definición admite `any-of` con string e integer, `${CTX}` conserva string
y `${CTX|integer}` produce integer; el orden de las alternativas no cambia el
resultado. Una definición que exige integer rechaza `${CTX}` aunque contenga
dígitos. Las comillas dobles no anulan un sufijo explícito:
`ctx-size: "${CTX|integer}"` también produce un entero.

### Archivos de variables

Los `.env` usan UTF-8, líneas vacías/comentarios y `NOMBRE=valor`, con nombre
`[A-Za-z_][A-Za-z0-9_]*`; `NOMBRE=` declara vacío y `NOMBRE` solo declara ausencia.
El último valor gana, incluso dentro de un archivo. Comentario inline sin comillas
requiere un espacio antes de `#`; comillas simples conservan literal y admiten
multilínea, dobles admiten `\n`, `\r`, `\t`, `\\` y `\"`. Valores simples/dobles
pueden interpolar variables ya definidas y entorno del proceso; no hay referencias
adelantadas ni ejecución de shell. Entorno del proceso tiene prioridad también
si define una cadena vacía. No se modifica el entorno persistente del usuario.

Ejemplo de override sobre la receta anterior:

```yaml
service:
  backend:
    executable: ./bin/llama-server.exe
presets:
  conversacion:
    args:
      ctx-size: ${CTX:-4096|integer}
      mmproj: null
  experimental: null
```

Con `CTX=16384`, ctx-size se valida como entero por su definición; mmproj y el
preset experimental desaparecen. El backend pasa a externo sin heredar version
o variant. Un `.env` con `CTX=texto` falla por tipo; `${TOKEN:?falta TOKEN}` falla
si está ausente o vacío. Diagnósticos indican archivo, línea/columna, ruta lógica,
fase y código estable; una variable ausente indica su nombre, nunca su contenido.
No se imprime el valor expandido de una variable en mensajes o diffs compartibles.

## Índices, resolución y estado local

```text
.coquena/
  .gitignore
  combinations.json
  combinations/<id>/
    resolved.json
    local/
      last-good.json
      update-plan.json
  local/
    recipes.json
    combinations.json
    active.json
  models/
  backends/
  downloads/
  tmp/
  logs/
```

Los índices describen las combinaciones y permiten encontrarlas sin explorar cada
carpeta. El índice compartible incluye sólo selecciones compartibles; el local
registra selecciones privadas con el mismo formato. El ámbito global mantiene sus índices separados, sin mezclar inventarios.

Ejemplo de índice; la clave ilustrativa representa el hash calculado:

```json
{
  "kind": "combination-index",
  "format": 1,
  "revision": 1,
  "data": {
    "combinations": {
      "<sha256>": {
        "files": ["coquena.yaml", "coquena.cpu.yaml"],
        "env-files": []
      }
    }
  }
}
```

El ID se obtiene de una serialización determinista de las listas ordenadas de
rutas relativas normalizadas de YAML y `.env`, con `/` como separador y sin `./`
redundantes. No incluye contenido ni rutas absolutas. Cambiar orden o renombrar un
archivo cambia el ID; mover la carpeta o editar contenidos no. Una huella separada
detecta cambios de intención efectiva. No se guardan secretos expandidos ni huellas
que permitan comprobar secretos débiles por diccionario en documentos compartidos.

`resolved.json` conserva la resolución concreta de la combinación, recursos y
procedencia portable; no certifica arranque. `last-good.json` conserva un snapshot
independiente que pasó salud en esta máquina: nunca se reconstruye desde el
`resolved.json` actual. `active.json` identifica la instancia del servicio propio
y su combinación. Índices y resoluciones se publican con coordinación recuperable,
sin referencias a documentos parcialmente escritos.

Git ignora `local/`, `combinations/*/local/`, `models/`, `backends/`, `downloads/`,
`tmp/` y `logs/`. Los companions preparados viven con los pesos en `models/`.
Los `resolved.json` son compartibles, no obligatoriamente versionados: también se
excluyen los correspondientes a combinaciones privadas. `.env` y secretos quedan
excluidos por defecto. Compartir una resolución no comparte éxito local.

## Inicialización

`coquena init` crea como mínimo `coquena.yaml`, `.coquena/` y su `.gitignore`, y
complementa las exclusiones del proyecto sin reemplazar reglas existentes. La receta
mínima puede ser `{}`: define un proyecto editable, no uno listo para iniciar.
No descarga, inicia ni ejecuta Git init. Preserva archivos existentes e informa
conflictos; repetirlo no reinicia estado ni sobrescribe definiciones.

Índices, resoluciones, inventarios, directorios de recursos y registros de ejecución
se materializan cuando hacen falta. Init no fabrica `resolved.json`, `last-good.json`
ni una instancia activa. La ausencia inicial de esos documentos es válida.

## Correspondencia con operaciones

Backend/model/companion/preset/param mantienen las secciones declarativas. Clonar
materializa un preset independiente. Up/down, apply, transferencias,
verify, update/upgrade y limpieza son operaciones; no se guardan como instrucciones
de ejecución en la receta. `config` sigue siendo global del usuario.

La composición lee y combina archivos para obtener una configuración efectiva;
no es una interfaz de edición. La receta puede redactarse con un editor externo,
consultarse y validarse sin descargar ni iniciar. Los comandos de configuración
persisten cambios puntuales; no requieren un editor completo incorporado.

Serialización exacta de identidad, inventario, envolturas JSON, layout global y
protocolo recuperable en [persistencia](persistencia-proyectos.md).
