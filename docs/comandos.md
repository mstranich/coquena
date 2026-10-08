# Comandos de la CLI

Superficie acordada del núcleo, al estilo de SDKMan, FNM o UV para la gestión y de
Docker para la ejecución. El alcance aplazado y las excepciones por resolver se
identifican en la lista central; no son requisitos implícitos.

## Convenciones

- `<familia>` es `cpu`, `vulkan`, `cuda@12`, `cuda@13`, … . En `cuda@12`, `12` es la
  versión mayor de las bibliotecas CUDA usadas para compilar el runtime, **no** el
  release de llama.cpp. Éste se expresa por separado como `<build>` (`b1002`).
  Cada backend registrado tiene además un **ID único y estable**. Un selector abreviado
  como familia + build sólo se acepta si identifica exactamente uno. Servidores seleccionan
  por origen/variante:release o nombre externo; `uninstall` conserva la selección por ID.
  Sin familia, Coquena elige automáticamente una compatible con el hardware; sin
  release, `latest` significa **el build más reciente que tenga un binario compatible
  dentro del filtro indicado**. Primero se compara el build y sólo entre candidatos
  del mismo build se prefiere, en NVIDIA, la familia CUDA con la versión de bibliotecas
  más alta que resulte compatible; después Vulkan. CPU es el fallback portable de la
  arquitectura nativa. Así, CPU `b10010` gana a CUDA `b10009` sin filtro, pero
  filtrar por CUDA con `latest` elige CUDA `b10009`. Coquena informa la elección por
  defecto y enumera las alternativas. La preferencia no garantiza rendimiento; el
  usuario siempre puede elegir explícitamente otra familia.
- La selección automática considera sólo candidatos `compatible`. En la plataforma
  correcta, un candidato con aceleración `incompatible` o `unknown` puede elegirse
  explícitamente; ambos quedan fuera de la elección automática, pero se muestran con
  diagnósticos distintos. La elección explícita no cambia su estado de compatibilidad.
  Un binario de otro SO o arquitectura se rechaza siempre: Coquena no descarga algo
  que no podrá instalar para uso local. Toda instalación o registro exige además un
  `llama-server --version` local válido.
- En el núcleo, la selección automática usa sólo la distribución oficial. Los
  backends externos son obtenidos y registrados por el usuario, y se eligen
  explícitamente para ejecutar. Repositorios de terceros como fuentes de descargas
  quedan para una iteración posterior. Al registrar uno externo, el usuario declara
  su familia; `--version` aporta la versión del ejecutable, no confirma esa familia.
  También se declara la distribución de origen para asociar su catálogo de parámetros:
  `--distribution oficial` (implícito) o el repositorio de un tercero. Esto no habilita
  descargas desde ese repositorio ni verifica la procedencia del binario externo.
- El núcleo inicial **no ofrece `--json`**. La salida JSON por comando se reconsiderará después de un milestone funcional. Si
  hiciera falta una interfaz programática, se prefiere evaluar un servidor HTTP.

## Mapa por comando

Todos los comandos se invocan con el prefijo `coquena`, omitido en los diagramas.

Los flujos de selección y confirmación son **interactivos**. No se incorpora un modo
automático para omitir sus menús. Si no hay entrada interactiva y el flujo necesita
una respuesta, informar el requisito y terminar sin decidir por el usuario ni ejecutar
la acción pendiente. Un registro local ya concretado antes de un emparejamiento se
conserva; la falta de interacción no revierte esa alta.

Las descargas y reanudaciones de modelos, companions y backends ocupan la terminal
en primer plano. Ctrl+C pausa sin borrar parciales y muestra el GUID de la operación
y `coquena download resume <guid>` antes de devolver el prompt. No equivale a
`download cancel`, que solicita limpiar los parciales.

### `system`

Inspecciona el hardware y la plataforma para informar las opciones de ejecución.

```plantuml
@startmindmap
* system
** (inspecciona hardware)
@endmindmap
```

### `backend`

Consulta, instala y registra backends de llama.cpp; también permite retirarlos.

```plantuml
@startmindmap
* backend
** remote [<familia>] [--release <build>|latest]
** install [<familia>] [--release <build>|latest]
** register <ruta-llama-server> --name <nombre> --family <familia> [--distribution <origen>]
** use [<selector>|<nombre-externo>]
** update [<selector-latest>]
** upgrade [<selector-latest>]
** list
** uninstall <id-backend>
@endmindmap
```

Repetir install fuerza descarga y reinstalación; intención latest/pin y publicación
siguen [servidores.md](servidores.md) y [descargas.md](descargas.md).

Update y upgrade sin selector operan sobre todos los seguimientos latest registrados;
con selector (por ejemplo ggml/cuda@12:latest), sólo sobre ese seguimiento. Releases
fijados y externos quedan excluidos. Update consulta y guarda novedades; upgrade aplica
el build detectado, sin seleccionar silenciosamente uno nuevo. El recorrido es por
unidad: un fallo no impide tratar las demás. No se deduce el alcance del servidor default.

### `model`

Registra y descarga modelos y companions, y consulta su estado.

```plantuml
@startmindmap
* model
** add <ruta.gguf> --name <nombre-canónico> [--try-match]
** download <usuario/repositorio[:cuantización]>
** companion
*** add <ruta.gguf> --name <nombre-canónico-con-tipo> [--try-match]
*** download <usuario/repositorio#tipo[:cuantización]>
*** list
*** refresh <nombre>
*** verify <nombre> [--set-baseline]
*** update [<nombre>]
*** upgrade [<nombre>]
*** remove <nombre>
** list
** refresh <nombre-modelo>
** verify <nombre-modelo> [--set-baseline]
** update [<nombre-modelo>]
** upgrade [<nombre-modelo>]
** remove <nombre-modelo>
@endmindmap
```

La sintaxis del nombre es `usuario/repositorio[#tipo]:cuantización`; `#tipo` sólo
aparece en companions. No hay aliases ni `--type`. Las reglas de identidad están en
[modelos-y-companions.md](modelos-y-companions.md#identificación-en-el-catálogo-local).
`--name` es obligatorio para externos; no certifica procedencia.
`--try-match` es opcional, sin valor. Sin él no se consulta la red; si falla, el alta
local permanece. Emparejar no habilita actualizaciones del externo.
Si hay candidatos, un menú muestra opciones y razones y permite elegir o cancelar
el empate. Cancelar conserva el registro local; elegir guarda sólo equivalencia informativa.
`update` guarda novedades y `upgrade` aplica la revisión detectada sólo a gestionados.

`model download usuario/repositorio[:cuantización]` selecciona una unidad completa.
Omitir cuantización conserva el menú acordado; al elegirla se determina el nombre
canónico. No se descargan companions como efecto secundario.
Si ese nombre está ocupado por un externo, ofrecer su transición explícita a gestionado;
publicar sólo tras verificar la descarga, conservando ID y referencias y sin tocar los
archivos externos. Cancelar o fallar mantiene el externo. Otras colisiones se rechazan.
Antes de crear la operación presenta un único menú repetible:

- **Con cuantización:** descargar la selección indicada, mostrar README o cancelar.
- **Sin cuantización:** una opción «Descargar <variante>» por candidato, mostrar README
  o cancelar. Elegir una variante inicia la descarga, sin una segunda confirmación.
- Mostrar README consulta la documentación del repositorio en la revisión resuelta y
  vuelve al mismo menú. No descarga pesos; si falla o no existe, informa y vuelve.
- Cancelar en este menú no crea GUID, trabajo ni parciales; no es Ctrl+C de una descarga
  activa. Una cuantización inexistente se informa, sin sustituirla automáticamente.
- Si una etiqueta identifica varias variantes, mostrar sus diferencias para una selección
  explícita; no adivinar. Etiquetar cada opción de inicio como descarga, no como inspección.

El README se consulta sólo al solicitarlo. No se descarga por defecto con los pesos.
Si el repositorio existe pero no ofrece GGUF seleccionables para la unidad solicitada,
mostrar sólo «Mostrar README» y «Cancelar», explicando la ausencia de candidatos.
No ofrecer descarga ni crear GUID, operación o parciales. La misma regla aplica a
companions cuando no hay candidatos del tipo indicado.
No encola companions. `model companion download` es otra invocación independiente,
incluso otro día. Usa `usuario/repositorio#tipo[:cuantización]` y el mismo menú de
selección, README y cancelación, limitado al tipo auxiliar indicado. Descargarlo no
descarga el principal ni lo asocia automáticamente a un preset. `list`, `refresh`,
`update`, `upgrade` y `remove` aplican las mismas reglas que en modelos, sobre companions.
Sin nombre, `model update` revisa todos los gestionados del catálogo, incluidos companions,
y `model upgrade` aplica sus planes resueltos; los subcomandos de `companion` limitan
el alcance a companions. Externos no reciben seguimiento ni actualización. Los problemas
de una unidad se informan sin impedir tratar las demás; no hay publicación global atómica.
`model verify <nombre>` y `model companion verify <nombre>` leen todos los archivos
de la unidad y comparan SHA-256 completos con las referencias registradas, sin red.
No reparan ni reemplazan referencias. Sin referencia, informan que no pueden verificar;
no establecen una base automáticamente. Reglas en
[integridad local](modelos-y-companions.md#integridad-local).

En externos, `verify <nombre> --set-baseline` calcula y registra SHA-256 de los bytes
actuales como base local. Advierte que no certifica procedencia ni corrección original.
Es una acción explícita distinta de comparar; sin el flag nunca reemplaza la referencia.
El flag no se aplica a gestionados, cuya referencia se establece durante la descarga.

### `download`

Consulta y controla operaciones por GUID. Estados, persistencia y recuperación se
definen en [descargas.md](descargas.md); no inicia selecciones nuevas.

```plantuml
@startmindmap
* download
** list [--model | --companion | --backend]
** status [<guid>]
** pause <guid>
** resume <guid>
** cancel <guid>
@endmindmap
```

`download list` muestra todas las operaciones y admite filtros excluyentes `--model`,
`--companion` o `--backend`: principales, companions o backends respectivamente.
El detalle conserva progreso por archivo. Pause, resume y cancel requieren el GUID
del conjunto; no hay control individual de archivos en el alcance inicial.
Operaciones terminadas y limpiadas desaparecen de estas vistas; su resultado queda en el log.

### `param`

Consulta, importa y exporta el catálogo de parámetros y su disponibilidad.

```plantuml
@startmindmap
* param
** list [--backend <id-backend> | --version <release> [--distribution <origen>]]
** show <parámetro> [--backend <id-backend> | --version <release> [--distribution <origen>]]
** import <archivo> [--backend <id-backend>]
** export [<archivo>] [--backend <id-backend>]
@endmindmap
```

### `config`

Consulta y modifica valores de configuración globales o de un ámbito específico.

```plantuml
@startmindmap
* config
** show [--family F | --model M | --preset P]
** set <param> <valor> [ámbito]
** set <param> --ref <nombre> [ámbito]
** unset <param> [ámbito]
@endmindmap
```

`unset` elimina el par clave-valor propio del ámbito indicado, como `delete` en
JavaScript. No bloquea la herencia: si hay un valor heredado, vuelve a regir; si no,
la clave deja de emitirse en la sección correspondiente del INI. No modifica el padre.

Los parámetros que cargan companions aceptan referencias por nombre canónico a su registro en vez
de una ruta literal. Coquena conserva la referencia y la resuelve a una ruta absoluta al generar
el INI; CPU/GPU y demás ajustes se configuran con los parámetros del backend.
`config set <param> --ref <nombre> [ámbito]` guarda una referencia al catálogo.
El valor posicional y `--ref` son alternativas excluyentes: un valor normal es literal,
aunque coincida con un nombre registrado. No se usa un prefijo especial como `@`.

Por ejemplo:

```text
coquena config set mmproj --ref ggml-org/Qwen3.8-27B-GGUF#mmproj:BF16 --preset conversacion
coquena config set temperature 0.7 --preset conversacion
```

Coquena no valida la compatibilidad funcional entre modelo y companion ni requiere
forzar su uso. El backend decide si puede cargar la combinación configurada.

### `preset`

Define configuraciones de ejecución de un modelo. Sus companions se eligen mediante
los parámetros de `config`, no con una lista de asociaciones separada.

```plantuml
@startmindmap
* preset
** create <modelo> <nombre>
** clone <preset> <nombre> [--model M]
** model set <preset> <modelo>
** list [--model M]
** show <preset>
** remove <preset>
@endmindmap
```

## Servidores e instancias

Reglas confirmadas y pendientes en [servidores.md](servidores.md).

### Sintaxis confirmada

~~~text
coquena backend use [<selector>|<nombre-externo>]

coquena server create <nombre> [--port <puerto>]
coquena server default [<nombre>]
coquena server [<nombre>] use default|<selector>|<nombre-externo>
coquena server [<nombre>] set <parámetro> <valor>
coquena server [<nombre>] unset <parámetro>
coquena server [<nombre>] preset add <preset>...
coquena server [<nombre>] preset remove <preset>...
coquena server [<nombre>] preset list
coquena server [<nombre>] start [-d]
coquena server [<nombre>] restart
coquena server [<nombre>] show
coquena server [<nombre>] remove
coquena server [<nombre>] logs [-f]
coquena server list
coquena server ps
coquena server stop <id-instancia>
~~~

Backend use configura el backend predeterminado de Coquena; sin selector lo muestra.
No descarga ni inicia: la selección debe resolver a una instalación registrada.
Server default designa un servidor existente; sin nombre muestra el predeterminado.
Coquena parte de un servidor default predeterminado. El primer backend instalado
correctamente mediante descarga propia se autoasigna como backend predeterminado,
sin sustituir una selección previa del usuario. Los presets siguen asignándose explícitamente.

Palabras reservadas tras server: create, default, list, ps, stop, use, set, unset,
preset, show, remove, logs, start y restart. Para preset, las acciones son add, remove
y list. Un token no reservado tras server identifica un servidor existente y su acción.
Los nombres de servidores no pueden coincidir con las reservadas, salvo default,
que también puede crearse explícitamente. Los argumentos se interpretan según su posición:
server create default crea ese servidor si el nombre está libre; server default default
lo designa como predeterminado si existe. No se interpretan esos argumentos como subcomandos.
La excepción de acción explícita se resuelve por el siguiente token:

- server default sin argumentos muestra la designación predeterminada.
- server default <nombre> designa un servidor existente (incluido default).
- server default seguido de use, set, unset, preset, show, remove, logs, start o restart
  actúa sobre el servidor llamado default, aunque otro sea el predeterminado.
- Un subcomando global como create, list, ps o stop no es una acción válida después
  del nombre default; no se reinterpreta como nombre de servidor para adivinar la intención.

Por ejemplo, server default start inicia el llamado default; server default lts designa
lts como predeterminado. Se interpreta la gramática antes de consultar entidades;
si el servidor default fue retirado, su acción informa que no existe y no lo recrea.
Omitir el nombre en use, set,
unset, preset, show, remove, logs, start o restart dirige la acción al servidor predeterminado. Create, default, list,
ps y stop tienen su significado propio; no operan implícitamente sobre ese servidor.

Server use default elimina la selección propia y recupera la herencia del backend
predeterminado de Coquena. Los selectores mantienen la distinción entre release fijado
y seguimiento latest. El selector admite origen/variante:release (o latest), con origen
oficial implícito si se omite. Un externo se selecciona por nombre, sin --backend.
Los selectores de gestionados no incluyen externos, aunque éstos declaren igual origen,
familia y release. Backend update/upgrade no siguen ni actualizan registros externos.
Las identidades y alcance de terceros están en [servidores.md](servidores.md#identificación-de-backends).

Start utiliza la definición guardada, sin overrides temporales de backend o parámetros;
-d sólo selecciona el modo. Start sin -d permanece en primer plano. Ps muestra instancias activas y métricas como
instantánea. Stop detiene sólo el ID indicado. No hay comando de guardar.
Restart aplica la configuración guardada y, si la selección sigue latest, el build actual
instalado por backend upgrade. No descarga al reiniciar. Hay una sola instancia activa
por servidor; un segundo start no crea otra. Restart inicia también un servidor detenido
y devuelve el prompt sin seguir logs; la nueva instancia queda en segundo plano.
Los antiguos comandos raíz run, ps y stop no son la superficie acordada.

Preset add asocia presets existentes; remove retira sólo las asociaciones, sin eliminar
los presets del catálogo. List muestra los asociados al servidor seleccionado. Los cambios
afectan al próximo inicio, no a instancias activas.

Show muestra la definición completa: selección de backend propia o heredada, build
instalado resuelto, parámetros efectivos y presets asociados. No inicia procesos ni
consulta actualizaciones remotas. Si falta una referencia/default, muestra el diagnóstico
sin inventar una selección.

Logs muestra salida reciente; -f sigue la salida. Ctrl+C termina únicamente el
seguimiento, sin detener el servidor. Se consulta la ejecución actual o, si está
detenido, la última ejecución. La retención se limita por tamaño configurable;
no confundir logs -f con start en primer plano.

Remove retira sólo la definición de un servidor detenido. Si era el predeterminado,
limpia la designación sin seleccionar otro ni recrear default. No elimina recursos asociados.

## Home

El objetivo es separar una ubicación de configuración de otra para los archivos pesados
gestionados (modelos, companions y backends). Configurar esas ubicaciones se abordará
en una iteración posterior, dentro de la configuración de Coquena, no del catálogo
de modelos ni de los parámetros de inferencia. No se define todavía su comando,
migración. La primera iteración usa un home común con la
[estructura acordada](implementacion-catalogo-modelos.md#almacenamiento-local-acordado).

No hay comando `init` ni autodetección de directorios junto al ejecutable. Si no se
define `COQUENA_HOME`, se utiliza `$USERPROFILE\.coquena` en Windows.
La variable puede redefinirse con una ruta absoluta para testing o uso portable;
por ejemplo, `$env:COQUENA_HOME = Join-Path $PWD '.coquena'` estando en el
directorio del ejecutable. La configuración es del proceso: Coquena no modifica
variables de entorno persistentes. El directorio se crea en el primer uso de almacenamiento.

```plantuml
@startuml
start
if (¿COQUENA_HOME definido?) then (sí)
  if (¿es ruta absoluta?) then (no)
    :Rechazar nombrando\nCOQUENA_HOME;
    stop
  endif
  :Usar COQUENA_HOME;
else (no)
  :Usar $USERPROFILE/.coquena\n(crearlo en el primer uso);
endif
stop
@enduml
```
