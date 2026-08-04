## Purpose

Define deterministic resolution and inspection of public Hugging Face GGUF artifacts.
## Requirements
### Requirement: Resolve public Hugging Face GGUF artifacts
Coquena SHALL resolve a public Hugging Face repository ID or canonical Hugging Face resolve-file URL without requiring an installed or registered inference runtime. Repository resolution SHALL list only `.gguf` artifacts and include repository, immutable resolved revision, filename, canonical URL, and available remote size/validators. A file or variant selector ending in `.gguf` (case-insensitive) SHALL resolve only when it identifies exactly one GGUF artifact by exact filename equality; any other selector SHALL continue to resolve only when it identifies exactly one artifact by substring match. When a selector identifies zero or more than one artifact, the resulting diagnostic SHALL carry the filenames of the artifacts available for selection, not only their count.

#### Scenario: List GGUF variants from a repository
- **WHEN** an operator resolves a public Hugging Face repository containing GGUF and non-GGUF files
- **THEN** Coquena returns only its GGUF candidates with their remote identities
- **AND** it does not depend on llama.cpp availability

#### Scenario: Reject ambiguous or non-GGUF selection
- **WHEN** a selector identifies zero or more than one GGUF artifact
- **THEN** Coquena returns a diagnostic without creating a download job or local model artifact
- **AND** the diagnostic names the candidate filenames the operator can choose from

### Requirement: Inspect GGUF metadata by random access
Coquena SHALL inspect the GGUF header and metadata through bounded random access from either a local GGUF file or a resolved remote artifact, decoding all metadata under their original keys including unknown keys, and SHALL NOT fetch tensor payloads or parse Hugging Face HTML. Inspection SHALL return a derived projection of that metadata — the artifact's identity, classification, and scalar metadata keys — and SHALL NOT return the raw metadata map or the contents of vocabulary arrays as a payload. Values not present in the derived projection SHALL remain available by reading the `.gguf` artifact on demand.

#### Scenario: Inspect a local GGUF artifact
- **WHEN** an operator inspects an installed GGUF by path
- **THEN** Coquena reads its header from disk and returns its identity, classification, and scalar metadata
- **AND** it issues no network request

#### Scenario: Inspect a remote GGUF before download
- **WHEN** Coquena classifies a resolved remote candidate as part of discovery
- **THEN** it reads the artifact's identity and classification using HTTP Range access
- **AND** it does not download the complete artifact

#### Scenario: Omit raw metadata from an inspection payload
- **WHEN** an inspected artifact declares vocabulary token, merge, or token-type arrays
- **THEN** the inspection result reports the tokenizer's identity, size, special token IDs, and vocabulary digest
- **AND** it does not include the array contents

#### Scenario: Reject malformed GGUF
- **WHEN** the remote or local bytes do not contain a supported valid GGUF header/metadata section
- **THEN** Coquena reports inspection failure
- **AND** it does not represent invented metadata as valid

### Requirement: Pin mutable references to an immutable revision
Coquena SHALL resolve a mutable requested revision, normally `main`, to an exact commit SHA before remote inspection or download. It SHALL retain both requested and resolved revisions and construct the operational artifact URL using the resolved SHA. It SHALL NOT inspect or download through a mutable branch URL after resolution.

#### Scenario: Resolve a repository main branch
- **WHEN** an operator supplies a repository or selector without an explicit revision
- **THEN** Coquena records `main` as the requested revision and resolves it to a commit SHA
- **AND** it uses the SHA-pinned URL for the selected artifact

#### Scenario: Mutable branch changes after resolution
- **WHEN** the requested branch advances after Coquena resolved an artifact
- **THEN** the existing inspection or download operation continues to use its original resolved commit SHA
- **AND** it does not silently inspect a newer artifact

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
### Requirement: Discover a remote repository's classified candidates
Coquena SHALL provide remote discovery of a Hugging Face repository that reports every GGUF candidate it contains together with each candidate's classification, read through bounded range access without downloading any complete artifact. When the reference carries a selector, discovery SHALL still report every candidate and SHALL mark which one the selector would resolve to, rather than reducing the result to that candidate. Discovery SHALL NOT print the repository's model card, whose length buries the candidate list discovery exists to present; reporting it SHALL be reintroduced behind an explicit option in a later change.

#### Scenario: Discover every candidate in a repository
- **WHEN** an operator discovers a repository containing a principal model and a projector
- **THEN** Coquena reports both candidates with their declared types
- **AND** it downloads neither artifact in full

#### Scenario: Mark the candidate a selector would choose
- **WHEN** an operator discovers a repository using a reference carrying a selector that matches one candidate
- **THEN** Coquena still reports every candidate in the repository
- **AND** it marks the one the selector resolves to

#### Scenario: Report the repository model card
- **WHEN** an operator discovers a repository that publishes a model card
- **THEN** Coquena does not print the model card
- **AND** the reporting this scenario previously required is withdrawn, because the card's length buries the candidate list; a later change reintroduces it behind an explicit option

#### Scenario: Present the candidates without the model card
- **WHEN** an operator discovers a repository that publishes a long model card
- **THEN** the output reports the candidates and their classification only
- **AND** the model card does not appear

#### Scenario: Discover a repository without a model card
- **WHEN** the repository publishes no model card
- **THEN** Coquena reports the candidates
- **AND** it does not fail discovery
