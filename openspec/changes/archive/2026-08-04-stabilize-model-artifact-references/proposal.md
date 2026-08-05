## Why

Coquena currently accepts one textual selector slot for both quantization labels and exact GGUF filenames, but its consumers do not consistently preserve the distinction between a logical class and a concrete artifact. When multiple calibration or production variants share one quantization, single-artifact operations can become ambiguous, revision pins can be ignored locally, and durable preset bindings can report ambiguity as staleness.

## Derivation

This change is derived from RFC `gguf-model-artifact-identity`, archived at
`docs/rfcs/archive/gguf-model-artifact-identity.md`. It implements the RFC's
artifact-reference work after the evidence foundation delivered by
`correct-gguf-quantization-evidence`; the RFC registry records both changes in
`derivedChanges`.

## What Changes

- Define `user/repo[@revision]:QUANT` as a logical quantization selector with cardinality `0..N` and `user/repo[@revision]:filename.gguf` as the canonical repository artifact name with cardinality `0..1`.
- Preserve the existing single `:selector` grammar; quantization and exact filename are alternatives, not nested selectors, and no slash or variant-tag grammar is introduced.
- Require singleton operations to reject ambiguous logical selectors and report the canonical filename references of every matching artifact rather than choosing implicitly.
- Make installed-library resolution enforce an explicit `@revision` against persisted resolved provenance instead of silently discarding it.
- Make model listings and diagnostics expose canonical artifact references alongside any logical quantization selector.
- Stabilize preset and companion bindings around the resolved canonical artifact while retaining the authored selector, and distinguish resolved-same, resolved-different, missing, and ambiguous outcomes instead of collapsing them into a stale boolean.

Non-goals:

- No `:QUANT#variant`, `:QUANT-variant`, slash-form artifact address, or inferred calibration taxonomy.
- No redesign of sidecar drift, attestation, ownership, or authority writers.
- No `model upgrade`, model-byte replacement, bulk operation, WebUI redesign, or automatic rebinding.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `gguf-model-resolution`: Define selector cardinality, canonical exact-filename references, local revision enforcement, and distinct resolution outcomes.
- `gguf-model-interfaces`: Make CLI listing and singleton model operations present and require canonical artifact references when logical selectors are ambiguous.
- `model-preset-persistence`: Preserve authored selectors while binding principals and companions to canonical artifacts and report ambiguity separately from actual staleness.

## Impact

- Reference parsing and remote/offline resolution under `internal/model/`.
- Installed-library matching, ambiguity diagnostics, model list output, and CLI JSON contracts under `cmd/coquena/` and model interface code.
- Preset principal/companion binding persistence and stale-state evaluation under `internal/presets/`.
- Tests for quantization classes with multiple filenames, exact-filename singleton resolution, explicit revision pins, stable bindings, and diagnostic candidate references.
- Existing persisted authored references remain valid; the change clarifies their cardinality and canonicalizes resolved artifact identity without inventing a migration to a new grammar.
