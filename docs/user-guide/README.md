# Coquena User Guide

Coquena manages a local [llama.cpp](https://github.com/ggml-org/llama.cpp)
environment from the command line. You describe a service, models, and presets
in YAML recipes. Coquena downloads and verifies what they need, prepares each
launch, and publishes your presets to a running `llama-server` router.

## What you can do

- **Run several services at once.** Keep a global service and local services
  running side by side, each with its own binary.
- **Share a server.** Projects can publish their presets to one global service
  instead of each running their own, which saves resources.
- **Try forks.** Give a project its own service with a compatible fork and custom
  arguments to test features that are not in official llama.cpp yet.
- **Experiment on a live router.** Swap presets and models without restarting the
  service while you look for the sweet spot between your hardware and model speed.

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
- [Maintenance](maintenance.md): update resources, verify them, and free disk space.
- [Configuration](configuration.md): adjust log retention, storage folders, and more.
- [CLI reference](cli-reference.md): the commands available in this version.
- [Troubleshooting](troubleshooting.md): common errors and what to do.

## Beyond this guide

This guide covers everything you need to use Coquena. To see where the project is
going, the [Roadmap](../milestones/README.md) lists what has shipped and what comes
next. The [Design documentation](../design/README.md), written in Spanish, is aimed
at people working on Coquena itself.

Coquena's command output is also in Spanish in this version. This guide quotes
messages in their original language where you need to recognize them.
