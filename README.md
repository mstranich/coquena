# Coquena

<p align="center">
  <img src="docs/assets/coquena-banner.jpg" alt="Coquena, invisible guardian of the Andean AI and LLMs" width="100%">
</p>

**A little invisble guardian that keeps your llamas organized and ready to work.**

Coquena manages your local [llama.cpp](https://github.com/ggml-org/llama.cpp)
environment from the command line: it installs backends, downloads and verifies
GGUF models, organizes settings and presets, and manages servers with metrics
and logs.

It replaces the scripts and long command lines you end up maintaining to run
your models. You choose the backend, models, and parameters; Coquena saves those
settings and prepares each launch. `llama-server` handles inference and model
loading through its Router Mode, which lets a single server expose multiple presets.

## Why the name?

In Andean folklore, Coquena is a small shepherd and guardian of mountain wildlife.
The name brings that image to llama.cpp: a guardian that looks after your
“llamas,” keeps them organized, respects your machine's capabilities, and gets
their environment ready for work. Read more about the legend in
[this Perfil article (in Spanish)](https://www.perfil.com/noticias/cultura/el-coquena-la-leyenda-del-guardian-invisible-de-la-fauna-andina-y-las-leyes-del-cerro-a35.phtml).

## Current version: 0.3.0

Coquena 0.3.0 brings shareable YAML recipes to your local AI lab. Define a shared
llama.cpp service, give each project its own model presets, and apply changes
while the router keeps running. A project can also run its own official or
experimental backend alongside the shared service.

The current target is **Windows x64**, with a Native AOT executable that
**does not require .NET to be installed**.

- Write or receive a recipe and run it directly. Initialization is optional.
- Combine YAML layers for services, models, presets, and experimental arguments.
- Publish and withdraw project presets without restarting a compatible router.
- Keep backend versions, model resources, and resolved selections scoped to each project.
- Import recipes into the global catalog and reuse one service across projects.
- Inspect services and logs, verify resources, and apply protected updates and cleanup.

The 0.x series may introduce changes to commands or data formats. Read the release
notes before upgrading.

## Quick start

Download and extract the Windows x64 ZIP from
[GitHub Releases](https://github.com/mstranich/coquena/releases). Add the extracted
folder to PATH or replace `coquena` below with the full path to `coquena.exe`.
Backends and model weights are separate downloads and retain their own licenses.

Save this as `service.yaml` to define an official CPU service for the lab.

```yaml
name: home-lab
service:
  backend:
    version: latest
    variant: cpu
```

Import it and start the service in the background.

```powershell
coquena import service.yaml --as-default
coquena service default start -d
```

In a project directory, place a compatible GGUF model at `weights/model.gguf`
and save this as `coquena.yaml`.

```yaml
name: ai-lab
target: global
models:
  local:
    files: [./weights/model.gguf]
presets:
  chat:
    args:
      model: model://local
      ctx-size: 4096
```

Publish the project's preset to the shared service.

```powershell
coquena up
coquena ps
```

With the default port available, point your client at `http://127.0.0.1:9931`
and choose `ai-lab/chat`. llama.cpp loads the model when a request needs it.
Other projects can publish their own presets to the same service.

Edit the YAML or use the CLI, then apply the new definition.

```powershell
coquena preset set chat ctx-size 8192
coquena up
coquena logs --service default
```

A preset change can unload a model and interrupt requests. Coquena asks for
confirmation when it observes activity or cannot establish that the affected
entries are idle. Check each experimental backend's router capabilities.

Withdraw the project's presets and stop the service when you are finished.

```powershell
coquena down
coquena service default stop
```

`down` leaves the service running and preserves other projects' presets. A recipe
with `target: self` and a `service` block instead operates its own project service.

Data is stored in `%USERPROFILE%\.coquena` by default. Set `COQUENA_HOME` to use
another location. Managed project resources and local state are kept in `.coquena/`
inside the project. See the [recipe format](docs/formato-proyecto.md) for remote
models, explicit backend versions, layers, and external executables.

## Tune several profiles in one recipe

Finding a useful configuration can take several attempts. Keep the alternatives
in the same recipe while you try context lengths, GPU offload, CPU threads, and
KV cache settings. The profiles you keep can be versioned with the project and
shared with someone who wants to reproduce or adapt the experiment.

This example combines a Hugging Face model with a GGUF file you supply locally.
The short- and long-context presets share the same remote model selection.
The values illustrate different profiles to tune for your model and available
memory. GPU offload settings require a suitable CUDA or Vulkan backend.

```yaml
name: my-lab
target: global
models:
  hf-model: Qwen/Qwen2.5-1.5B-Instruct-GGUF:Q4_K_M
  local-model:
    files: [./weights/local-model.gguf]
presets:
  short-context:
    args:
      model: model://hf-model
      ctx-size: 8192
      n-gpu-layers: 99
      threads: 4
      cache-type-k: q8_0
      cache-type-v: q8_0
      flash-attn: on
  long-context:
    args:
      model: model://hf-model
      ctx-size: 32768
      n-gpu-layers: 20
      threads: 8
      cache-type-k: q4_0
      cache-type-v: q4_0
      flash-attn: on
  local-experiment:
    enable: false
    args:
      model: model://local-model
      ctx-size: 16384
      n-gpu-layers: 0
      threads: 8
```

Run `coquena up --start-service` and select `my-lab/short-context` or
`my-lab/long-context` in your client. Coquena prepares
the remote model from [Hugging Face](https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF).
To add `my-lab/local-experiment`, provide the local file, change its `enable` field
from `false` to `true`, and run `coquena up` again.
Edit the settings and run `coquena up` again to apply another iteration.

A coding project can keep several profiles for different context requirements.
A news project can instead use separate models for filtering and editing. Each
project publishes its own presets to the shared service. To try a backend fork
independently, define a project service with `target: self` and an external
`service.backend.executable`, including the health and router configuration that
build requires. See [backend and service configuration](docs/servidores.md).

## Build and install from source (Windows x64)

Prerequisites: Git, the .NET 10 SDK selected by `global.json`, and Visual Studio
Build Tools with the Desktop development with C++ workload and a Windows SDK
for Native AOT compilation.

Run these commands in PowerShell to clone the repository, prepare dependencies,
compile Native AOT packages, and install Coquena as a global .NET tool for your user:

```powershell
git clone https://github.com/mstranich/coquena.git
cd coquena

# Generate the tool entry package.
dotnet pack .\src\Coquena\Coquena.csproj -c Release -p:PackAsTool=true -p:ToolCommandName=coquena -p:ToolPackageRuntimeIdentifiers=win-x64 -o .\artifacts\tool

# Compile and package the Windows x64 native executable.
dotnet pack .\src\Coquena\Coquena.csproj -c Release -r win-x64 -p:PackAsTool=true -p:ToolCommandName=coquena -p:ToolPackageRuntimeIdentifiers=win-x64 -o .\artifacts\tool

# Install from the local packages.
dotnet tool install --global coquena --version 0.3.0 --add-source .\artifacts\tool

# Run from any directory.
coquena --help
```

Both `dotnet pack` commands restore build dependencies automatically. They generate
local packages; no upload or publication to a package server is required. The
packaging properties are supplied on the command line, so no project edits are needed.

Global tools are installed for the current user. If Windows cannot find `coquena`,
ensure `%USERPROFILE%\.dotnet\tools` is on your user `PATH` and open a new terminal.
The installed Native AOT executable does not need a separate .NET runtime.

To reinstall a build from an updated checkout, repeat both packaging commands,
then run `dotnet tool uninstall --global coquena` followed by the installation
command above. Uninstalling the tool does not remove Coquena application data.

## Documentation

The detailed documentation is currently in Spanish and is published at
[mstranich.github.io/coquena](https://mstranich.github.io/coquena/).

- [Documentation guide](docs/README.md): functional overview and project map.
- [Commands](docs/comandos.md): planned syntax and behavior, including future
  features. Run `coquena --help` to see the commands available in the current executable.
- [Servers and runtime settings](docs/servidores.md).
- [Models, downloads, and integrity](docs/modelos-y-companions.md).
- [Verified M1 walkthrough](specs/014-m1-entrega-m1/recorrido.md): a complete
  two-model example, the resources used, and the test conditions.
- [Milestone M2](docs/milestones/M2-gestion-completa.md): completed catalog
  management scope and acceptance evidence.

## Authorship and license

Coquena was conceived and designed by **Maximiliano Stranich**.

Copyright © 2026 Maximiliano Stranich.

Coquena is distributed under the [MIT License](LICENSE). Downloaded backends and
models remain subject to their own licenses.
