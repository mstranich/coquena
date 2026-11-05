# Getting started

This walkthrough goes from an empty machine to your first response. You write one
small file, and Coquena prepares everything else.

## Install Coquena

Download the Windows x64 ZIP from
[GitHub Releases](https://github.com/mstranich/coquena/releases) and extract it.
Add the extracted folder to `PATH`, or replace `coquena` below with the full path
to `coquena.exe`. The executable does not require .NET.

Backends and model weights are separate downloads and keep their own licenses.

## Write a recipe

Create a folder for your project. Inside it, save this as `coquena.yaml`:

```yaml
name: ai-lab
presets:
  chat:
    args:
      model: Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M
      ctx-size: 4096
```

## Start

Run this from that folder, in an interactive terminal:

```powershell
coquena up --start-service
```

The first time, Coquena detects your hardware, downloads the most suitable official
backend, and creates a default global service. It then downloads the model, starts
the service, and publishes the preset as `ai-lab/chat`. The service keeps running
after the command ends.

To confirm the preset is published, run:

```powershell
coquena ps
```

## Send a request

Any OpenAI-compatible client can use the service at `http://127.0.0.1:9931`. Select
the published preset as the model. llama.cpp loads it when the first request arrives.

```powershell
Invoke-RestMethod `
  -Uri 'http://127.0.0.1:9931/v1/chat/completions' `
  -Method Post `
  -ContentType 'application/json' `
  -Body @'
{
  "model": "ai-lab/chat",
  "messages": [{ "role": "user", "content": "Say hi" }],
  "max_tokens": 16
}
'@
```

If the port is taken, see
[Troubleshooting](troubleshooting.md#port-already-in-use).

## Change a setting

Edit the YAML or use the CLI, then apply the new definition. The service keeps
running.

```powershell
coquena preset set chat ctx-size 8192
coquena up
```

A preset change can unload a model and interrupt requests. Coquena asks for
confirmation when it observes activity or cannot establish that the affected
entries are idle.

## Clean up

Withdraw the project's presets. The service keeps running and other projects'
presets stay published.

```powershell
coquena down
```

Stop the service. Downloaded files are kept.

```powershell
coquena service default stop
```

## Choose the service yourself

The automatic setup suits most people. To decide the details yourself, create the
service before running `up`.

From the command line, with the same automatic backend selection:

```powershell
coquena service create home-lab --global
```

From a recipe, to set the backend and the server arguments:

```yaml
name: home-lab
service:
  backend:
    version: latest
    variant: cpu
```

```powershell
coquena import service.yaml --as-default
```

See [Global and local](global-and-local.md) for how services and projects relate.

## Build from source

Prerequisites: Git, the .NET 10 SDK selected by `global.json`, and Visual Studio
Build Tools with the Desktop development with C++ workload and a Windows SDK.

Run these commands in PowerShell to build the Native AOT packages and install
Coquena as a global .NET tool for your user:

```powershell
git clone https://github.com/mstranich/coquena.git
cd coquena

# Generate the tool entry package.
dotnet pack .\src\Coquena\Coquena.csproj -c Release -p:PackAsTool=true -p:ToolCommandName=coquena -p:ToolPackageRuntimeIdentifiers=win-x64 -o .\artifacts\tool

# Compile and package the Windows x64 native executable.
dotnet pack .\src\Coquena\Coquena.csproj -c Release -r win-x64 -p:PackAsTool=true -p:ToolCommandName=coquena -p:ToolPackageRuntimeIdentifiers=win-x64 -o .\artifacts\tool

# Install from the local packages.
dotnet tool install --global coquena --version 0.3.0 --add-source .\artifacts\tool

coquena --help
```

If Windows cannot find `coquena`, ensure `%USERPROFILE%\.dotnet\tools` is on your
user `PATH` and open a new terminal. The installed executable does not need a
separate .NET runtime.

To reinstall from an updated checkout, repeat both packaging commands, run
`dotnet tool uninstall --global coquena`, and install again. Uninstalling the
tool does not remove Coquena application data.
