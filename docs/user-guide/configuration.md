# Configuration

Coquena works without any configuration. The `config` command adjusts its global
settings, which apply to your user and are stored in the Coquena home. Each setting
has `show`, `set`, and `unset`; `unset` restores the default.

| Setting | Default | Controls |
| --- | --- | --- |
| `language` | `auto` | The language of Coquena's messages |
| `log-retention` | 100 MiB | How much log history each service keeps |
| `observer-interval` | 2 s; 0.5 s in `logs -f` | How often observers read a service |
| `observer-retry` | 1 s, up to 30 s | How `ps --watch` and `logs follow` reconnect |
| `models-dir` | `models` in the Coquena home | Where global models are stored |
| `backends-dir` | `backends` in the Coquena home | Where global backends are stored |

The **Coquena** category of the Configuration tab (≡) in the
[terminal interface](terminal-interface.md) edits the same settings.

## Language

Coquena ships in English (`en-US`) and Spanish (`es-AR`). With `auto`, it follows the
language of your system and falls back to English.

```powershell
coquena config language list
coquena config language set en-US
coquena config language unset
```

Numbers and dates keep your system's regional format; the language only changes the
texts. To translate Coquena, export a language as a template, translate its values,
and save it as `languages/<code>.json` in the Coquena home:

```powershell
coquena config language export es-AR
```

## Coquena home

The home holds global services, resources, and settings. It is
`%USERPROFILE%\.coquena` unless you set `COQUENA_HOME` to an absolute path:

```powershell
$env:COQUENA_HOME = 'D:\coquena'
```

This sets it for the current terminal. To keep it, set it as a user environment
variable in Windows.

## Log retention

Each time a service starts, it writes a new log file. Older files are removed when a
service's logs exceed the limit.

```powershell
coquena config log-retention show
coquena config log-retention set 500MiB
coquena config log-retention unset
```

Sizes accept bytes or units such as `KiB`, `MiB`, and `GiB`. `KB`, `MB`, and `GB` also
use base 1024, so `1GB` means 1 GiB. Lowercase `k`, `m`, and `g` alone use base 1000.

The limit applies to each service, not to all of them together. Coquena never truncates
the active or latest log, so logs can exceed the limit. A new limit takes effect the
next time a service starts or its logs are queried, not immediately.

To set a different limit or folder for one service, use its recipe:

```yaml
service:
  backend:
    version: latest
    variant: cpu
  server:
    logs:
      directory: ./logs
      retention:
        max-bytes: 104857600
```

## Reconnection

When a service stops responding, `ps --watch` and `logs follow` keep trying to
reconnect. The wait doubles after each failure, up to a maximum. Set both values in
whole seconds, up to 3600. Observers read them when they open.

```powershell
coquena config observer-retry show
coquena config observer-retry set 2 60
coquena config observer-retry unset
```

## Observation interval

`ps --watch`, the status line of `logs -f`, and the Live tab of the terminal interface
read each service periodically. By default they read every 2 seconds, and the status
line every 0.5 seconds. Set your own interval, in seconds between 0.25 and 60, to get
more detail or to put less load on the service; it then applies to all of them:

```powershell
coquena config observer-interval show
coquena config observer-interval set 1
coquena config observer-interval unset
```

`--interval` in `ps --watch` and `logs -f` overrides it for a single run. In the Live
tab, each column of the charts covers one interval, so a longer interval shows a longer
stretch of history.

## Storage folders

These commands change where global models and backends are stored:

```powershell
coquena config models-dir show
coquena config models-dir set D:\coquena\models
coquena config backends-dir set D:\coquena\backends
```

Coquena asks whether to move the existing files, leave them where they are, or cancel.
Run these commands in a terminal, because they need your answer.

## Parameter catalog

Coquena keeps a catalog of known `llama-server` arguments. It does not validate your
recipes against it yet; for that, use [your own definitions](recipes.md#define-your-own-arguments).
To see the catalog, add `--global` outside a project:

```powershell
coquena param list --global
coquena param show ctx-size --global
```

To restore the catalog that ships with this version, preview the impact first. Values
assigned in presets are kept.

```powershell
coquena config param-reset --dry-run
coquena config param-reset
```
