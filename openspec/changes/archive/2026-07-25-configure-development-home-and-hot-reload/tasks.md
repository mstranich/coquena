## 1. Application-home resolution

- [x] 1.1 Add one tested application-home resolver that selects a validated absolute `COQUENA_HOME` override or falls back to the resolved executable directory.
- [x] 1.2 Refactor initialization and runtime-manager/store entry points to use that resolver without changing the unset portable behavior.
- [x] 1.3 Add CLI and runtime tests for override selection, fallback, invalid relative/empty overrides, and isolation from the executable directory.

## 2. Development backend watch loop

- [x] 2.1 Add committed, repository-portable Air watcher configuration that rebuilds and restarts the WebUI backend from `.tmp/dev/` while using the persistent repository `home/` development home.
- [x] 2.2 Add `home/.gitkeep`, ignore all other `home/` contents and `.tmp/`, and initialize the development home before watched runtime operations begin.
- [x] 2.3 Extend toolchain checks and documentation with the required watcher and the development-home lifecycle.

## 3. Unified frontend/backend workflow

- [x] 3.1 Add an Angular development-server proxy configuration forwarding relative `/api` traffic to the local backend.
- [x] 3.2 Add a compiled Go supervisor that directly launches Air and Angular with inherited streams and a shared absolute `COQUENA_HOME`.
- [x] 3.3 Implement Windows Job Object ownership with kill-on-close and shared-console Ctrl+C, plus Unix process-group shutdown behind build tags.
- [x] 3.4 Add an interactive root `task dev` that builds and runs the supervisor without shell orchestration or `concurrently`.
- [x] 3.5 Add supervisor tests covering command/environment construction and platform process ownership behavior.

## 4. Development cleanup

- [x] 4.1 Make `task clean` preserve `home/.gitkeep`, remove all other `home/` content, and remove `.tmp/dev/` without retries or process killing.
- [x] 4.2 Verify cleanup after a stopped development session and verify locked-file failures remain visible.

## 5. Verification

- [x] 5.1 Verify startup, root PID reporting, backend reload, frontend HMR, proxy routing, and `home/` persistence.
- [x] 5.2 Verify one Ctrl+C and forced `devrun` termination both remove the supervised process tree and release ports 4200 and 8080 in a direct PowerShell console.
- [x] 5.3 Run Go formatting/tests, `task verify`, strict OpenSpec validation, and `git diff --check`.
- [x] 5.4 Record concrete acceptance results and deviations, then reconcile the implementation with all Change artifacts.
