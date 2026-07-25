## Context

The portable installation root is currently derived independently in initialization and runtime management from the resolved executable path. This preserves extracted-binary portability but fails under `go run`: each run may use a distinct temporary or cached executable directory. Angular already has a development server, but it has no API proxy, and the Taskfile has no backend restart loop or combined development command.

## Goals / Non-Goals

**Goals:**

- Preserve executable-relative portable behavior when no override is supplied.
- Let a developer select one persistent application home without modifying the product JSON configuration.
- Make initialization and all runtime-store consumers use exactly the same home-resolution policy.
- Offer one cross-shell Taskfile command that runs the two development servers, restarts Go on backend changes, and proxies `/api` to it.

**Non-Goals:**

- Changing the release installation layout, moving an executable, or selecting a system/user-wide default home.
- Persisting `COQUENA_HOME` into `config/coquena.json`, adding a CLI flag, or supporting several precedence layers in this change.
- Hot-reloading compiled embedded assets, changing production WebUI serving, or managing a llama-server process.

## Decisions

### Use `COQUENA_HOME` as an explicit environment override

A non-empty `COQUENA_HOME` names the entire application home. It MUST be an absolute path after cleaning; a relative or otherwise unusable value fails with an actionable diagnostic. If it is unset, the resolved executable parent remains the home. This keeps the release default intact while providing a conventional, process-scoped development override.

A `--home` CLI flag was rejected for now: every invocation, including watcher configuration, would need to carry it and it would enlarge every CLI grammar. A value inside `coquena.json` was rejected because the file itself must be located before it can be read.

### Centralize home resolution in `internal/installation`

Export one home-resolution function and inject it where tests require control. Initialization and runtime-manager construction consume that common boundary; no package independently calls `os.Executable` to derive state paths. This prevents `init` and runtime APIs from selecting different stores.

### Use a repository-local `home/` development directory

The repository SHALL contain an empty `home/` directory represented by a committed `home/.gitkeep` marker. `.gitignore` SHALL ignore all other `home/` content. The development task sets `COQUENA_HOME` to that directory's absolute path and invokes `coquena init` before launching watched services. This makes downloads and other mutable state durable across restarts, visible as one dedicated project location, and isolated from release-like `bin/` content.

### Supervise development processes with a compiled Go tool

Task builds `tools/devrun` into `.tmp/dev/devrun[.exe]` and executes it interactively. The supervisor starts Air and `npm --prefix apps/web run start` directly with shared standard streams and a common absolute `COQUENA_HOME`. It prints only its own PID and the two known root child PIDs. `concurrently`, Task parallel dependencies, shell wrappers, dynamic descendant polling, and process-name killing are excluded.

On Windows, npm is distributed as a `npm.cmd` shell wrapper rather than a native executable. To preserve the no-shell ownership guarantee, devrun resolves `node.exe` and invokes npm's adjacent `npm-cli.js` with the equivalent `--prefix apps/web run start` arguments. Unix invokes `npm` directly.

On Windows, devrun creates a Job Object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, assigns itself before starting children, and permits no breakaway. Children inherit membership. Ctrl+C is shared through the console; after a short grace period devrun calls `TerminateJobObject` as its final successful shutdown action. Closing the last non-inherited handle remains the abrupt-termination guarantee. Assignment failure aborts rather than degrading process ownership.

On Unix, Air and Angular run in separate process groups. Shutdown sends `SIGTERM`, waits the same grace period, and then sends `SIGKILL` to remaining groups. Platform behavior is separated by build tags without creating a general process-management framework.

Air continues to provide backend rebuild/restart and uses `build.entrypoint`. Development executables move to `.tmp/dev/`, while `home/` contains only persistent application state. Angular continues to proxy `/api` to `127.0.0.1:8080`.

### Clean the complete development workspace

`task clean` removes mutable contents of `home/` while preserving `home/.gitkeep`, and removes `.tmp/dev/`. It does not kill processes or retry locked files; the supported flow is stop `task dev`, then clean.

## Risks / Trade-offs

- [A user accidentally exports `COQUENA_HOME` for a release] → The explicit override is intentional and diagnostics report the selected home; documentation scopes it to development.
- [A malformed override targets an unsafe relative location] → Reject it before creating or reading any state.
- [The watcher starts before the backend port is ready] → Angular proxy requests fail normally until backend readiness; the backend listener output remains visible.
- [A Go source error stops the backend] → Air retains watch mode and reports the compiler error; Angular remains running.
- [New watcher tooling adds setup cost] → Keep it development-only and document it in `task tools`/README.
- [The parent Task process is killed without terminating devrun] → Document this as a known limitation; acceptance covers normal Ctrl+C and forced termination of devrun itself.
- [The parent environment uses a restrictive Job Object] → Abort with an actionable diagnostic rather than starting unsupervised children.

## Migration Plan

The change is backward compatible. Existing installations continue to resolve beside the executable when `COQUENA_HOME` is unset. A developer opts in through `task dev`, which initializes `home/`; deleting its ignored contents discards only development state while preserving the committed marker. Rollback removes the override/tooling and leaves existing executable-relative installations untouched.

## Open Questions

None. The supervisor architecture and acceptance boundary are fixed by this Change.
