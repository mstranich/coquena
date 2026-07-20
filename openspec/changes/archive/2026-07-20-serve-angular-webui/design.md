## Context

The executable is currently a CLI with reserved `internal/api` and `internal/service` packages. The first web UI slice should be a small, local-only static server and must not introduce an HTTP API or privileged browser behavior.

## Decisions

### Reserve a minimal API namespace

Route `GET /api/health` before static-file handling. It returns status `200`, `text/plain`, and the body `ok` followed by a newline. This deliberately proves that the same server exposes backend endpoints for Angular without introducing runtime or configuration dependencies.

### Embed the production Angular output

Use Go `embed.FS` in a dedicated `internal/webui` package. Configure the Angular production output to be generated into a tracked-source-relative build directory consumed by that package, while keeping generated files ignored. The root build task must build the Angular app before building Go, so a clean checkout fails with a clear build prerequisite rather than silently shipping an empty UI.

### Serve through `net/http`

Use the standard library only. The server always binds to `127.0.0.1`; `--port` overrides the default `8080`. There is intentionally no host flag in this first slice, preventing accidental remote exposure and avoiding third-party dependency cost. `webui` prints the effective URL, blocks until shutdown, handles `Ctrl+C` through context cancellation, and returns a useful error if the listener cannot be created. Tests may use port `0` to obtain an ephemeral listener without changing the user-facing default.

### SPA-aware static handler

Serve existing embedded files with their MIME type derived from the file extension. For browser routes without a file extension, return the embedded `index.html`; missing asset-like paths return `404`. This supports Angular client-side routing without masking broken asset URLs or causing strict module MIME checks to fail.

### Keep lifecycle injectable

The command delegates to a small server function accepting a listener and context/shutdown hooks, allowing tests to verify routing and bind failures without hanging. OS signal handling belongs at the CLI boundary. A normal interrupt produces a clean zero exit status.

## Risks and Mitigations

- Generated output can be stale: `backend:build` depends on `web:build`, and tests verify the embedded marker.
- A port may already be occupied: fail before reporting a successful startup.
- Accidental network exposure: do not expose a host flag in this first slice.
- Angular output layout may change: test the required `index.html` and keep the embed directory contract explicit.

## Deferred

Additional API routing, authentication, CORS, configurable persistent settings, TLS, browser launch, remote access, host configuration, and production service integration.
