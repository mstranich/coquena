# CLI reference

This is a summary of Coquena 0.3.0. Run `coquena --help` for every option.

Notation: `<value>` is required, `[value]` is optional, and `|` separates
alternatives.

## General

| Command | Purpose |
| --- | --- |
| `coquena init [<directory>]` | Create a minimal recipe |
| `coquena system probe` | Show hardware and accelerators |
| `coquena ps [--service <name>] [-w]` | Show or follow hardware and services |
| `coquena import <recipe.yaml>` | Import global intent without starting anything |

`import` also accepts `--as-default`, `--select`, `--replace <entity>`, and `--force`.

## Publication

| Command | Purpose |
| --- | --- |
| `coquena up [--start-service]` | Publish the selected combination of presets |
| `coquena down` | Withdraw the project's presets without stopping the service |
| `coquena apply <key>` | Publish one preset |
| `coquena withdraw <key>` | Withdraw one preset |

These commands require an interactive terminal.

| Option | Purpose |
| --- | --- |
| `-f`, `--file <yaml>` | Add a recipe layer; repeatable |
| `--env-file <file>` | Add an environment file; repeatable |
| `--project-directory <folder>` | Set the project folder (alias `--base`) |
| `--service <name\|default\|self>` | Select the destination |
| `--private` | Allow private external references |

## Services

The selector is a service name, `default`, or `self`. Without one, `default` is
used. `server` is an alias of `service`.

| Command | Purpose |
| --- | --- |
| `service create <name> --global` | Create an official service without starting it |
| `service list` | List known services |
| `service [<selector>] show` | Show the effective definition |
| `service default set <name>` | Choose the default global service |
| `service [<selector>] start\|restart [-d]` | Start or restart; `-d` detaches |
| `service [<selector>] stop` | Stop the verified instance |
| `service [<selector>] remove` | Remove a stopped service, keeping files and logs |
| `logs [list\|follow\|show <log-id>] [-f] [--service <selector>]` | Read logs |

## Backends

`runtime` is an alias of `backend`.

| Command | Purpose |
| --- | --- |
| `backend set --version <version> --variant <variant>` | Select an official build in the recipe |
| `backend set --executable <path>` | Select your own executable |
| `backend show` | Show the recipe's backend |
| `backend list` | List revisions in the scope |
| `backend download --version <version> --variant <variant> [--force]` | Prepare and protect an official build |
| `backend protect\|unprotect <installation-id>` | Change explicit protection |
| `backend remove <installation-id>` | Remove an unprotected, unreferenced installation |

## Models and companions

| Command | Purpose |
| --- | --- |
| `model list` | List inventory and known progress |
| `model show [<alias>]` | Show intent without downloading |
| `model add <file.gguf> --name <name> [--try-match]` | Register an external file |
| `model download <repository[:quantization]>` | Choose and download a model |
| `model remove <name\|id>` | Remove owned files and keep external ones |
| `model protect\|unprotect <installation-id>` | Change explicit protection |

`model companion` provides the same commands. Downloads use
`<repository#type[:quantization]>`, where `type` is `mmproj` or `mtp`.

## Downloads

| Command | Purpose |
| --- | --- |
| `download list [--model\|--companion\|--backend]` | List downloads |
| `download status [<id>]` | Show one download, or all |
| `download pause <id>` | Pause and keep progress |
| `download resume <id> [--force]` | Resume a download |
| `download cancel <id>` | Cancel and delete temporary files |

## Presets

| Command | Purpose |
| --- | --- |
| `preset list` | List presets |
| `preset show <name>` | Show a preset |
| `preset set <preset> <argument> <yaml-value>` | Set an argument |
| `preset bind <preset> <argument> <model://key\|companion://key>` | Bind a resource |
| `preset unset <preset> <argument>` | Remove an argument |
| `preset clone <source> <name>` | Copy a preset |
| `preset remove <name>` | Remove a preset |
| `preset apply\|withdraw <key>` | Same as `apply` and `withdraw` |

## Maintenance

| Command | Purpose |
| --- | --- |
| `update [--global]` | Check for updates and save a plan |
| `upgrade [--global]` | Apply valid proposals from the plan |
| `verify [--global] [--checksum] [--json]` | Audit resources |
| `clean [--global] [--dry-run\|--yes]` | Preview or free eligible resources |
| `combination list\|forget <id>` | Show or forget combinations |

`update`, `upgrade`, and `verify` accept a repeatable `--select <reference>`, such
as `backend://cuda@12`, `model://key`, or `companion://key`.

## Configuration

| Command | Purpose |
| --- | --- |
| `config backends-dir show\|set <path>\|unset` | Configure the backends folder |
| `config models-dir show\|set <path>\|unset` | Configure the models folder |
| `config log-retention show\|set <size>\|unset` | Configure global log retention |
| `config observer-retry show\|set <initial-s> <max-s>\|unset` | Configure reconnection |
| `param list\|show <name>` | Inspect parameter definitions |
| `param define <name> --file <definition.yaml>` | Define a parameter |
| `param set <name> <field> <yaml-value>` | Change a definition |
| `param unset <name> <field>` | Remove a field |
| `param remove <name>` | Remove a definition |
| `param reset [--yes]` | Reset the global catalog |

## Environment

| Variable | Purpose |
| --- | --- |
| `COQUENA_HOME` | Absolute path of the data and configuration folder |
