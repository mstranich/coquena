## 1. Establish the Task workflow

- [x] 1.1 Document and verify Go 1.26.5, Node.js 24.18.0, npm 11.16.0, and Task 3.52.0 as the minimum supported Windows development versions.
- [x] 1.2 Add a root `Taskfile.yml` with discoverable `default`, `tools`, `deps`, and `verify` entry points using direct, shell-neutral executable invocations and Unix-style repository paths; initially let unimplemented composed tasks fail clearly.
- [x] 1.3 Add focused Task namespaces for `backend:*` and `web:*`, keeping each task as a thin invocation of a standard Go or npm command without Bash, PowerShell, or Command Prompt-specific syntax.
- [x] 1.4 Add root ignore rules for Go and Angular build outputs, npm dependencies, coverage, local environment files, and common Windows/editor artifacts; verify representative generated paths are ignored.
- [x] 1.5 Add `.editorconfig` and `.gitattributes` rules for UTF-8, LF, and final newlines with no default CRLF exceptions; verify Git normalizes a representative Windows-created text file to LF.
- [x] 1.6 Document the Windows prerequisites, Conventional Commit convention, intentionally undecided license, and root Task command contract in the README without documenting commands that do not yet work.
- [x] 1.7 Materialize the complete README-proposed directory skeleton, adding tracked scope markers to deferred `internal`, `docs/decisions`, and `schemas` directories so a clean checkout preserves the structure without speculative implementation.

## 2. Initialize and checkpoint the Angular frontend

- [x] 2.1 Select Angular 22 for the verified Node.js 24.18.0/npm 11.16.0 environment and record the resolved dependencies, including RxJS, in the package manifest and lockfile.
- [x] 2.2 Generate a complete strict standalone Angular application directly under `apps/web` with routing, SCSS, and the CLI's zoneless option enabled; keep the Angular CLI as a local development dependency and prevent it from creating a nested Git repository or commit.
- [x] 2.3 Verify `zone.js` is absent from runtime dependencies and build/test polyfills, and that the application does not configure `provideZoneChangeDetection`.
- [x] 2.4 Replace generated demonstration content with a minimal Coquena administration shell whose local reactive state uses signals; add one small RxJS Observable converted once with `toSignal`, and test its observable zoneless behavior without duplicating subscriptions.
- [x] 2.5 Add `web:deps`, `web:test`, `web:build`, and `web:serve` root Task commands using npm scripts and the project-local Angular CLI; compose deterministic checks under `web:verify`.
- [x] 2.6 Restore from the committed lockfile, run tests non-interactively in the zoneless configuration, produce a production build, and verify generated artifacts remain ignored.
- [x] 2.7 Generate a disposable component and verify the workspace defaults its stylesheet to SCSS, then remove the disposable component.
- [x] 2.8 Review and stage only the initialized `apps/web` project and create its dedicated Conventional Commit (for example, `chore(web): initialize Angular workspace`) after verification, leaving root orchestration changes for the final integration checkpoint.

## 3. Initialize and checkpoint the Go backend

- [x] 3.1 Initialize one Go module at the repository root using the repository's canonical import path and generate its module manifests.
- [x] 3.2 Add a failing test for the minimal deterministic `coquena` command behavior selected for the spike, then implement only enough under `cmd/coquena` to make it pass.
- [x] 3.3 Add `backend:deps`, `backend:fmt-check`, `backend:test`, and `backend:build` root Task commands and compose them under `backend:verify`.
- [x] 3.4 Run the backend workflow from a clean dependency cache where practical and confirm it restores modules, formats, tests, and builds without requiring Node.js.
- [x] 3.5 Confirm deferred backend directories contain only scope markers and no speculative Go packages.
- [x] 3.6 Review and stage only the Go module manifests and `cmd/coquena` initialization, then create the dedicated Conventional Commit (for example, `chore(backend): initialize Go module`) after verification, leaving root orchestration changes for the final integration checkpoint.

## 4. Integrate and prove the Windows workspace

- [x] 4.1 Compose `deps` from `backend:deps` and `web:deps`, and compose `verify` from `backend:verify` and `web:verify`, preserving each underlying command's output and exit status.
- [x] 4.2 Add a `bootstrap` Task command that checks prerequisites, restores both workspaces, and runs the complete verification workflow.
- [x] 4.3 From a clean Windows checkout, run the documented bootstrap flow and confirm dependency directories and build outputs remain untracked by Git.
- [x] 4.4 Inspect Task and documentation paths for `\\` separators or host-specific shell syntax and replace them with Unix-style `/` paths or direct portable invocations.
- [x] 4.5 Update the README with only the commands and directory structure proven by the clean-checkout exercise, including separate backend and frontend development loops.
- [x] 4.6 Run `openspec validate --all`, capture any deferred issues as future changes rather than expanding this spike, and confirm every scenario in the `development-workspace` spec has verification evidence.
- [x] 4.7 Review the final root orchestration and complete directory skeleton as an integration checkpoint, then create a separate Conventional Commit (for example, `chore(build): orchestrate workspace with Task`) for Task coordination and remaining repository-level files.

## Deferred follow-up

- Angular 22's current transitive Vite toolchain reports low-severity advisory `GHSA-g7r4-m6w7-qqqr` for a nested esbuild version on Windows. `npm audit fix` cannot currently resolve it without an upstream dependency update; track it separately and keep the development server bound to trusted local interfaces.
