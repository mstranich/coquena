## Verification results — 2026-07-25

### Automated

- `go test ./...`: passed, including the Windows integration test that force-terminates a helper supervisor and verifies its Job Object child disappears.
- Linux cross-compilation of `tools/devrun`: passed.
- `task verify`: passed (format, lint, 18 frontend tests, frontend production build, Go tests, and backend build).
- `openspec validate configure-development-home-and-hot-reload --strict`: passed.
- `git diff --check`: passed.

### Development acceptance completed

- `task dev` started `devrun`, Air, Angular, and the backend; ports 4200 and 8080 listened.
- The supervisor emitted the required root PID line.
- `GET http://127.0.0.1:4200/api/health` returned `200 ok` through the Angular proxy.
- Touching `internal/installation/installation.go` caused Air to replace backend PID 40760 with PID 37484; the health endpoint returned `ok` after restart.
- Force-terminating devrun PID 49804 removed the recorded Air, Angular, and backend processes and released both ports.
- A direct PowerShell run reported devrun PID 56756, Air PID 45500, and web PID 37304. One Ctrl+C reached Task, Air shut down, devrun returned the conventional interrupt status 130, all three recorded root PIDs disappeared, and neither port 4200 nor 8080 remained listening.
- `task clean` preserved only `home/.gitkeep` and removed `.tmp/dev/`.
- A direct PowerShell persistence/cleanup check showed the same initialized configuration and llama.cpp runtime tree across development restarts. After `task clean`, `home/.gitkeep` still existed, every mutable entry was gone, and `.tmp/dev/` did not exist. The cleanup script performs no retry, suppression, or process killing, so filesystem lock errors propagate through Node and Task unchanged.
- A direct PowerShell hot-reload check changed Angular HTML and logged `Page reload sent to client(s)`. It also changed `internal/webui/server.go`; Air detected each change, stopped the old backend, rebuilt, and restored the listener on `127.0.0.1:8080`.

### Still requiring a direct interactive PowerShell console


### Deviation

Windows cannot execute npm without a shell through its normal `npm.cmd` launcher. The supervisor therefore starts `node.exe` with npm's adjacent `npm-cli.js` and the exact equivalent arguments. This preserves the required shell-free supervised tree. Unix still starts `npm` directly.
