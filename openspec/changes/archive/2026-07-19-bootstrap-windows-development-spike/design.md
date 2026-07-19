## Context

The repository has product intent in `README.md` but no source tree, package manifests, task runner, or executable code. The daily development platform is Windows, and the immediate need is a repeatable workspace from which backend and configuration-UI work can begin. This spike crosses the Go and Angular toolchains, so the boundaries and orchestration commands need to be decided before scaffolding.

## Goals / Non-Goals

**Goals:**

- Make the repository restorable and verifiable on Windows using root-level Task commands.
- Materialize the complete README-proposed repository structure while clearly distinguishing initialized projects from planned, unimplemented packages.
- Produce a minimal Go executable and Angular application that compile and have automated baseline tests.
- Keep tool-specific commands available beneath Task so failures remain diagnosable.

**Non-Goals:**

- Define production architecture beyond the backend/frontend boundary already stated in the README.
- Implement any `llama-server` lifecycle or configuration behavior.
- Add Windows Service integration or release packaging.
- Prove Linux compatibility.
- Implement behavior inside planned packages that are outside this spike.

## Decisions

### Materialize the complete repository skeleton

All directories shown in the README's initial structure will exist after the spike. Because Git does not track empty directories, planned but unimplemented areas will contain a small tracked scope marker such as a `README.md` explaining their intended responsibility and that implementation is deferred. This makes the target structure visible without inventing APIs or placeholder production code.

### Use Task as the root orchestration interface

`Taskfile.yml` will expose small, composable commands for dependency restoration, formatting/linting where available, tests, builds, and development servers. A root verification task will compose backend and frontend verification.

Task is preferred over PowerShell-only scripts because it is already the project direction, gives Windows a first-class command surface, and can later execute equivalent commands on Linux. Native `go` and npm scripts remain usable directly; Task is orchestration, not a replacement for either ecosystem.

### Keep a single Go module at the repository root

The initial Go module will live at the root, with the executable in `cmd/coquena`. The complete `internal` directory skeleton will be present, but only packages required by the spike will contain Go source. `internal/api`, runtime adapters, services, and other future packages will remain documented placeholders until the changes that first require them.

This is simpler than a Go workspace or multiple modules and matches the intended single executable. The initial command will expose only enough behavior for a deterministic smoke test, such as version/help output, without inventing application functionality.

### Use a standard Angular application under `apps/web`

The frontend will be generated as a strict standalone Angular application in `apps/web`, with Angular tooling recorded as local development dependencies. The CLI initialization will explicitly select SCSS and zoneless change detection. Task and npm scripts will invoke the project-local CLI rather than require a globally installed Angular CLI.

The generated starter content will be reduced to a neutral Coquena shell and a baseline test. Component-local reactive state will use Angular signals rather than mutable fields that depend on Zone.js-triggered global change detection. RxJS remains available for asynchronous streams such as HTTP, SSE, cancellation, retries, and event composition; presentation-facing state derived from an Observable will be converted once and reused through `toSignal` from `@angular/core/rxjs-interop`. `zone.js` will not be retained as an application or test dependency, and tests will exercise the same zoneless scheduling model as production. Configuration forms and backend integration remain separate future changes.

### Use the local toolchain as the initial minimum baseline

The supported development floor for this spike will be Go 1.26.5, Node.js 24.18.0, npm 11.16.0, and Task 3.52.0, matching the versions already installed on the primary Windows workstation. Angular 22 will be selected because it supports the installed Node.js release. This intentionally favors immediate productivity over compatibility with older development environments.

### Use Unix repository conventions on every host

The repository will use `.editorconfig` to require UTF-8 text, LF line endings, and a final newline, and `.gitattributes` to normalize committed text to LF. Documentation, Task commands, configuration, and repository-relative paths will use Unix-style `/` separators regardless of whether development occurs on Windows or macOS. CRLF and Windows-only script conventions will not be introduced by default; an exception requires a concrete platform format that cannot consume LF.

Commit messages will follow Conventional Commits. The initialization checkpoints will use scoped `chore` commits for the web workspace, backend module, and root build orchestration. License selection remains explicitly deferred; no placeholder license will be generated.

### Use two reviewable initialization checkpoints

The Angular project initialization and the Go module initialization will each end in a clean, verified state suitable for an independent Git commit. The task list includes explicit review, staging, and commit checkpoints, but Task commands will not create Git commits automatically. This keeps history intentional and makes either scaffold easy to inspect or revert.

### Treat dependency restoration and verification as separate operations

Setup tasks will restore Go and npm dependencies. Verification tasks will assume dependencies are present and run deterministic checks without silently modifying manifests. A convenience bootstrap task may compose restoration followed by verification.

For npm, the committed lockfile and clean-install command will provide repeatability. For Go, `go.mod` and `go.sum` will be committed and `go mod download`/`go mod verify` will restore and verify modules.

### Keep orchestration shell-neutral while targeting Windows first

Task commands will prefer direct executable invocations and avoid Bash-, PowerShell-, and Command Prompt-specific syntax. Repository paths will be written with `/` separators. Commands must work through Task on the primary Windows environment and should remain natural on macOS or Unix-like IDE environments. This development portability does not expand the product scope: Windows service and runtime behavior remain first, and Linux runtime verification is deferred.

## Risks / Trade-offs

- **[Angular scaffolding creates substantial generated code]** → Generate only one application, remove demo content, and review every retained file as part of the spike.
- **[A dependency may assume Zone.js]** → Keep the initial dependency set minimal and verify every added Angular dependency in zoneless tests before adoption.
- **[`toSignal` subscribes immediately and can duplicate side effects]** → Convert each Observable once in its owning injection context and reuse the returned signal.
- **[Exact minimum versions reduce contributor compatibility]** → Accept this during the personal productivity spike and revisit only when another supported environment exists.
- **[Latest tool versions may require unsupported local runtimes]** → Record minimum tool versions and verify them before generation; pin project dependencies and commit lockfiles.
- **[Task may obscure underlying failures]** → Keep tasks thin and print/run standard Go and npm commands without custom wrappers.
- **[A complete tree can look implemented when it is not]** → Put scope markers, not production stubs, in deferred directories and label them clearly as unimplemented.
- **[Windows-first commands can accidentally become Windows-only]** → Avoid platform-specific scripting in shared tasks and explicitly document any unavoidable Windows constraint.
- **[Unix-style documentation can obscure Windows runtime behavior]** → Keep development conventions separate from explicit Windows service and process requirements in future specs.
