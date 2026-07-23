## Why

Coquena already detects the host through `coquena system`, but the local Web UI cannot consume that result and instead presents a hard-coded hardware summary. Operators need one trustworthy, current hardware view in the Dashboard and Runtimes workspaces without each page repeatedly probing the machine.

## What Changes

- Reuse the existing normalized Go system-inspection result to serve a read-only local HTTP hardware-capabilities API.
- Add a reusable Angular hardware-capabilities widget to Dashboard and Runtimes, replacing the mock hardware summary in Runtimes.
- Add an Angular API client that caches successful hardware reports in `sessionStorage` with a bounded TTL, refetching only after expiry, invalid/missing cache data, or an unsuccessful request.
- Keep the CLI `coquena system` output contract and the localhost-only Web UI boundary intact.

## Capabilities

### New Capabilities
- `system-capability-api`: Expose the normalized host-capability report to local HTTP clients through the embedded Web UI server.
- `hardware-capability-widget`: Fetch, cache, and render the host-capability report through a reusable Angular widget.

### Modified Capabilities
- `webui-serving`: Add the hardware-capabilities API endpoint to the existing local Web UI server.
- `mock-control-plane-navigation`: Replace the Runtimes mock hardware summary and add the actual detected-hardware view to Dashboard and Runtimes.

## Impact

- Go: `internal/webui` will depend on the existing `internal/system` inspection contract and expose a JSON endpoint with tests.
- Angular: new typed API/cache service and reusable widget will be integrated into the Dashboard and Runtimes views with component tests.
- No new external service, privileged browser operation, persistent storage, or change to the `coquena system --json` schema is required.
