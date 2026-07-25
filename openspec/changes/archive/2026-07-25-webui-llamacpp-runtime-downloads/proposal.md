## Why

Operators can safely discover and download host-compatible official llama.cpp
distributions from the CLI, but the WebUI Runtimes workspace still presents
mock inventory and cannot perform that workflow. Bringing this narrow,
already-validated operation to the local UI makes the control plane useful
without duplicating or weakening the runtime-management safeguards.

## What Changes

- Expose the existing official llama.cpp compatible-release discovery and local
  runtime inventory through the local WebUI API.
- Allow the WebUI to start a download of a selected, exact official primary
  runtime asset, show its real download/install progress, and then show the
  resulting local installation state.
- Allow an operator to remove one exact managed local runtime from the same
  workspace after an explicit destructive confirmation.
- Replace the mock runtime inventory and update affordance in the Runtimes
  workspace with an API-backed discovery and download experience.
- Share runtime-management application logic between the CLI and WebUI rather
  than reimplementing release classification, compatibility, or installation.
- Keep history browsing, runtime activation, upgrades, cancellation,
  persistent operation history, and support for additional runtime families out
  of scope. A future lifecycle change will persist desired runtime selection
  and derive restart-required state from the observed running runtime; this
  change never replaces or restarts a runtime.

## Capabilities

### New Capabilities
- `webui-llamacpp-runtime-management`: Local WebUI API and Runtimes workspace
  behavior for discovering compatible official llama.cpp distributions,
  inspecting local installations, and downloading an exact selected asset.

### Modified Capabilities
- `webui-serving`: Route the new runtime-management API independently of
  embedded static files and Angular client-route fallback.

## Impact

- Go runtime application boundary currently implemented in `cmd/coquena`.
- `internal/webui` HTTP routing and handler tests.
- Angular Runtimes view, typed API client, and component tests.
- Existing CLI runtime artifact behavior remains the authoritative safety and
  compatibility policy; no new external service or browser privilege is added.
