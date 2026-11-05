# Configuration

Coquena works without any configuration. The `config` command adjusts its global
settings, which apply to your user and are stored in the Coquena home. Each setting
has `show`, `set`, and `unset`; `unset` restores the default.

| Setting | Default | Controls |
| --- | --- | --- |
| `log-retention` | 100 MiB | How much log history each service keeps |
| `observer-retry` | 1 s, up to 30 s | How `ps --watch` and `logs follow` reconnect |
| `models-dir` | `models` in the Coquena home | Where global models are stored |
| `backends-dir` | `backends` in the Coquena home | Where global backends are stored |

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
