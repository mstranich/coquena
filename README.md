# Coquena

**A little guardian that keeps your llamas organized and ready to work.**

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

## First release: 0.1.0

Version 0.1.0 corresponds to M1: a local server. The current target is
**Windows x64**, with a Native AOT executable that **does not require .NET to be installed**.

- Discovery and installation of official CPU, CUDA, and Vulkan backends.
- Selection and download of GGUF models from public Hugging Face repositories,
  with integrity checks and validation of models split across multiple shards.
- Downloads that can be paused and resumed.
- Settings, presets, and server configuration for Router Mode.
- Foreground or background execution, stopping, restarting, metrics, and logs.

The project will add features through successive milestones and releases.
Linux and macOS support will be addressed after the initial features are complete.
The 0.x series is under development and may introduce changes to commands or data
formats; read the release notes before upgrading.

## Quick start

Published packages are hosted on
[GitHub Releases](https://github.com/mstranich/coquena/releases). Download the
Windows x64 package, extract it, and open PowerShell in that folder. Models and
llama.cpp binaries are downloaded separately; disk space and memory requirements
depend on your selections.

```powershell
.\coquena.exe --help
.\coquena.exe system
.\coquena.exe backend remote
.\coquena.exe backend install
```

Installing without specifying a family selects an official backend compatible
with your hardware. You can also explicitly choose `cpu`, `vulkan`, or a variant
such as `cuda@13`. The first successfully installed backend becomes the default.

To download a model, replace `user/repository` with a public Hugging Face
repository containing GGUF models suitable for your machine:

```powershell
.\coquena.exe model download user/repository
.\coquena.exe model list
```

The menu lets you read the model's documentation and choose what to download.
Ctrl+C pauses a transfer and displays the GUID you can use to resume it:

```powershell
.\coquena.exe download resume <guid>
```

To create a preset and expose it through the initial `default` server, replace
`user/repository:quantization` with the exact name shown by `model list`:

```powershell
.\coquena.exe preset create user/repository:quantization my-model
.\coquena.exe config set ctx-size 2048 --preset my-model
.\coquena.exe server default preset add my-model
.\coquena.exe server default start -d
.\coquena.exe server ps
```

With the initial port available and the default settings, the server listens on
`http://127.0.0.1:9931`. Use `my-model` as the model name in your client compatible
with the llama.cpp API. You can add more presets to the server; changes take
effect the next time it starts.

```powershell
.\coquena.exe server default logs
.\coquena.exe server default stop
```

Data is stored in `%USERPROFILE%\.coquena` by default. Set the `COQUENA_HOME`
environment variable to use a different directory.

## Documentation

The detailed documentation is currently in Spanish.

- [Documentation guide](docs/README.md): functional overview and project map.
- [Commands](docs/comandos.md): planned syntax and behavior, including future
  features. Run `coquena --help` to see the commands available in the current executable.
- [Servers and runtime settings](docs/servidores.md).
- [Models, downloads, and integrity](docs/modelos-y-companions.md).
- [Verified M1 walkthrough](specs/014-m1-entrega-m1/recorrido.md): a complete
  two-model example, the resources used, and the test conditions.
- [Milestone M2](docs/milestones/M2-gestion-completa.md): plans for the remaining
  initial features.

## Authorship and license

Coquena was conceived and designed by **Maximiliano Stranich**.

Copyright © 2026 Maximiliano Stranich.

Coquena is distributed under the [MIT License](LICENSE). Downloaded backends and
models remain subject to their own licenses.
