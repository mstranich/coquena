## Why

Coquena stores its portable state beside the running executable. That is correct for a distributed binary, but it makes `go run` unsuitable for development because its executable path is temporary or cached and therefore does not retain the initialization layout between restarts.

## What Changes

- Add an explicit `COQUENA_HOME` environment-variable override for Coquena's application home; when absent, retain the executable-relative portable default.
- Make `init` and runtime-store operations resolve the same selected home.
- Provide one development task that starts the Angular development server and a Go backend with automatic restart, using a persistent repository `home/` development directory and a frontend API proxy.
- Add a compiled Go development supervisor using Windows Job Objects and Unix process groups so Ctrl+C and forced supervisor termination clean up the complete supervised tree.
- Separate persistent state in `home/` from disposable development executables in `.tmp/dev/`, and make `task clean` remove both while preserving `home/.gitkeep`.
- Preserve portable-release behavior and do not make a home override part of the product configuration JSON.

## Capabilities

### New Capabilities

- `development-hot-reload`: Start the Angular UI and Go WebUI backend together in development mode, reload them on source changes, proxy browser API traffic to the backend, and retain development state in `home/`.
- `application-home-override`: Select a persistent application-home directory through `COQUENA_HOME` while retaining the resolved-executable directory as the default.

### Modified Capabilities

- `portable-initialization`: Resolve portable initialization at the selected application home rather than unconditionally beside the executable.

## Impact

- `internal/installation`, `internal/runtime`, CLI diagnostics and their tests.
- `Taskfile.yml`, `.gitignore`, the repository `home/.gitkeep` marker, Angular development-server proxy configuration, and development documentation.
- Adds an internal Go supervisor and `golang.org/x/sys/windows`; removes the temporary `concurrently` dependency. Production build and embedded assets remain unchanged.
