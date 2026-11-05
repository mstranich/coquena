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
[GitHub Releases](https://github.com/mstranich/coquena/releases), then add the
extracted folder to PATH. Backends and model weights are separate downloads and
retain their own licenses.

In a project folder, save this as `coquena.yaml`:

```yaml
name: ai-lab
presets:
  chat:
    args:
      model: Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M
      ctx-size: 4096
```

Run it from an interactive terminal. Coquena detects your hardware, downloads a
suitable backend and the model, and starts a shared service.

```powershell
coquena up --start-service
```

Point your client at `http://127.0.0.1:9931` and choose `ai-lab/chat`.

The [Getting started](https://mstranich.github.io/coquena/getting-started/) guide
continues from here.

## Documentation

- [User Guide](https://mstranich.github.io/coquena/): install Coquena, write
  recipes, and use the command line.
- [Roadmap](https://mstranich.github.io/coquena/milestones/): what has shipped and
  what comes next.
- [Design documentation](https://mstranich.github.io/coquena/design/) (in Spanish):
  what the application does and how it should behave, including planned features.
- [Build from source](https://mstranich.github.io/coquena/getting-started/#build-from-source).

## Authorship and license

Coquena was conceived and designed by **Maximiliano Stranich**.

Copyright © 2026 Maximiliano Stranich.

Coquena is distributed under the [MIT License](LICENSE). Downloaded backends and
models remain subject to their own licenses.
