## Why

Coquena exists to run several llama.cpp runtimes, switch them on and off, and
swap models to experiment. Resolving the relations between a model, its
companions, and the runtimes able to load it is therefore the product, not an
accessory. The catalog cannot do any of that today because it has no notion of
what a `.gguf` file *is*: a 461 MB vision projector and a 27B principal model
are both just "a model" in `coquena model list`, and nothing records that one
only makes sense alongside the other.

Every fact needed to fix this is already inside the files, and reading it is
almost free. `general.type` declares the artifact's role explicitly — `model`
or `mmproj` — and sits in the first 128 bytes, ahead of any tensor payload.
Verified live against Hugging Face with a Range request: 128 bytes were enough
to classify multi-gigabyte artifacts without downloading them. The same
`RandomAccessReader` already serves local files, so one classification
mechanism covers discovery, download, reconciliation, and listing.

The sidecar meanwhile pays an absurd price to cache nothing useful. A measured
real artifact carries a 13.2 MB `.json` beside a 5.3 GB `.gguf`; 99.9% of it is
three vocabulary arrays, while every key that identifies, classifies, or
constrains the model totals 150 bytes. Those arrays are persisted and never
shown — `InspectionResult` already omits arrays longer than 256 entries. Worse,
`Manager.Library()` decodes each sidecar in full on every scan, so listing ten
models parses roughly 130 MB of JSON, in direct conflict with the goal of
switching models quickly.

Inspection is broken in the same area. `coquena model inspect` always resolves
remotely and never touches the local library; `Manager.InspectLocal` has no
caller anywhere in the product. When a selector matches zero or several
artifacts the CLI reports only a count and discards the candidate list, because
the resolver's own errors carry a number and not the filenames. And when
inspection does succeed, the human output prints the resolved commit SHA alone
and throws away every metadata key it just decoded — so there is no way to
confirm by hand that a classification is correct.

## What Changes

- Classify every GGUF by its intrinsic `general.type`, reading the header
  through the existing bounded random-access reader, identically for a remote
  candidate and an installed file.
- Distinguish the intrinsic *type* of a file from the *role* it plays. `mmproj`
  is the only case where the two always coincide; a speculative draft model is
  `general.type = model` and nothing in its header says otherwise, so `draft`
  remains a role assigned in the preset.
- Record companion compatibility as computed criteria rather than hard pins to
  a file, mirroring the declared-vs-computed pattern `EvaluateRuntime` already
  uses for runtimes: a projector matches a principal when its projection
  dimension equals the principal's embedding length, and a draft matches a
  target when they share a tokenizer family.
- Derive a runtime capability constraint from the bound model itself — exotic
  quantization file types, the declared architecture, and the presence of
  hybrid attention/SSM keys — as a second source of *computed* constraint
  alongside the preset's own fields, reported as advisory information only.
- Replace the raw GGUF map in the sidecar with a compact classification digest.
  Raw metadata is no longer persisted, and no API or command returns it as a
  bulk payload; when a specific value is needed it is read from the `.gguf` on
  demand.
- Bump the sidecar schema version. A sidecar written under the previous version
  is no longer valid, so its artifact reports as `unreconciled` and is restored
  to the catalog through reconciliation like any other foreign file.
- Add `coquena model reconcile --all`, which scans the configured models
  directory, reads each header, classifies every artifact, computes the
  relations between them, and persists the resulting digests in one pass.
- Split inspection by locality: `coquena model inspect <path>` becomes local
  only and finally calls `InspectLocal`, while the new `coquena model discover
  <repo[:selector]>` explores a remote repository, always listing every
  candidate with its classification and printing the repository's model card as
  plain text.
- Render `coquena model list` as a tree, nesting each companion beneath the
  principal it is compatible with.
- Add an explicit flag to `coquena model download` that enqueues a principal
  together with its identified companions in one invocation, returning several
  job IDs.
- Carry the matching candidates inside the resolver's ambiguity and no-match
  errors so the CLI can list the available filenames, and print a classified
  metadata table for a successful inspection instead of the bare commit SHA.
- **BREAKING**: sidecars written before this change are invalid and their
  artifacts require reconciliation.
- **BREAKING**: `coquena model inspect` no longer accepts a repository or URL;
  remote exploration moves to `coquena model discover`.
- **BREAKING**: the `includeRaw` inspection option is removed from the HTTP API.

## Non-Goals

- Executing `llama-server`, or evaluating a preset against installed runtimes
  at run time. The model-derived runtime constraint is computed and displayed;
  nothing selects or launches a runtime.
- Assigning any metadata value to a preset field. Publisher sampling
  recommendations are displayed as advisory suggestions and never applied.
- Rendering the model card as styled ANSI. The first stage prints plain text.
- Automatic reconciliation. Every reconciliation, including `--all`, stays an
  explicit operator action.
- Any migration path for previously written sidecars. The project is
  pre-release and reconciliation is the supported route.

## Capabilities

### Modified Capabilities
- `gguf-model-library`: replace the raw GGUF map in the sidecar with a compact
  classification digest; bump the sidecar schema version; permit a computed,
  advisory runtime constraint derived from model metadata while keeping the
  prohibition on assigning metadata to preset fields.
- `gguf-model-resolution`: split inspection into local inspection and remote
  discovery; stop returning raw metadata as an inspection payload; carry
  matching candidates in resolution diagnostics.
- `gguf-model-interfaces`: add `coquena model discover`, `coquena model
  reconcile --all`, and companion-aware download; make `coquena model inspect`
  local and render a classified metadata table; render `coquena model list` as
  a tree; drop `includeRaw` from the HTTP API and load the classification
  digest rather than raw metadata in the Angular workspace.
- `gguf-model-downloads`: allow one download request to enqueue a principal
  artifact together with its companions as independent durable jobs.

## Impact

- Affects `internal/model` (classification, compatibility criteria, sidecar
  schema, library scan, resolver diagnostics, inspection projection) and
  `cmd/coquena` (`discover`, local `inspect`, `reconcile --all`, tree `list`,
  companion download).
- Affects `internal/presets` (companion roles and the model-derived runtime
  constraint) and `internal/webui` (removal of `includeRaw`, classification in
  library and inspection responses).
- Affects the Angular Models workspace, which loads a classification digest
  instead of a raw sidecar document.
- Requires updating `README.md`, which documents the removed raw-inspection
  behavior.
- Adds no new persisted document and introduces no SQLite or repository-wide
  metadata file; the per-artifact sidecar remains the only store, and becomes
  roughly three orders of magnitude smaller.
