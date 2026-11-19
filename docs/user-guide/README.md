# Coquena User Guide

Coquena manages your local AI resources: models, presets, backends, and services, in
the global scope and in each project. On top of that, it gives you the control and the
monitoring to tune a model to your hardware. Finding the right model, quantization, and
settings often takes many tries; Coquena keeps each try short and lets you see its
effect on memory and speed. Use it for the models of a concrete project or for your
home AI lab.

You describe a service, models, and presets in a YAML recipe. Coquena downloads and
verifies what they need, prepares each launch, and publishes your presets to a running
[llama.cpp](https://github.com/ggml-org/llama.cpp) `llama-server` router.

![The terminal interface following a preset while it serves requests](images/tui-live.gif)

## How you work with it

When you tune a model, you repeat a quick loop:

1. **Edit** the recipe: another model, quantization, context size, or argument.
2. **Apply** it with `coquena up`. Coquena downloads the model if you do not have it
   yet and publishes the preset while the service keeps running.
3. **Load** it with `coquena load` to see whether it fits in memory, and watch the GPU
   memory in the [terminal interface](terminal-interface.md).
4. **Try** it with `coquena chat` and look at the answer and its speed.
5. **Change** something and go back to step 1.

![From an empty folder to a running preset: write the recipe, start it, and try it](images/getting-started.gif)

[Getting started](getting-started.md) walks through one full round.

## What you can do

- **Run several services at once.** Keep a global service and local services
  running side by side, each with its own binary.
- **Share a server.** Projects can publish their presets to one global service
  instead of each running their own, which saves resources.
- **Try forks.** Give a project its own service with a compatible fork and custom
  arguments to test features that are not in official llama.cpp yet.
- **Experiment on a live router.** Swap presets and models without restarting the
  service while you look for the sweet spot between your hardware and model speed.
- **Watch it work.** Follow services, tasks, and hardware in a terminal interface, and
  try a preset with a quick chat and its telemetry.

## Key ideas

| Term | Meaning |
| --- | --- |
| **Recipe** | A YAML file that defines a service, a set of presets, or both. A project's recipe is named `coquena.yaml`. |
| **Service** | A managed `llama-server` process and its backend. |
| **Backend** | The llama.cpp build a service runs: an official release or your own executable. |
| **Preset** | A named model configuration that clients select as `recipe-name/preset`. |
| **Project** | A folder with a recipe. Its local state lives in `.coquena/`. |
| **Global** | Services stored in the Coquena home and shared by projects. |
| **Local** | A service and resources that belong to one project. |

## Where to go next

- [Getting started](getting-started.md): install Coquena and send your first request.
- [Global and local](global-and-local.md): how services and projects relate.
- [Recipes](recipes.md): write recipes, combine layers, and keep several profiles.
- [Services and backends](services-and-backends.md): run, inspect, and share services.
- [Models and Presets](models-and-presets.md): choose models and configure how they run.
- [Try presets](try-presets.md): chat with a preset, load and unload it.
- [Terminal interface (TUI)](terminal-interface.md): watch services and manage definitions
  in a full-screen view.
- [CLI reference](cli-reference.md): the commands available in this version.
- [Configuration](configuration.md): adjust log retention, storage folders, and more.
- [Maintenance](maintenance.md): update resources, verify them, and free disk space.
- [Troubleshooting](troubleshooting.md): common errors and what to do.

## Beyond this guide

This guide covers everything you need to use Coquena. The [Design documentation](../design/README.md), written in Spanish, is aimed
at people working on Coquena itself.

Coquena's messages are available in English and Spanish, and follow your system's
language by default. This guide quotes them in English; see
[Language](configuration.md#language) to choose one.
