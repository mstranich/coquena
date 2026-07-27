## MODIFIED Requirements

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

### Requirement: Resolve public Hugging Face GGUF artifacts
Coquena SHALL resolve a public Hugging Face repository ID or canonical Hugging Face resolve-file URL without requiring an installed or registered inference runtime. Repository resolution SHALL list only `.gguf` artifacts and include repository, immutable resolved revision, filename, canonical URL, and available remote size/validators. A file or variant selector ending in `.gguf` (case-insensitive) SHALL resolve only when it identifies exactly one GGUF artifact by exact filename equality; any other selector SHALL continue to resolve only when it identifies exactly one artifact by substring match.

#### Scenario: List GGUF variants from a repository
- **WHEN** an operator resolves a public Hugging Face repository containing GGUF and non-GGUF files
- **THEN** Coquena returns only its GGUF candidates with their remote identities
- **AND** it does not depend on llama.cpp availability

#### Scenario: Reject ambiguous or non-GGUF selection
- **WHEN** a selector identifies zero or more than one GGUF artifact
- **THEN** Coquena returns a diagnostic without creating a download job or local model artifact
