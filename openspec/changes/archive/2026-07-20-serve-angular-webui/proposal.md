## Why

Coquena already contains the Angular administration application, but the `webui` command does not exist. A user cannot start Coquena and reach the UI through the product executable, which blocks the first end-to-end backend/frontend workflow.

## What Changes

- Add `coquena webui` to start a local HTTP server.
- Serve the production Angular application from the Go executable.
- Expose a minimal `GET /api/health` endpoint returning `ok` so the frontend/API boundary is established.
- Bind only to localhost with a deterministic default port and an optional `--port` override.
- Print the effective listening URL and shut down cleanly on `Ctrl+C`.
- Keep static asset lookup, SPA fallback, and startup failures observable and testable.
- Document the command and build/serve workflow.

Non-goals: authentication, CORS, domain API endpoints beyond the health check, WebSocket/SSE, runtime lifecycle management, browser auto-launch, remote binding, host configuration, and development hot reload.

## Capabilities

### New Capabilities
- `webui-serving`: Start a local HTTP server that exposes the compiled Angular application.

### Modified Capabilities
- None.

## Impact

- Go CLI dispatch and a new internal web UI/server package.
- Angular production build output and root Task orchestration.
- Embedded static assets increase the distributed executable and require the frontend build before the Go build.
- README and automated Go integration/unit tests.
