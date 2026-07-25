## Why

The Models-directory control in Settings is a non-functional mock, while the
CLI offers no equivalent setting. Coquena needs one persisted, validated
configuration value that both interfaces can use before model acquisition and
management are introduced.

## What Changes

- Add a persisted models-directory setting whose default is `models/` beneath
  the resolved Coquena application home.
- Add shared application behavior, a local HTTP API, and a CLI command to read
  and update that setting after `coquena init`.
- Replace the mock Settings control with API-backed manual path editing and a
  progressively enhanced browser folder-picker affordance.
- Validate configured paths as existing, absolute, writable directories before
  persisting them.
- **BREAKING**: evolve the initial `config/coquena.json` document beyond its
  current schema-version-1 shape while continuing to recognize installations
  initialized by prior versions.

## Capabilities

### New Capabilities
- `model-directory-configuration`: Persist, inspect, and update the models
  artifact directory consistently through the CLI and WebUI.

### Modified Capabilities
- `portable-initialization`: Initialize and recognize a configuration document
  that supplies a portable default models directory.

## Impact

- Affects `internal/installation`, a new shared configuration application
  boundary, CLI parsing/output in `cmd/coquena`, WebUI HTTP routing, and the
  Angular Settings component.
- Adds local JSON API endpoints and tests; no external service or dependency is
  introduced.
- Does not add model discovery, downloads, model execution, or privileged
  browser filesystem access.
