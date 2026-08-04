## Why

Coquena currently interprets GGUF `general.file_type` values through a
`ggml_type` tensor-encoding table, so valid model-level recipes can be rendered
as plausible but incorrect quantizations. Its duplicated tensor traversal also
misreads descriptor layout, making parameter counts and physical fallback
evidence unreliable; this must be corrected before artifact references or
catalog decisions depend on those values.

## Derivation

This change is derived from RFC `gguf-model-artifact-identity`, archived at
`docs/rfcs/archive/gguf-model-artifact-identity.md`. The RFC registry names this
change in `derivedChanges`; this proposal implements the RFC's accepted Change A
without reopening its settled command boundaries or expanding into the later
artifact-addressing, sidecar-authority, WebUI, or upgrade work.

## What Changes

- Introduce domain-specific handling for GGUF model recipes (`llama_ftype`) and
  tensor encodings (`ggml_type`) so raw integers cannot cross lookup domains
  implicitly.
- Replace duplicated GGUF offset arithmetic with one validated traversal that
  consumes metadata before tensor descriptors and decodes descriptor fields in
  their specified widths and order.
- Preserve the declared recipe when `general.file_type` supplies one, while
  reporting physical tensor composition as separate evidence rather than using
  it to invent recipe suffixes.
- Compute physical dominance by scalar parameter count over supported canonical
  structural tensor scopes, with an explicit unavailable result when no
  supported scope is present.
- Version sidecar-derived classification and parameter-count evidence, read the
  immediately preceding sidecar schema tolerantly, and let `model list` identify
  stale derivation without opening or hashing the artifact.
- Add opt-in `coquena model verify <ref> --repair-derived`, which repairs only
  derived evidence after integrity succeeds and leaves bytes, the digest
  baseline, provenance, attestation, drift, and ownership untouched.
- **BREAKING**: remove the `coquena model inspect` CLI surface while retaining
  the internal inspection projection used by download and verification.
- Correct capability classification and projector defaults that currently
  depend on the same cross-domain file-type interpretation.

Non-goals:

- No new `--all` operation or automatic catalog sweep.
- No canonical artifact-reference redesign, calibration-variant addressing, or
  ambiguity-policy change.
- No persisted remote-drift, attestation, or ownership axis redesign.
- No `model upgrade` command and no model-byte mutation.
- No attempt to reconstruct `_M`, `_S`, `_L`, calibration, or imatrix identity
  from tensor composition.
- No WebUI or browser-API behavior changes. The WebUI is postponed until further
  notice; this change serves the minimally functional CLI milestone.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `filetype-quantization-names`: Interpret model recipe identifiers in the
  `llama_ftype` domain and keep physical tensor encodings separate.
- `gguf-model-library`: Persist versioned derived evidence, tolerate the prior
  sidecar schema for repair, and compute classification/counts through one
  validated GGUF traversal.
- `gguf-model-interfaces`: Remove the `inspect` CLI surface, expose declared and
  physical evidence through verification, and add per-artifact
  `verify --repair-derived` without weakening default read-only verification.

## Impact

- Go model parsing, classification, capability inference, sidecar reading and
  writing, library enumeration, verification, download completion, and CLI
  rendering under `internal/model/` and `cmd/coquena/`.
- Tests and fixtures for GGUF metadata traversal, tensor scopes, model recipe
  decoding, sidecar compatibility, and repair safety.
- CLI documentation and README references to the removed inspection command.
  Existing browser APIs and Angular surfaces remain unchanged and are not
  acceptance criteria for this change.
- Existing uncommitted GGUF and CLI edits must be audited against this proposal
  rather than assumed correct or discarded wholesale.
