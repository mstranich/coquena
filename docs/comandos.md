# Comandos de la CLI

Propuesta de superficie de comandos del núcleo, al estilo de SDKMan, FNM o UV para la
gestión y de Docker para la ejecución. Los nombres son borrador.

## Convenciones

- `<familia>` es `cpu`, `vulkan`, `cuda12`, `cuda13`, …; `<familia>@<versión>` fija un
  build (`cuda13@b6123`). La identidad completa de un backend incluye también su
  **distribución**: esta notación abreviada sólo es inequívoca si hay una coincidencia.
  Antes de admitir varias distribuciones se definirá cómo seleccionarla sin ambigüedad.
  Sin familia, Coquena elige una instalada y compatible con el hardware; sin versión,
  la más reciente de esa selección. Un orden fijo de familias es sólo fallback, no
  sustituto de la evaluación de compatibilidad.
- Los comandos que **leen** estado aceptan `--json`; los que **actúan** no, y rechazan la
  opción antes de hacer nada (convención heredada de `legacy-go`, `docs/model-commands.md`).

## Mapa

```plantuml
@startmindmap
* coquena
** system
*** (inspecciona hardware)
** backend
*** remote [<familia>[@<versión>]]
*** install [<familia>[@<versión>]]
*** list
*** uninstall <familia>@<versión>
** distribution
*** list
*** add <nombre> <origen>
** model
*** dir [<ruta>]
*** add <ruta.gguf>
*** add-companion <ruta.gguf> --type mmproj|draft
*** list
*** refresh <modelo>
*** remove <modelo>
** param
*** list [--version <build>]
*** show <parámetro>
*** import <archivo>
*** export [<archivo>]
** config
*** show [--family F | --model M | --preset P]
*** set <param> <valor> [ámbito]
*** unset <param> [ámbito]
** preset
*** create <modelo> <nombre>
*** clone <preset> <nombre> [--model M]
*** companion add <preset> <companion> [--on cpu|gpu] [--force]
*** companion remove <preset> <companion>
*** model set <preset> <modelo> [--force]
*** list [--model M]
*** show <preset>
*** remove <preset>
** server
*** create <nombre> [--family F] [--port N]
*** add <servidor> <preset>...
*** remove-preset <servidor> <preset>
*** list
*** show <servidor>
** run <servidor> [--backend F[@V]] [--port N] [-d]
** ps
** logs <id-instancia> [-f]
** stop <id-instancia>
@endmindmap
```

## Correspondencias

| Coquena | Análogo | Nota |
|---|---|---|
| `backend install` | `sdk install java 21-tem`, `fnm install --lts` | sin argumentos elige familia por hardware y último release |
| `backend list` / `remote` | `sdk list java`, `fnm ls-remote` | |
| `distribution add` | candidato/vendor de SDKMan | distribuciones de terceros |
| `model add` | `sdk install java x /ruta/local` | registra sin copiar |
| `config set` sin ámbito | configuración global | con `--family` o `--model`, el ámbito correspondiente |
| `run` / `run -d` | `docker run` / `docker run -d` | genera el INI y lanza `llama-server` |
| `ps`, `logs -f`, `stop` | `docker ps`, `docker logs -f`, `docker stop` | |

## Arqueología (`legacy-go`)

La CLI anterior (`cmd/coquena/runtime.go`) tenía `init`, `webui`, `system`,
`runtime remote|download|list|remove`, `model resolve|download|revision`,
`download list|status|pause|resume|cancel` y `field define|remove|list|show`. No tenía
ningún comando para lanzar `llama-server`.

## Home

No hay comando `init`. El home se resuelve así (`COQUENA_HOME` primero, porque es una
decisión explícita del usuario; se conserva de `legacy-go`):

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
  deberá aplicarse al comando de quitar companions cuando se defina su superficie.
