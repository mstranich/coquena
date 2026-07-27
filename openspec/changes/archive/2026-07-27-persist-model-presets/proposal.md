## Why

The field catalog is durable, but presets — the artifact that actually configures
a model — still live only in browser memory and vanish on reload. A field
vocabulary with nothing authored against it configures nothing.

Presets are also where the two associations Coquena needs are finally expressed.
Upstream documents that a `--models-preset` INI section name *is* the model
identifier served at `/v1/models`, that `[*]` supplies inherited defaults, and
that parameter names have changed between builds. So a preset must bind to a
model, and must state which llama.cpp builds can run it — while the INI itself
can represent neither binding once flattened. The store must therefore hold what
the INI cannot, and the INI becomes a derived artifact rendered from it.

## What Changes

- Persist presets beneath the application home as one atomically published
  document with its own schema version, holding a reserved `global` preset that
  supplies inherited defaults plus any number of model presets.
- Identify a preset by a validated kebab-case name that is also its INI section
  name and its served model identifier, with a separate optional display title.
- Bind a preset to a model through the existing `user/repo:QUANT` grammar,
  persisting both the requested reference and the resolved artifact so a preset
  survives a moved model directory and renders even when nothing is installed.
- Bind a preset to a runtime as a capability constraint rather than an identity
  pin, and derive the constraint that the preset's own fields already imply.
- Validate every preset value against its field's kind before it is stored.
- Add a `coquena preset` command family and an analogous `/api/presets` resource
  whose subresources mirror it one to one, reading effective values with their
  inheritance source and writing only a preset's own layer.
- Render a deterministic `--models-preset` INI for a named runtime, reported
  rather than written unless writing is explicitly requested.
- Refuse to remove a catalog field whose values exist in a persisted preset,
  cascading the removal through both documents when it is forced.
- List installed models through the CLI and resolve a model reference against
  the installed library without network access.
- **BREAKING**: presets are no longer a browser-memory-only slice.

## Capabilities

### New Capabilities
- `model-preset-persistence`: Persist model presets with global inheritance,
  model and runtime bindings, and field-validated values, managed through
  analogous CLI and HTTP interfaces and rendered to a `--models-preset` INI.

### Modified Capabilities
- `llamacpp-configuration-catalog-ui`: Source presets from the local API instead
  of browser memory, and drop the browser-memory-only constraint now that
  nothing in the slice is browser-memory only.
- `model-preset-inheritance-ui`: Label inherited and overridden values from the
  inheritance source reported by the API rather than from browser state.
- `llamacpp-field-catalog-persistence`: Protect fields whose values exist in a
  persisted preset from unforced removal, and cascade a forced removal through
  the presets.
- `gguf-model-interfaces`: List installed models through the CLI.
- `gguf-model-resolution`: Resolve a model reference against the installed
  library without contacting Hugging Face.

## Impact

- Affects a new model-preset package, model resolution and CLI in
  `internal/model` and `cmd/coquena`, field removal in `internal/fields`, WebUI
  HTTP routing in `internal/webui`, and the presets workspace in `apps/web`.
- Adds one configuration document, one derived INI artifact beneath `data/`, and
  local JSON API endpoints; no external service or dependency is introduced.
- Does not execute `llama-server`, register custom runtime builds, add a
  revision axis to the short model reference grammar, add an exact-match model
  selector, or read an INI file written outside Coquena.
