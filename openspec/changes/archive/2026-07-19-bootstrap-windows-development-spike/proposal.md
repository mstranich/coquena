## Why

Coquena currently consists only of its product README, so there is no repeatable way to install dependencies, build the backend, or start frontend development on Windows. This spike establishes the smallest usable development workspace so the project can begin delivering functionality for its primary operator and platform.

## What Changes

- Add a root Task configuration as the single documented entry point for workspace setup, validation, backend work, and frontend work.
- Materialize the complete repository skeleton proposed by the README, using tracked scope markers where a planned directory has no implementation yet.
- Initialize and verify a complete standalone Angular project in `apps/web`, using SCSS, signals for local reactive state, and zoneless change detection, ready to be committed as an independent checkpoint.
- Initialize and verify the Go module and minimal `cmd/coquena` executable, ready to be committed as a second independent checkpoint.
- Keep repository and development conventions Unix-style on Windows and macOS—LF text, forward-slash paths, and shell-neutral Task commands—while Windows remains the first product runtime target.
- Establish the installed local tool versions as the minimum supported development baseline: Go 1.26.5, Node.js 24.18.0, npm 11.16.0, and Task 3.52.0.
- Establish repository text conventions with LF line endings, a final newline, and Conventional Commit history.
- Verify that a clean checkout can restore dependencies and run the initial backend and frontend checks through Task.
- Keep generated files and dependency caches out of version control.

### Non-goals

- Managing or launching `llama-server`.
- Defining the configuration model or configuration UI.
- Implementing the administration API, authentication, logs, SSE, service installation, or frontend/backend embedding.
- Supporting Linux-specific setup during this spike.
- Implementing speculative behavior inside placeholder backend packages.
- Selecting the project license.

## Capabilities

### New Capabilities

- `development-workspace`: A Windows developer receives the complete planned repository skeleton, can restore and validate the initialized Go and Angular projects through root Task commands, and can review them as separate commit checkpoints.

### Modified Capabilities

None.

## Impact

- Adds root development configuration such as `Taskfile.yml`, `.gitignore`, and Go module files.
- Adds the complete `cmd/coquena`, `internal`, `apps/web`, `docs`, and `schemas` skeleton described by the README; unimplemented areas contain tracked scope markers rather than speculative source code.
- Introduces Go, Node.js/npm, Angular CLI project dependencies, and Task as development prerequisites.
- Establishes Windows as the first supported development environment while leaving room to add Linux verification later.
