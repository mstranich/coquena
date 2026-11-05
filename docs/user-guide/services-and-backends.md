# Services and backends

A service is a managed `llama-server` process running in router mode, so a single
server can expose many presets. There is at most one active instance per service.

Services are global or local. See [Global and local](global-and-local.md).

## The default service

One global service is the `default`, and `target: global` publishes to it. The first
service you create becomes the default. To change it, run:

```powershell
coquena service default set home-lab
```

## Run a service

```powershell
coquena service list
coquena service default show
coquena service default start -d
coquena service default restart -d
coquena service default stop
coquena service default remove
```

`-d` returns control while the backend keeps running. Without it, the service runs
in the foreground and Ctrl+C stops it. `remove` applies to a stopped service and
keeps its files and logs. `server` is an alias of `service`.

Starting a service and publishing presets are separate steps:

- `service start` and `service stop` control the process.
- `up` and `down` publish and withdraw the project's presets.
- `up --start-service` authorizes starting a stopped target without asking.

## Choose a backend

Official builds are selected by version and variant:

```yaml
service:
  backend:
    version: latest
    variant: cuda@12
```

`version` is `latest` or a build such as `bNNNN`. `variant` is `cpu`, `vulkan`, or
`cuda@<major>`. Run `coquena system probe` to see your hardware and accelerators.

Manage installations with these commands:

```powershell
coquena backend show
coquena backend list
coquena backend download --version latest --variant vulkan
```

Add `--global` to work with the global scope. In a project without a `service`
block, `backend show` reports `backend-required`, so use `coquena backend show --global`.
`runtime` is an alias of `backend`.

## Use your own build

A project can have its own service running a fork, so you can try its arguments
and experimental features before they reach official llama.cpp. Declare the
executable instead of a version and variant. Coquena uses the file where it is and
does not update it.

```yaml
name: fork-lab
target: self
service:
  backend:
    name: my-fork
    executable: ./bin/llama-server.exe
  server:
    args:
      port: 19932
models:
  local:
    files: [./weights/model.gguf]
presets:
  experiment:
    args:
      model: model://local
      my-fork-option: fast
```

The fork must stay compatible with the official router, including its `.ini` preset
files. Coquena does not assume this from the build's name, so check what your build
provides. If it needs a different health check, configure `service.server.health`.

To update presets without restarting the service, the build must support reloading
them. Otherwise, restart the service to apply changes.

In the default `permissive` mode, arguments Coquena does not know, such as
`my-fork-option`, are passed through. To describe and validate them, add them under
`arg-defs`. See [Define your own arguments](recipes.md#define-your-own-arguments).

## Server arguments

`service.server.args` sets `llama-server` arguments for the service:

```yaml
service:
  backend:
    version: latest
    variant: cpu
  server:
    args:
      port: 19931
    logs:
      retention:
        max-bytes: 104857600
```

## When a start fails

If a new configuration fails to start, Coquena tries to restore the last one that
worked for that service. The message shows the outcome, and `coquena logs` shows the
server's output.

To be notified, declare a program in `hooks`. It runs once after a failed start,
even if recovery succeeds, and it receives a JSON description on standard input.
It runs without a shell and has 10 seconds to finish.

```yaml
service:
  server:
    hooks:
      start-failed:
        command: ./scripts/notify.exe
        args: ["start-failed"]
```

## Observe a service

```powershell
coquena ps
coquena ps --service default --watch
coquena logs --service default
coquena logs list --service default
coquena logs follow --service default
```

`ps` shows hardware use, service state, and published presets. Observing a service
does not load models. Ctrl+C closes only the observer.

To update backends and free disk space, see [Maintenance](maintenance.md).
