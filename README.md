# Coquena

**A control plane for reliable and resource-efficient local inference.**

> **Project status:** Planning and design. No implementation is available yet.

Coquena is a local operations console for managing inference servers as long-running system services.

The initial implementation will focus on [`llama.cpp`](https://github.com/ggml-org/llama.cpp) and its `llama-server` executable.

Unlike tools that hide inference configuration behind simplified abstractions, Coquena aims to expose the underlying capabilities of the runtime while making them easier and safer to operate.

## Motivation

Running `llama-server` locally commonly involves manually maintained Bash scripts, PowerShell scripts, batch files, environment variables, and command-line arguments.

That approach works, but becomes increasingly difficult to manage when dealing with:

* complex server configurations;
* multiple models;
* limited RAM and VRAM;
* process restarts;
* runtime upgrades;
* logs and diagnostics;
* operating-system services;
* reproducible environments.

Coquena aims to turn `llama-server` into a stable and manageable local infrastructure component.

Its core principle is:

> Preserve resources, avoid unnecessary restarts, and keep local inference reliable.

## Why “Coquena”?

Coquena is a guardian from the folklore of the Argentine Northwest and the Andean region.

He protects llamas, vicuñas, guanacos, and other animals living in an environment where resources are scarce and balance is essential.

The metaphor matches the purpose of this project:

* inference models consume limited resources;
* loaded models should be preserved when possible;
* processes should not be restarted without reason;
* the system must remain under careful supervision;
* resource allocation should remain balanced.

Coquena is not the inference engine. It is its guardian.

## Goals

Coquena is intended to provide:

* lifecycle management for local inference servers;
* installation and execution as a system service;
* explicit access to runtime configuration;
* safe configuration changes;
* minimal and controlled process restarts;
* health and status monitoring;
* log streaming and diagnostics;
* reproducible runtime configurations;
* a local administration Web UI;
* a command-line interface for automation and headless environments;
* support for Windows and Linux.

The first supported runtime will be `llama.cpp`.

The architecture should allow additional inference runtimes to be supported later without weakening the initial focus.

## Non-goals

Coquena is not intended to be:

* a chat client;
* an agent framework;
* a model inference engine;
* a replacement for `llama.cpp`;
* an IDE;
* a model marketplace;
* a simplified abstraction that hides runtime configuration;
* a direct competitor to desktop applications such as LM Studio.

Other applications—including coding harnesses, agent runtimes and local AI tools—will connect directly to the inference server managed by Coquena.

## Initial scope

The first useful version should manage one local `llama-server` instance.

Expected responsibilities include:

1. Reading and editing the `llama.cpp` configuration.
2. Validating configuration changes before applying them.
3. Starting and stopping `llama-server`.
4. Restarting it only when required.
5. Confirming that the server becomes healthy after a change.
6. Restoring the previous configuration when an update fails.
7. Displaying runtime state and recent logs.
8. Streaming new log entries through Server-Sent Events.
9. Opening the local administration interface securely.
10. Running as a service on Windows and Linux.

Support for multiple concurrent instances is deliberately outside the initial scope.

## Proposed architecture

Coquena is expected to be distributed as a single executable containing both the backend and the compiled Web UI.

```text
┌───────────────────────────────────────┐
│               Coquena                 │
│                                       │
│  ┌──────────────┐   ┌──────────────┐  │
│  │ Angular Web  │   │   HTTP API   │  │
│  │      UI      │◄─►│   Go server  │  │
│  └──────────────┘   └──────┬───────┘  │
│                            │          │
│                  ┌─────────▼────────┐ │
│                  │ Runtime adapter  │ │
│                  └─────────┬────────┘ │
└────────────────────────────┼──────────┘
                             │
                    ┌────────▼────────┐
                    │  llama-server   │
                    └────────┬────────┘
                             │
                       HTTP endpoints
                             │
              ┌──────────────▼──────────────┐
              │ Agents, coding tools, UIs,  │
              │ local applications, LOKI   │
              └─────────────────────────────┘
```

### Backend

The backend is expected to be written in Go.

It will be responsible for:

* process lifecycle management;
* configuration persistence;
* operating-system integration;
* permissions;
* health checks;
* logs;
* authentication;
* rollback operations;
* serving the compiled frontend.

### Web UI

The administration interface is expected to be built with Angular.

Angular was selected because its reactive forms model is well suited for complex programmatic forms generated from structured configuration schemas.

The UI must remain a client of the backend API. It must not contain process-management or operating-system logic.

### Future TUI

A terminal interface may be added later.

It should consume the same backend API as the Web UI rather than duplicate lifecycle and configuration logic.

## Configuration model

Coquena should not treat the raw `llama.cpp` configuration file as its public API contract.

Instead, it should maintain an internal configuration model and translate it into the format required by the installed `llama.cpp` version.

This separation should allow Coquena to:

* validate values;
* provide metadata and descriptions;
* generate dynamic forms;
* handle deprecated options;
* migrate configurations between versions;
* remain stable when upstream formats change.

The underlying configuration should remain visible and understandable. Coquena should assist the operator, not conceal the runtime.

## Applying configuration changes

Configuration updates should be transactional whenever possible.

A proposed flow is:

1. Receive the new configuration.
2. Validate its structure and values.
3. Preserve the currently working configuration.
4. Write the candidate configuration.
5. Determine whether a restart is required.
6. Restart only when necessary.
7. Wait for the inference server to become healthy.
8. Commit the change when successful.
9. Restore the previous configuration when unsuccessful.

The definition of “restart required” should eventually be based on the specific fields that changed rather than restarting after every update.

## Logs and events

Coquena should maintain portable application logs independently from the operating system’s native logging facilities.

Logs may also be forwarded to platform-specific systems such as:

* `journald` on Linux;
* Windows Event Log on Windows.

The administration API should expose:

* recent log entries;
* filtered log queries;
* live server events;
* process state changes;
* configuration application results;
* health-check failures.

Live updates will initially use **Server-Sent Events**.

SSE is a good fit because these events primarily flow from the backend to the administration clients and do not require a bidirectional persistent protocol.

## Local authentication

The administration interface must not be publicly accessible by default.

The initial authentication model may use a short-lived random token generated by the backend.

A command such as:

```console
coquena webui
```

would open the default browser using a temporary authenticated URL.

After validating the token, the backend may establish a local session cookie so the credential does not remain in subsequent URLs.

For headless environments, the command should be able to print the URL instead of opening a browser.

Binding to loopback interfaces should be the default.

Remote administration, if eventually supported, will require a separate security model.

## Operating-system integration

### Windows

Coquena should support installation as a Windows Service.

Privileged process-management operations must remain isolated in the backend service. The browser-based interface must never execute privileged system operations directly.

### Linux

Coquena should support installation as a `systemd` service.

It should run under a dedicated account or controlled group whenever practical rather than requiring unrestricted root access.

## Repository structure

The initial repository may follow this structure:

```text
coquena/
├── apps/
│   └── web/                 # Angular administration interface
├── cmd/
│   └── coquena/             # Go executable entry point
├── internal/
│   ├── api/                 # HTTP API and SSE endpoints
│   ├── auth/                # Local authentication and sessions
│   ├── config/              # Configuration model and persistence
│   ├── events/              # Internal event distribution
│   ├── logs/                # Log collection and streaming
│   ├── runtime/             # Runtime contracts
│   │   └── llamacpp/        # llama.cpp adapter
│   ├── service/             # Windows and Linux service integration
│   └── system/              # Process and OS abstractions
├── docs/
│   ├── architecture.md
│   └── decisions/           # Architecture Decision Records
├── schemas/                 # Configuration and API schemas
├── Taskfile.yml
├── go.mod
├── LICENSE
└── README.md
```

This structure is provisional and will evolve once implementation starts.

## Development workflow

The project is expected to use [Task](https://taskfile.dev/) as its cross-platform task runner.

### Development prerequisites

The initial supported development baseline matches the primary Windows workstation:

* Go 1.26.5 or newer;
* Node.js 24.18.0 or newer;
* npm 11.16.0 or newer;
* Task 3.52.0 or newer.

The repository uses Unix-style development conventions on every host: UTF-8 text, LF line endings, final newlines, `/` in repository-relative paths, and shell-neutral Task commands. Commits follow [Conventional Commits](https://www.conventionalcommits.org/).

The root workflow is:

```console
task tools
task bootstrap
task verify
```

Backend and frontend loops remain independently available through the `backend:*` and `web:*` Task namespaces. Run `task --list` for the complete command list.

During development:

* Angular will run through its development server;
* the Go backend will run independently;
* the frontend will connect to the local backend API.

For release builds:

1. Angular will be compiled into static assets.
2. The static assets will be embedded into the Go executable.
3. A single distributable binary will serve both the API and Web UI.

## Possible future capabilities

The following ideas are intentionally deferred:

* configuration migration between `llama.cpp` releases;
* automatic runtime installation and upgrades;
* model discovery and metadata;
* Hugging Face integration;
* model download management;
* multiple inference-server instances;
* resource-aware model scheduling;
* RAM and VRAM monitoring;
* configuration history and auditing;
* backup and restore;
* remote administration;
* support for additional inference runtimes;
* terminal administration interface.

These are directions, not commitments.

## Current status

Coquena is currently a personal experimental project for exploring how local inference servers can be operated as reliable, long-running system services.

The project starts from a concrete need: running llama.cpp as a stable local provider without relying on manually maintained batch files, PowerShell scripts or shell scripts.

Its initial implementation will focus exclusively on llama.cpp and its llama-server executable. The architecture may later evolve to support additional inference runtimes, but this is not currently a commitment.

The project is intentionally designed with clear boundaries and replaceable components so it can grow beyond its original use case if the experiment proves useful.

There is no executable, package, installation procedure, stable API or published roadmap yet. The current design represents working hypotheses that may change substantially during implementation.

The first objective is to solve a narrow, real-world problem:

run an existing llama-server as a managed local service in Windows;
expose its health and runtime status;
stream its logs;
edit and validate its configuration;
apply changes safely;
avoid unnecessary process restarts.

Broader capabilities will only be considered after this initial workflow has been validated in practice.

## License

A license has not been selected yet. No provisional license is implied by the current repository contents.
