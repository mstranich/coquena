## MODIFIED Requirements

### Requirement: Inspect GGUF metadata by random access
Coquena SHALL inspect the GGUF header and metadata through bounded random access from either a local GGUF file or a resolved remote artifact, decoding all metadata under their original keys including unknown keys, and SHALL NOT fetch tensor payloads or parse Hugging Face HTML. Inspection SHALL return a derived projection of that metadata — the artifact's identity, classification, and scalar metadata keys — and SHALL NOT return the raw metadata map or the contents of vocabulary arrays as a payload. Values not present in the derived projection SHALL remain available by reading the `.gguf` artifact on demand.

#### Scenario: Inspect a local GGUF artifact
- **WHEN** an operator inspects an installed GGUF by path
- **THEN** Coquena reads its header from disk and returns its identity, classification, and scalar metadata
- **AND** it issues no network request

#### Scenario: Omit raw metadata from an inspection payload
- **WHEN** an inspected artifact declares vocabulary token, merge, or token-type arrays
- **THEN** the inspection result reports the tokenizer's identity, size, special token IDs, and vocabulary digest
- **AND** it does not include the array contents

#### Scenario: Reject malformed GGUF
- **WHEN** the remote or local bytes do not contain a supported valid GGUF header/metadata section
- **THEN** Coquena reports inspection failure
- **AND** it does not represent invented metadata as valid

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

### Requirement: Resolve a model reference against the installed library
Coquena SHALL resolve a `user/repo[@revision]:QUANT` model reference against the installed model library without contacting Hugging Face or any other network service, applying the same reference grammar accepted for remote resolution. An omitted `@revision` SHALL default to `main`. A `QUANT` selector ending in `.gguf` (case-insensitive) SHALL match by exact filename equality; any other selector SHALL match by case-insensitive substring as before. Resolution SHALL report the matched artifact's exact filename and its recorded resolved revision. Resolution SHALL fail with a distinguishable diagnostic when no installed artifact matches and when more than one matches, and an ambiguous-match diagnostic SHALL carry the matching filenames rather than only their count.

#### Scenario: Resolve an installed reference offline
- **WHEN** a reference is resolved against a library holding exactly one matching artifact
- **THEN** Coquena reports that artifact's exact filename and recorded revision
- **AND** it issues no network request

#### Scenario: Report no installed match
- **WHEN** a reference matches no installed artifact
- **THEN** Coquena reports that the reference is not installed
- **AND** the diagnostic is distinguishable from an ambiguous match

#### Scenario: Report an ambiguous installed match
- **WHEN** a reference matches more than one installed artifact
- **THEN** Coquena reports how many artifacts matched and names each matching filename
- **AND** it does not select one of them

#### Scenario: Resolve without a configured revision axis
- **WHEN** a reference is resolved offline without an `@revision` segment
- **THEN** Coquena reports the revision already recorded in the artifact's sidecar
- **AND** it does not require the reference to carry a revision

#### Scenario: Pin a revision in the short reference form
- **WHEN** an operator supplies `user/repo@revision:QUANT`
- **THEN** Coquena resolves using the supplied revision instead of defaulting to `main`

#### Scenario: Resolve an exact-filename selector
- **WHEN** a selector names a complete `.gguf` filename
- **THEN** Coquena matches only the artifact whose filename equals the selector exactly
- **AND** it does not fall back to substring matching for that selector

## ADDED Requirements

### Requirement: Discover a remote repository's classified candidates
Coquena SHALL provide remote discovery of a Hugging Face repository that reports every GGUF candidate it contains together with each candidate's classification, read through bounded range access without downloading any complete artifact. When the reference carries a selector, discovery SHALL still report every candidate and SHALL mark which one the selector would resolve to, rather than reducing the result to that candidate. Discovery SHALL also report the repository's model card as plain text.

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
- **THEN** Coquena reports the model card as plain text alongside the candidates

#### Scenario: Discover a repository without a model card
- **WHEN** the repository publishes no model card
- **THEN** Coquena reports the candidates and omits the model card
- **AND** it does not fail discovery
