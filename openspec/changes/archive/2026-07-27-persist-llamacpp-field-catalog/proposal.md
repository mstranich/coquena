## Why

The llama.cpp field catalog exists only in browser memory and is lost on every
page reload, so no operator can build a durable configuration vocabulary. The
catalog is also the prerequisite for presets, and presets are the artifact that
Coquena must eventually render as a `--models-preset` INI file for a
`llama-server` router instance.

Upstream documents that INI keys are llama.cpp argument names without leading
dashes, and warns that flag and parameter names have changed across builds.
A durable catalog therefore cannot be a flat list of names: each field must
declare its kind, so a value can be validated before it reaches the INI, and
must record which llama.cpp builds admit it, so a later preset can be validated
against the runtime it will actually run on.

## What Changes

- Persist one llama.cpp field catalog beneath the application home, published
  atomically with its own schema version.
- Identify every field by its canonical long INI key name, with a required
  immutable kind and optional short-argument and environment variable aliases
  recognized as the same field rather than as separate entries.
- Give every field a provider-qualified availability declaration so a field can
  state the official llama.cpp build range that admits it.
- Add a `coquena field` command family to define, remove, list, and show fields,
  and a `/api/fields` resource collection analogous to it.
- Support upsert through `field define --overwrite` and `PUT /api/fields/<name>`
  so a catalog can be seeded and shared as a replayable script.
- Add a replayable seed script that populates a starter catalog through the CLI.
- Replace the Angular in-memory field catalog with API-backed state, keeping the
  existing editor interactions unchanged.
- **BREAKING**: the field catalog is no longer a browser-memory-only slice.

## Capabilities

### New Capabilities
- `llamacpp-field-catalog-persistence`: Persist and serve a durable llama.cpp
  field catalog with per-field kind and build availability, managed through
  analogous CLI and HTTP interfaces.

### Modified Capabilities
- `llamacpp-configuration-catalog-ui`: Source the field catalog from the local
  API instead of browser memory, and narrow the browser-memory-only constraint
  so that it covers presets alone.

## Impact

- Affects `internal/installation`, a new llama.cpp field catalog package, WebUI
  HTTP routing in `internal/webui`, CLI parsing in `cmd/coquena`, and
  `apps/web/src/app/catalog-store.ts` with the Fields workspace.
- Adds one new configuration document, local JSON API endpoints, and one seed
  script; no external service or dependency is introduced.
- Does not persist presets, render an INI file, register custom runtime builds,
  execute `llama-server`, embed a curated catalog in the executable, or derive
  fields from a runtime binary or its source.
