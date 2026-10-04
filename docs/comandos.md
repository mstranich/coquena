# Comandos de la CLI

Propuesta de superficie de comandos del núcleo, al estilo de SDKMan, FNM o UV para la
gestión y de Docker para la ejecución. Los nombres son borrador.

## Convenciones

- `<familia>` es `cpu`, `vulkan`, `cuda@12`, `cuda@13`, … . En `cuda@12`, `12` es la
  versión mayor de las bibliotecas CUDA usadas para compilar el runtime, **no** el
  release de llama.cpp. Éste se expresa por separado como `<build>` (`b1002`).
  Cada backend registrado tiene además un **ID único y estable**. Un selector abreviado
  como familia + build sólo se acepta si identifica exactamente uno; `run` y
  `uninstall` admiten el ID para eliminar toda ambigüedad.
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
** register <ruta-llama-server> --family <familia> [--distribution <origen>]
** list
** uninstall <id-backend>
@endmindmap
```

### `model`

Registra modelos y companions, consulta su estado y gestiona su directorio.

```plantuml
@startmindmap
* model
** dir [<ruta>]
** add <ruta.gguf> [<alias>]
** companion
*** add <ruta.gguf> [<alias>] --type <tipo>
*** remove <alias>
** list
** refresh <alias-modelo>
** update [<alias-modelo>]
** upgrade [<alias-modelo>]
** remove <alias-modelo>
@endmindmap
```

Modelos y companions se seleccionan por su alias en el catálogo local, no mediante
búsquedas por nombre de archivo. Si se omite el alias al registrar, el nombre del archivo
se usa como alias inicial; un alias ya ocupado se rechaza, sin sobrescribir otro registro.

`model companion add` registra el archivo sin asociarlo a un modelo o preset.
`--type` declara su papel (por ejemplo, `mmproj` o `draft`). `remove` retira el registro
por alias: borra archivos gestionados, pero conserva los externos.

El registro de archivos externos no copia pesos, crea enlaces físicos ni consulta la
red. No requiere reconciliación con Hugging Face y queda fuera de las actualizaciones.
`update` consulta metadatos y guarda novedades; `upgrade` aplica la revisión detectada
sólo a gestionados. Las reglas están en [modelos-y-companions.md](modelos-y-companions.md).
La superficie de descarga, búsqueda asistida, verificación y actualización de companions
queda pendiente; no se presupone una reconciliación ni una descarga conjunta obligatoria.

### `param`

Consulta, importa y exporta el catálogo de parámetros y su disponibilidad.

```plantuml
@startmindmap
* param
** list [--version <build>]
** show <parámetro>
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
** set <param> --ref <alias> [ámbito]
** unset <param> [ámbito]
@endmindmap
```

`unset` elimina el par clave-valor propio del ámbito indicado, como `delete` en
JavaScript. No bloquea la herencia: si hay un valor heredado, vuelve a regir; si no,
la clave deja de emitirse en la sección correspondiente del INI. No modifica el padre.

Los parámetros que cargan companions aceptan referencias por alias a su registro en vez
de una ruta literal. Coquena conserva la referencia y la resuelve a una ruta absoluta al generar
el INI; CPU/GPU y demás ajustes se configuran con los parámetros del backend.
`config set <param> --ref <alias> [ámbito]` guarda una referencia al catálogo.
El valor posicional y `--ref` son alternativas excluyentes: un valor normal es literal,
aunque coincida con un alias. No se usa un prefijo especial como `@`.

Por ejemplo:

```text
coquena config set mmproj --ref vision --preset conversacion
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

### `server`

Registra la composición de presets que ofrecerá un router y sus opciones de lanzamiento.

```plantuml
@startmindmap
* server
** create <nombre> [--family F] [--port N]
** add <servidor> <preset>...
** remove-preset <servidor> <preset>
** list
** show <servidor>
@endmindmap
```

### `run`

Genera el INI e inicia una instancia de un servidor registrado.

```plantuml
@startmindmap
* run <servidor> [--backend <id-backend> | --family <familia> [--release <build>]] [--port N] [-d]
@endmindmap
```

### `ps`

Lista las instancias registradas y sus IDs.

```plantuml
@startmindmap
* ps
@endmindmap
```

### `logs`

Consulta los logs de una instancia; con `-f`, sigue su salida.

```plantuml
@startmindmap
* logs <id-instancia> [-f]
@endmindmap
```

### `stop`

Detiene la instancia identificada por su ID.

```plantuml
@startmindmap
* stop <id-instancia>
@endmindmap
```

## Correspondencias

| Coquena | Análogo | Nota |
|---|---|---|
| `backend install` | `sdk install java 21-tem`, `fnm install --lts` | sin argumentos elige familia por hardware y último release |
| `backend list` / `remote` | `sdk list java`, `fnm ls-remote` | |
| `model add` | `sdk install java x /ruta/local` | registra sin copiar |
| `config set` sin ámbito | configuración global | con `--family` o `--model`, el ámbito correspondiente |
| `run` / `run -d` | `docker run` / `docker run -d` | genera el INI y lanza `llama-server` |
| `ps`, `logs -f`, `stop` | `docker ps`, `docker logs -f`, `docker stop` | |

## Home

No hay comando `init`. El home se resuelve así (`COQUENA_HOME` primero, porque es una
decisión explícita del usuario):

```plantuml
@startuml
start
if (¿COQUENA_HOME definido?) then (sí)
  if (¿es ruta absoluta?) then (no)
    :Rechazar nombrando\nCOQUENA_HOME;
    stop
  endif
  :Usar COQUENA_HOME;
elseif (¿existe un home junto al ejecutable?) then (sí)
  :Modo portable:\nusar ese directorio;
else (no)
  :Usar ~/.coquena\n(crearlo en el primer uso);
endif
stop
@enduml
```

## Reglas

- `run` siempre ejecuta un **servidor registrado** con un **backend instalado**; no existe
  un lanzamiento ad hoc de un preset suelto.
- `run` devuelve un ID de instancia. `ps`, `logs` y `stop` se refieren a ese ID, no al
  nombre del servidor; varias instancias del mismo servidor pueden convivir sólo con
  puertos distintos. El puerto del servidor es el predeterminado y `--port` permite
  elegir otro para ese lanzamiento.
- `model remove` y `backend uninstall` borran los archivos **gestionados por Coquena**
  junto con su registro e informan los bytes liberados. Si el registro es externo,
  quitan sólo el registro y avisan que los archivos permanecen en disco. La misma regla
  se aplica a `model companion remove`.
