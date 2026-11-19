# Coquena

<p align="center">
  <img src="docs/assets/coquena-banner.jpg" alt="Coquena: manage, watch &amp; tune your local LLMs" width="100%">
</p>

**The invisible guardian of your local llamas: organized, tuned, and ready to work.**

Coquena manages your local AI resources for [llama.cpp](https://github.com/ggml-org/llama.cpp):
it installs backends, downloads and verifies GGUF models, organizes presets and
parameters, and runs services, in a global scope shared by your projects and in each
project. You use it from the command line or from a terminal interface that keeps
services, tasks, logs, and hardware in view.

<p align="center">
  <img src="docs/user-guide/images/tui-live.gif" alt="The terminal interface following a preset while it serves requests" width="100%">
</p>

It also gives you the control and monitoring to tune a model to your hardware.
Finding the right model, quantization, and settings often takes many tries; Coquena
keeps each try short:

1. Edit the YAML recipe: another model, quantization, context size, or argument.
2. Run `coquena up`: it downloads what is missing and publishes the change while the
   service keeps running.
3. Run `coquena load` to see whether the model fits in memory.
4. Run `coquena chat` to look at the answer and its speed, then change something and
   repeat.

It replaces the scripts and long command lines you end up maintaining to run your
models. `llama-server` handles inference and model loading through its Router Mode,
which lets a single server expose multiple presets.

## Why the name?

In Andean folklore, Coquena is a small shepherd and guardian of mountain wildlife.
The name brings that image to llama.cpp: a guardian that looks after your
“llamas,” keeps them organized, respects your machine's capabilities, and gets
their environment ready for work. Read more about the legend in
[this Perfil article (in Spanish)](https://www.perfil.com/noticias/cultura/el-coquena-la-leyenda-del-guardian-invisible-de-la-fauna-andina-y-las-leyes-del-cerro-a35.phtml).

## Current version: 0.5.0

Coquena 0.5.0 adds a terminal interface to watch and manage your local AI lab, a
quick chat to try presets with their telemetry, and messages in English and
Spanish. It builds on the shareable YAML recipes of 0.3.0: define a shared
llama.cpp service, give each project its own model presets, and apply changes
while the router keeps running.

The current target is **Windows x64**, with a Native AOT executable that
**does not require .NET to be installed**.

- Write or receive a recipe and run it directly. Initialization is optional.
- Combine YAML layers for services, models, presets, and experimental arguments.
- Publish and withdraw project presets without restarting a compatible router.
- Keep backend versions, model resources, and resolved selections scoped to each project.
- Import recipes into the global catalog and reuse one service across projects.
- Inspect services and logs, verify resources, and apply protected updates and cleanup.
- Watch services, tasks, and hardware in the terminal interface (`coquena tui`).
- Chat with a published preset, or send it a single prompt from a script.
- See used disk space and free what no project needs.

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

Try the preset in the terminal, or point any OpenAI-compatible client at
`http://127.0.0.1:9931` and choose `ai-lab/chat`:

```powershell
coquena chat ai-lab/chat
```

To watch the service, the model, and the hardware while you change the recipe, run
`coquena tui` in another terminal.

The [Getting started](https://mstranich.github.io/coquena/getting-started/) guide
continues from here.

## Documentation

- [User Guide](https://mstranich.github.io/coquena/): install Coquena, write
  recipes, and use the command line and the terminal interface.
- [Design documentation](https://mstranich.github.io/coquena/design/) (in Spanish):
  what the application does and how it should behave, including planned features.
- [Build from source](https://mstranich.github.io/coquena/getting-started/#build-from-source).

## Authorship and license

Coquena was conceived and designed by **Maximiliano Stranich**.

Copyright © 2026 Maximiliano Stranich.

Coquena is distributed under the [MIT License](LICENSE). Downloaded backends and
models remain subject to their own licenses.
