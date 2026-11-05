# Global and local

Coquena works in two scopes. **Global** is shared by your whole machine. **Local**
belongs to one project. You can use both at the same time.

| | Global | Local |
| --- | --- | --- |
| Belongs to | Your user | One project folder |
| Stored in | `%USERPROFILE%\.coquena`, or `COQUENA_HOME` | `.coquena\` in the project |
| Service created by | `up`, `coquena service create <name> --global`, or `coquena import` | A `service` block in the project's recipe |
| Selected with | `target: global`, or a service name or `default` | `target: self`, or `self` |
| Best for | Everyday models shared by several projects | Experiments, forks, and pinned backends |

`COQUENA_HOME` must be an absolute path. Coquena adds its own `.gitignore` to a
project's `.coquena\` folder.

## Global services

Global services are shared infrastructure. A project with `target: global`, or with
no `target`, publishes its presets to the default global service. Several projects
can do this at once: their presets are combined on one router, and each project
manages only its own. This saves resources, because the projects share one process
instead of competing for the GPU.

```yaml
name: news
target: global
presets:
  summary:
    args:
      model: Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M
```

## Local services

A local service belongs to its project and can use a different backend or a
[fork](services-and-backends.md#use-your-own-build). Give it a port that does not
collide with other services.

```yaml
name: experiment
target: self
service:
  backend:
    version: latest
    variant: vulkan
  server:
    args:
      port: 19932
presets:
  chat:
    args:
      model: Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M
```

Control it with the `self` selector, from the project folder:

```powershell
coquena service self show
coquena service self stop
```

## Where resources go

A resource stays in the scope where it was prepared. Models a project downloads are
stored in its `.coquena\` folder, even when it publishes to a global service.
Publishing does not copy them to the global scope, so two projects can each hold
their own copy of a model. Files you supply stay where they are.

Commands use the current project by default. Add `--global` to work with the global
scope instead:

```powershell
coquena model list
coquena model list --global
```

## Downloads

`up` downloads what enabled presets need, and `coquena model download` fetches a
model explicitly. Both verify files before installing them. Each scope keeps its own
downloads, so use `--global` to see the global ones.

```powershell
coquena download list
coquena download status
```

Ctrl+C pauses a download and keeps its progress. These commands control it by ID:

```powershell
coquena download pause <download-id>
coquena download resume <download-id>
coquena download cancel <download-id>
```

`cancel` deletes the temporary files of that download.

To free disk space, see [Maintenance](maintenance.md#disk-space).
