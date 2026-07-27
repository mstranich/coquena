## Purpose

Define deterministic resolution and inspection of public Hugging Face GGUF artifacts.
## Requirements
### Requirement: Resolve public Hugging Face GGUF artifacts
Coquena SHALL resolve a public Hugging Face repository ID or canonical Hugging Face resolve-file URL without requiring an installed or registered inference runtime. Repository resolution SHALL list only `.gguf` artifacts and include repository, immutable resolved revision, filename, canonical URL, and available remote size/validators. A file or variant selector ending in `.gguf` (case-insensitive) SHALL resolve only when it identifies exactly one GGUF artifact by exact filename equality; any other selector SHALL continue to resolve only when it identifies exactly one artifact by substring match.

#### Scenario: List GGUF variants from a repository
- **WHEN** an operator resolves a public Hugging Face repository containing GGUF and non-GGUF files
- **THEN** Coquena returns only its GGUF candidates with their remote identities
- **AND** it does not depend on llama.cpp availability

#### Scenario: Reject ambiguous or non-GGUF selection
- **WHEN** a selector identifies zero or more than one GGUF artifact
- **THEN** Coquena returns a diagnostic without creating a download job or local model artifact

### Requirement: Inspect GGUF metadata by random access
Coquena SHALL inspect the GGUF header and metadata through bounded random access from either a local GGUF file or a resolved remote artifact. It SHALL preserve all decoded metadata under their original keys, including unknown keys, and SHALL NOT fetch tensor payloads or parse Hugging Face HTML.

#### Scenario: Inspect a remote GGUF before download
- **WHEN** an operator inspects a resolved remote GGUF
- **THEN** Coquena returns its raw GGUF metadata and selected identity using HTTP Range access
- **AND** it does not download the complete artifact

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
Coquena SHALL resolve a `user/repo[@revision]:QUANT` model reference against the installed model library without contacting Hugging Face or any other network service, applying the same reference grammar accepted for remote resolution. An omitted `@revision` SHALL default to `main`. A `QUANT` selector ending in `.gguf` (case-insensitive) SHALL match by exact filename equality; any other selector SHALL match by case-insensitive substring as before. Resolution SHALL report the matched artifact's exact filename and its recorded resolved revision. Resolution SHALL fail with a distinguishable diagnostic when no installed artifact matches and when more than one matches.

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
- **THEN** Coquena reports how many artifacts matched
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

