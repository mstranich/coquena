## MODIFIED Requirements

### Requirement: Resolve a model reference against the installed library
Coquena SHALL resolve model references against the installed library without contacting any network service using the grammar `user/repo[@revision]:selector`, where `selector` is either a logical quantization selector or an exact `.gguf` filename. A quantization selector SHALL represent a class with cardinality `0..N`; an exact filename selector SHALL represent a canonical repository artifact name with cardinality `0..1`. A filename selector SHALL match by case-insensitive exact equality, while a quantization selector SHALL retain the established case-insensitive quantization matching behavior.

An omitted `@revision` SHALL leave the installed artifact's recorded revision unconstrained. An explicit `@revision` SHALL be compared with persisted resolved provenance and SHALL match only artifacts carrying that revision; local resolution SHALL NOT discard or silently default an explicit revision. Resolution SHALL return the matched artifact's canonical `user/repo[@revision]:filename.gguf` reference and distinguish resolved-same, resolved-different, missing, and ambiguous outcomes when a caller supplies an expected artifact. Missing and ambiguous diagnostics SHALL be distinct, and ambiguity SHALL name every matching canonical artifact reference rather than choosing one.

#### Scenario: Resolve a logical selector to one installed artifact
- **WHEN** `user/repo:Q4_K_M` matches exactly one installed artifact
- **THEN** Coquena reports that artifact's canonical exact-filename reference and recorded revision
- **AND** it issues no network request

#### Scenario: Resolve an exact-filename selector
- **WHEN** `user/repo:model-Q4_K_M-imatrix.gguf` names an installed artifact
- **THEN** Coquena matches only that exact filename
- **AND** it does not apply quantization substring matching

#### Scenario: Report an ambiguous logical selector
- **WHEN** `user/repo:Q4_K_M` matches more than one installed artifact
- **THEN** Coquena returns an ambiguous outcome carrying each matching `user/repo[@revision]:filename.gguf` reference
- **AND** it does not select a calibration or production variant implicitly

#### Scenario: Report no installed match
- **WHEN** a reference matches no installed artifact
- **THEN** Coquena returns a missing outcome distinguishable from ambiguity
- **AND** it does not invent a canonical artifact reference

#### Scenario: Enforce an explicit revision locally
- **WHEN** an operator resolves `user/repo@abc123:model.gguf` against an installed artifact recorded at another revision
- **THEN** Coquena reports no match for the pinned reference
- **AND** it does not silently resolve the artifact from the other revision

#### Scenario: Pin a revision on a logical selector
- **WHEN** an operator resolves `user/repo@abc123:Q4_K_M`
- **THEN** only installed artifacts carrying persisted resolved revision `abc123` participate
- **AND** ambiguity is evaluated after applying the revision constraint

#### Scenario: Resolve without an explicit revision
- **WHEN** an operator resolves `user/repo:model.gguf` against exactly one installed artifact
- **THEN** Coquena reports the revision recorded in that artifact's sidecar
- **AND** omission of `@revision` does not imply that the artifact is recorded at `main`

#### Scenario: Distinguish a changed binding from new ambiguity
- **WHEN** a caller compares a previously resolved canonical artifact with the current result of its authored logical selector
- **THEN** Coquena distinguishes the same artifact, a different artifact, a missing selector, and an ambiguous selector
- **AND** it does not collapse all non-singleton outcomes into one boolean
