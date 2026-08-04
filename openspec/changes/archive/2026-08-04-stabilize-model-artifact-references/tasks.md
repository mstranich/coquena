## 1. Reference contract baseline

- [x] 1.1 Audit every remote, offline, CLI, preset, and companion reference parser/resolver call site and record where revision, selector kind, or candidate identity is currently discarded
- [x] 1.2 Add table-driven grammar tests for repository-only, `:QUANT`, `:filename.gguf`, optional `@revision`, invalid slash forms, and malformed multiple-selector inputs
- [x] 1.3 Introduce one parsed-reference representation that preserves repository, optional revision, selector text, and explicit absent/quantization/exact-filename selector kind

## 2. Canonical artifact references

- [x] 2.1 Add a canonical formatter for installed and remotely resolved artifacts using repository, available resolved revision, and exact filename
- [x] 2.2 Add tests proving remote-provenance artifacts format as `user/repo@revision:filename.gguf` while local-provider artifacts without a revision remain exact-filename references
- [x] 2.3 Ensure canonical formatting never derives quantization, calibration, or variant identity from filename tokens

## 3. Installed resolution and cardinality

- [x] 3.1 Replace locally discarded revision output with candidate filtering that enforces explicit `@revision` against persisted resolved provenance
- [x] 3.2 Preserve unpinned local resolution without synthesizing a `main` constraint and test sidecars recorded at non-main revisions
- [x] 3.3 Return structured missing, unique, and ambiguous resolution results with canonical candidate references for ambiguity
- [x] 3.4 Add evaluation of an expected canonical artifact as resolved-same, resolved-different, missing, or ambiguous, including the case where a new sibling joins the same quantization class
- [x] 3.5 Audit singleton model operations so each either receives one exact artifact or refuses ambiguity without implicit variant selection

## 4. CLI and model interfaces

- [x] 4.1 Update model list human output to report each installed artifact's canonical reference and present its logical quantization selector only as a secondary class
- [x] 4.2 Update model list JSON contracts and fixtures with canonical reference, exact filename, resolved revision, and optional logical selector
- [x] 4.3 Update missing and ambiguity diagnostics across model commands to name actionable canonical exact-filename references
- [x] 4.4 Add CLI tests with two calibration variants sharing one quantization and prove singleton commands refuse the logical selector but accept either canonical filename

## 5. Preset and companion bindings

- [x] 5.1 Adapt persisted principal bindings to derive or store a canonical artifact identity separately from the unchanged authored reference
- [x] 5.2 Replace boolean stale evaluation with explicit same/different/missing/ambiguous states and migrate every preset CLI/API/rendering consumer
- [x] 5.3 Add regression tests proving installation of a same-quantization sibling does not mark the previously resolved canonical artifact stale or rewrite the binding
- [x] 5.4 Require explicit rebinding before a uniquely resolved replacement becomes the persisted artifact and test that reads remain non-mutating
- [x] 5.5 Apply the same canonical singleton boundary to exact companion assignments while preserving criterion-based companion discovery as a set
- [x] 5.6 Verify existing persisted preset fixtures remain readable without an automatic document migration

## 6. Documentation and validation

- [x] 6.1 Update model-command and preset documentation with the two selector alternatives, cardinalities, revision behavior, and canonical ambiguity examples
- [x] 6.2 Confirm documentation introduces no slash address, nested selector, variant-tag grammar, sidecar-authority persistence, upgrade command, or bulk operation
- [x] 6.3 Run focused Go tests for parsing, remote/offline resolution, model listing, CLI diagnostics, preset binding, companion assignment, and rendering
- [x] 6.4 Run the full repository formatting, bootstrap, and test workflow and review the final diff against every modified requirement
