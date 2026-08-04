## 1. Baseline and enum domains

- [x] 1.1 Audit the existing uncommitted parser, CLI, and test hunks against this change; identify pre-existing frontend hunks as out of scope and leave them untouched while recording which in-scope hunks are retained, replaced, or reverted
- [x] 1.2 Add distinct `LlamaFileType` and `GGMLType` Go types with domain-named lookup functions and tests for overlapping value `15`, unknown values, deprecated recipes, and explicit conversions at binary/JSON boundaries
- [x] 1.3 Replace the mixed `FileTypeNames` lookup contract and audit every display, capability, offline-default, and projector call site so each consumes the correct typed domain

## 2. Shared GGUF traversal

- [x] 2.1 Add scanner fixtures covering metadata scalars, skipped arrays, 64-bit counts, multidimensional tensor descriptors, descriptor order, and malformed/truncated input
- [x] 2.2 Implement one validated random-access GGUF traversal that consumes all metadata before yielding typed tensor descriptors
- [x] 2.3 Migrate classification, inspection, and download-record construction to projections of the shared traversal and remove their independent offset arithmetic
- [x] 2.4 Migrate parameter counting to the shared descriptor stream with overflow-safe dimension products and empty-table behavior

## 3. Declared and physical quantization evidence

- [x] 3.1 Decode `general.file_type` as a declared `LlamaFileType`, including defensive guessed-flag handling that never promotes a guessed `_M` suffix into declared identity
- [x] 3.2 Compute a `GGMLType` physical histogram weighted by scalar parameters only for `blk.`, `enc.blk.`, `dec.blk.`, `v.blk.`, and `a.blk.` tensor scopes
- [x] 3.3 Add counterexample tests proving embeddings/output tensors, tensor-count weighting, and encoded-byte weighting cannot determine the structural result, and return unavailable when no supported scope exists
- [x] 3.4 Update list rendering, capability inference, and projector defaults to consume declared and physical evidence without collapsing them into one quantization value

## 4. Versioned sidecar derivation

- [x] 4.1 Define sidecar schema 3 and derivation algorithm version 1 with one persisted derivation block for classification, parameter count, computation time, and optional physical evidence
- [x] 4.2 Make current download, reconciliation, and publication paths write schema 3 without duplicating classification outside the derivation block
- [x] 4.3 Add a tolerant schema-2 reader that preserves source, artifact, and digest evidence while marking unversioned derivation stale; keep incompatible schema 1 on its existing unreconciled path
- [x] 4.4 Update `model list` and its JSON/human tests to identify stale derivation from sidecar versions alone without model I/O, hashing, network access, or mutation

## 5. Verification and repair

- [x] 5.1 Extend verify results and human/JSON output to present integrity, declared recipe, physical composition, and derivation freshness as separate outcomes
- [x] 5.2 Add CLI parsing and usage for per-artifact `model verify <ref> --repair-derived`, explicitly rejecting `--all` and invalid flag combinations
- [x] 5.3 Implement one-pass integrity-gated derivation repair using atomic sidecar replacement, refusing mismatch and no-baseline cases
- [x] 5.4 Add preservation tests proving repair changes only the derivation block and leaves model bytes, digest baseline, source/provenance, attestation, drift, ownership, and unrelated fields unchanged
- [x] 5.5 Add schema-2 migration tests proving successful repair publishes schema 3 while ordinary verify and list remain read-only

## 6. CLI surface and documentation

- [x] 6.1 Remove `model inspect` from CLI dispatch, usage, CLI tests, and README examples while retaining the internal `Inspection` type and download projection
- [x] 6.2 Update `docs/model-commands.md` and the GGUF library README section to move accepted-target behavior to implemented only after its tests pass
- [x] 6.3 Confirm by diff review that no Angular component, browser workflow, or browser API contract changed and that existing frontend working-tree edits remain outside this change

## 7. Validation

- [x] 7.1 Run focused Go tests for enum mappings, GGUF traversal, classification, sidecar library behavior, verification, capability inference, CLI rendering, and usability
- [x] 7.2 Run the full repository bootstrap/test/format workflow while treating passing WebUI checks only as regression protection, not as a request to change or extend the postponed WebUI
- [x] 7.3 Validate a real Ornith Q4_K_M artifact through a documented optional acceptance command when locally available; otherwise record why synthetic fixtures are the reproducible default
- [x] 7.4 Review the final diff against every modified requirement and confirm that artifact addressing, remote authority redesign, upgrade, and bulk operations remain out of scope
