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

The first local Web UI slice is started with:

```console
coquena webui
```

It listens only on `127.0.0.1:8080`, prints the effective URL, and stops cleanly with `Ctrl+C`. The port can be overridden with `coquena webui --port 9090`. It does not open the browser automatically and does not yet support remote binding, authentication, or CORS.

The server exposes the Angular application and a minimal backend health endpoint:

```console
curl http://127.0.0.1:8080/api/health
```

which returns `ok`.

Future authentication may use a short-lived random token generated by the backend.

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
├── openspec/
│   ├── specs/               # Current, stable capability requirements
│   └── changes/             # Active and archived change artifacts
├── schemas/                 # Configuration and API schemas
├── Taskfile.yml
├── go.mod
├── LICENSE
└── README.md
```

This structure is provisional and will evolve once implementation starts.

## Specification-driven workflow

Coquena uses [OpenSpec](https://github.com/Fission-AI/OpenSpec) as the single repository-local system for planning changes, recording technical design, defining behavioral requirements, tracking implementation tasks, and preserving completed change history.

The `openspec/` directory is organized as follows:

```text
openspec/
├── config.yaml              # Project context and artifact rules
├── specs/                   # Stable requirements describing current behavior
└── changes/
    ├── <change-name>/       # Active proposal, design, delta specs, and tasks
    └── archive/             # Completed changes preserved with their context
```

Each non-trivial change follows the same lifecycle:

1. Create a change with a narrow proposal and explicit non-goals.
2. Define testable requirements and scenarios.
3. Record the technical design and relevant trade-offs.
4. Break implementation into independently verifiable tasks.
5. Implement and validate every task.
6. Synchronize the stable specs and archive the completed change.

Architectural decisions that belong to a capability are recorded in that change's `design.md`. Once archived, the design remains available with the proposal, requirements, and implementation tasks that motivated it. This avoids maintaining a separate documentation hierarchy whose relationship to the implemented behavior is unclear.

Useful inspection commands include:

```console
openspec list
openspec list --specs
openspec validate --all
```

The root README communicates product intent and the high-level architectural hypothesis. The stable OpenSpec files under `openspec/specs/` are the behavioral source of truth for implemented capabilities.

## Development workflow

The project is expected to use [Task](https://taskfile.dev/) as its cross-platform task runner.

### Development prerequisites

The initial supported development baseline matches the primary Windows workstation:

* Go 1.26.5 or newer;
* Node.js 24.18.0 or newer;
* npm 11.16.0 or newer;
* Task 3.52.0 or newer.
* [Air](https://github.com/air-verse/air) 1.67.2 for backend hot reload (`go install github.com/air-verse/air@v1.67.2`).

The repository uses Unix-style development conventions on every host: UTF-8 text, LF line endings, final newlines, `/` in repository-relative paths, and shell-neutral Task commands. Commits follow [Conventional Commits](https://www.conventionalcommits.org/).

The root workflow is:

```console
task tools
task bootstrap
task verify
```

Backend and frontend loops remain independently available through the `backend:*` and `web:*` Task namespaces. Run `task --list` for the complete command list.

### Unified development loop

Start Angular and the Go backend together with automatic reload:

```console
task dev
```

The task compiles a small development supervisor beneath `.tmp/dev/`, initializes the repository `home/` directory, and exports its absolute path as `COQUENA_HOME` to both watched processes. The persistent `home/` contents are ignored by Git; disposable executables and watcher artifacts remain in `.tmp/dev/`. Angular serves the frontend with hot reload and proxies relative `/api` requests to the backend at `127.0.0.1:8080`; Air rebuilds and restarts the backend when Go sources change. Press Ctrl+C once to stop the supervised tree.

On Windows, the supervisor uses a kill-on-close Job Object; on Unix it uses process groups. For that reason a restrictive parent terminal, CI runner, or harness can prevent startup rather than permit an unsupervised tree. Killing only the parent `task.exe` is not a supported shutdown path and may leave `devrun` active; use Ctrl+C or terminate the reported `devrun` PID.

After stopping development, remove all mutable state (including downloaded runtimes) and `.tmp/dev/` while retaining the committed `home/.gitkeep` marker:

```console
task clean
```

## Portable initialization

Coquena is designed to remain in the directory where it was manually extracted or placed by a separate installer. It does not copy or relocate itself. Initialize that portable application tree explicitly:

```console
coquena init
```

The command derives the application root from the resolved executable location, not from the terminal's current working directory, and creates this layout beside the executable:

```text
Coquena/
├── coquena[.exe]
├── config/
│   └── coquena.json
├── data/
├── logs/
├── runtimes/
└── tmp/
```

All Coquena application configuration uses JSON. The initial `config/coquena.json` contains a versioned configuration contract:

```json
{
  "schemaVersion": 1
}
```

Initialization is idempotent and non-destructive: rerunning it preserves a valid existing configuration and content while recreating missing standard directories. Invalid JSON, unsupported schema versions, filesystem conflicts, and insufficient write permissions produce an error instead of overwriting content.

The directory containing Coquena must be writable by the account that initializes and operates it. A future Bash installer, MSI/MSIX package, or other distribution mechanism is responsible for choosing a location and assigning suitable permissions. `coquena init` does not elevate privileges, modify `PATH`, register a service, integrate with the operating system, or download llama.cpp or models.

## System capability inspection

Inspect the host without initializing or modifying it:

```console
coquena system
```

The human-readable report describes the operating system, executable and native architectures, CPU, total memory, graphics adapters, and CPU, CUDA, Vulkan, or Metal backend states. Missing optional GPU hardware, drivers, or loaders are reported normally and do not make the command fail.

Installers and other applications can request the same normalized result as JSON:

```console
coquena system --json
```

JSON standard output contains only one newline-terminated document with `schemaVersion` equal to `1`. Optional capabilities use `available`, `unavailable`, `unknown`, or `unsupported`; absence and inconclusive optional probes still return exit status `0`. Non-zero status is reserved for invalid command usage or failure to produce the report.

Windows has the first complete native implementation. Other compiled platforms currently return the Go runtime facts they can establish and explicit deferred capability states. This command does not select, download, install, or activate a llama.cpp runtime.

## llama.cpp runtime artifacts

Coquena manages official `llama.cpp` binary distributions as versioned local runtimes. The workflow follows runtime and SDK managers such as FNM, NVM, and SDKMAN: discover a compatible build with a short selector, download it by that selector or by its exact upstream asset ID, inspect installed versions, and remove a version explicitly.

List the compatible distributions in the latest official release:

```console
coquena runtime remote
coquena runtime remote cuda
coquena runtime remote cuda@13
coquena runtime remote b10068 vulkan
coquena runtime remote --history 20 cuda@13
```

The human-readable matrix includes the release, CPU or GPU target, normalized backend, exact primary `ASSET` filename, and any `DEPENDENCIES`. For example, a Windows CUDA runtime has a `llama-...` primary asset and a matching `cudart-...` dependency; Coquena treats them as one managed runtime rather than two independent choices.

Download by a convenient selector when it identifies exactly one compatible distribution:

```console
coquena runtime download cpu
coquena runtime download cuda@13
coquena runtime download b10068 vulkan
```

An omitted release means `latest`. Broad selectors are allowed, but Coquena never guesses: if `cuda`, `cuda@13`, or another shorthand matches multiple distributions, the command exits without downloading or changing files and prints the exact asset IDs that can disambiguate the request. For a reproducible selection, pass the complete primary asset ID shown by `runtime remote`:

```console
coquena runtime download llama-b10068-bin-win-cuda-13.3-x64.zip
```

The exact name remains the public ID after installation:

```console
coquena runtime list
coquena runtime remove llama-b10068-bin-win-cuda-13.3-x64.zip
```

Remote discovery and download are constrained to the current operating system, native architecture, and backends established as compatible by `coquena system`. Coquena does not offer cross-platform or cross-architecture downloads, does not consider executable emulation compatible, and does not download a backend whose support is unknown. The initial complete Windows selection covers CPU, CUDA, and Vulkan. `runtime list` still shows every valid managed installation if hardware or driver changes later make one incompatible or unknown, and `runtime remove` can remove it by exact ID.

Remote and local listings support `--json` for automation:

```console
coquena runtime remote cuda@13 --json
coquena runtime list --json
```

Successful JSON output writes only one newline-terminated document to standard output. It uses `schemaVersion: 1` and keeps the applied selection, normalized entries, dependencies, compatibility evidence, and diagnostics as separate fields. Download progress and operational diagnostics do not contaminate JSON standard output.

This scope does not activate a runtime, choose a default, execute `llama-server`, upgrade or roll back an active runtime, manage models, import external or forked builds, expose incompatible remote assets through an `--all` mode, or bypass compatibility with a force/unsafe option. Those behaviors require separate changes after artifact management is established.

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

There is no published package, operating-system installer, stable API or roadmap yet. The current design represents working hypotheses that may change substantially during implementation.

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

## GGUF model library

Coquena manages GGUF artifacts independently of any installed runtime. Resolve and explore public Hugging Face repositories, then download a selected GGUF through the shared backend:

```console
coquena model resolve org/repository
coquena model discover org/repository
coquena model discover org/repository:Q4_K_M
coquena model inspect <model-path>
coquena model download org/repository:Q4_K_M
coquena model download org/repository:Q4_K_M --with-companions
coquena model download list
coquena model download status <job-id>
coquena model download pause <job-id>
coquena model download resume <job-id>
coquena model download cancel <job-id>
coquena model revision <model-path>
coquena model list
coquena model reconcile (<reference>|--all) (--online|--accept)
coquena model verify <reference>
coquena model check-updates <reference>
```

Remote exploration and local inspection are separate commands. `coquena model
discover <repository-or-url[:selector]>` reaches the network: it lists every
GGUF candidate in the repository with its classification, marks the candidate a
selector would resolve to, and prints the repository model card as plain text.
`coquena model inspect <model-path>` is local only, issues no network request,
and renders a `Metadata`/`Value` table for an installed artifact, including its
declared type.

Every artifact is classified by its own `general.type` — `model` for a
principal and `mmproj` for a multimodal projector — read from the first bytes of
the GGUF header, over an HTTP range request for a remote candidate and directly
for an installed file. A missing or unrecognised value is reported as `unknown`
and is never inferred from the architecture or the filename. Companion
compatibility is a computed criterion rather than a pin to a file: a projector
matches a principal when its projection dimension equals that principal's
architecture-specific embedding length.

`coquena model download ... --with-companions` enqueues the principal together
with the companions identified for it, creating one durable job per artifact and
reporting every job ID. Without the flag, one invocation downloads exactly one
artifact.

`coquena model list` renders the library as a tree, nesting each companion
beneath the principal it is compatible with; a companion matching no installed
principal is reported at the top level. The same shape is produced by `--json`.
`coquena model reconcile` names its subject with the same `user/repo:QUANT`
reference every other model command accepts, resolved against the unreconciled
entries the listing reports. An artifact is addressed identically whether it is
being reconciled, bound to a preset, verified, or checked for updates, and
`--accept` keeps that reference form even for an artifact with no Hugging Face
identity to update against. `--all` replaces the reference and walks the
configured models directory, classifying every unreconciled artifact and
persisting its sidecar in one pass. Exactly one of `--online` and `--accept` is
still required, and reconciliation always remains an explicit operator action:
no listing command triggers it.

Runtime storage can be configured independently from model storage:

```console
coquena config runtimes-directory
coquena config runtimes-directory D:/coquena-runtimes
```

Download management is shared across resources. The global form includes all
jobs, while the model and runtime forms filter by resource type:

```console
coquena download list
coquena download status
coquena model download list
coquena model download status
coquena runtime download list
coquena runtime download status
```

Human tables use `ID`, `TYPE`, `NAME`, `STATUS`, `PROGRESS`, and `PATH`.
Runtime partial transfers and their JSON state are discovered beneath the
configured runtime directory.

The shorter `coquena download ...` forms remain accepted aliases for download management.
`coquena download` is the global download-management namespace, while `runtime download` and `model download` are resource-specific. Human `download list` output is tabular and includes percentage plus human-readable transferred/total sizes; `--json` retains the exact schema-version-1 automation contract. Pressing Ctrl+C during a CLI model resume leaves the durable job paused and resumable.

Incomplete models persist beside their configured targets as `<model>.gguf.partial` and `<model>.gguf.download.json`, are rediscovered after a cold restart exclusively from the configured models tree, and resume only against the commit SHA resolved at creation. Model management does not use `<home>/downloads`. Completed library entries consist of a `.gguf`, a `.json` sidecar, and a `.revision` file containing the resolved remote commit SHA. The JSON and `.revision` values must agree; `.revision` is remote provenance, not a local checksum (a future checksum uses `.sha256`).

The sidecar no longer persists the raw GGUF metadata map. It carries a compact
classification digest — declared type, architecture, size label, file type,
context and embedding lengths or projection dimension, an SSM presence flag, a
tokenizer digest, and the publisher's sampling recommendations — so a sidecar is
kilobytes rather than the megabytes the vocabulary arrays used to cost. The
sidecar schema version is therefore `2`. **This is a breaking change with no
migration:** a sidecar written under version `1` reads as invalid, its artifact
enumerates as `unreconciled`, and `coquena model reconcile --all` is the
supported recovery route. When a specific raw metadata value is needed it is
read from the `.gguf` on demand rather than from a cache.

GGUF metadata remains informational: Coquena never derives llama.cpp arguments or preset values from it. The publisher's sampling recommendations are displayed as advisory suggestions and never applied. A model's classification can contribute an advisory *computed* runtime constraint to a preset — an out-of-range `general.file_type`, the declared architecture, and the presence of hybrid attention/SSM keys — which is reported alongside the preset's own field-derived bounds and never assigns a value, selects a runtime, or blocks authoring.

Coquena best-effort caches the Hugging Face publisher avatar once at `<models>/<publisher>/publisher-avatar.<format>`. Models being downloaded or already installed use this local image; discovery may show the remote avatar. Avatar availability never determines whether a model download or publication succeeds. An installed artifact's classification is loaded lazily from its validated JSON sidecar when its collapsible Metadata/Value table is opened, which shows the declared type.

Neither the CLI nor the HTTP API returns raw GGUF metadata as a bulk payload. Inspection responses are a derived projection carrying identity, classification, and scalar metadata keys only; vocabulary arrays are never included. **The `includeRaw` inspection option has been removed:** a request carrying it is rejected as an unknown property with status `400`. Library entries and inspection and remote-discovery responses carry the `classification` instead, and remote discovery is available to HTTP clients at `POST /api/models/discover`.
