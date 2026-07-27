## MODIFIED Requirements

### Requirement: Persist portable per-artifact model sidecars
Coquena SHALL store each completed GGUF beneath the effective configured models directory with a separate adjacent JSON sidecar. The sidecar SHALL contain schema version, provider/repository/immutable revision/filename provenance appropriate to that provider, artifact size, a computed SHA-256 content digest, download timestamp, and a compact classification digest derived from the artifact's GGUF metadata. It SHALL NOT contain the raw GGUF metadata map, and it SHALL NOT use SQLite or a repository-wide metadata file. Raw GGUF metadata SHALL be read from the `.gguf` artifact on demand rather than cached in the sidecar.

#### Scenario: Preserve two variants from one repository
- **WHEN** two different GGUF variants from the same repository are downloaded
- **THEN** each GGUF has its own adjacent sidecar
- **AND** each sidecar retains the identity and classification digest of its own artifact

#### Scenario: Persist a content digest at download time
- **WHEN** Coquena completes a managed download
- **THEN** the sidecar's SHA-256 digest reflects the downloaded content
- **AND** computing it required no additional read pass over the completed file

#### Scenario: Exclude vocabulary arrays from the sidecar
- **WHEN** Coquena writes a sidecar for an artifact whose metadata contains vocabulary token, merge, or token-type arrays
- **THEN** the sidecar records the tokenizer's identity, vocabulary size, special token IDs, and a digest of the vocabulary
- **AND** it does not persist the array contents themselves

#### Scenario: Reject a sidecar from a superseded schema version
- **WHEN** Coquena reads a sidecar whose schema version predates the classification digest
- **THEN** it treats the sidecar as invalid rather than upgrading it in place
- **AND** the artifact is reported as unreconciled so reconciliation can restore it

### Requirement: Keep GGUF metadata separate from runtime settings
Coquena SHALL make GGUF metadata available for identification, classification, comparison, validation, diagnosis, and user reference only. It SHALL NOT parse runtime help, generate registered arguments, create presets, or assign metadata values to manual runtime or preset fields. Coquena MAY compute an advisory runtime capability constraint implied by a model's own metadata and present it to the operator; that constraint SHALL remain informational and SHALL NOT select, launch, or configure a runtime, and SHALL NOT set any preset value.

#### Scenario: View declared context metadata
- **WHEN** a GGUF exposes an architecture-specific context length
- **THEN** Coquena displays it as model metadata
- **AND** it does not set any runtime context-size argument or preset value

#### Scenario: Present publisher sampling recommendations as advisory
- **WHEN** a GGUF declares publisher sampling recommendations such as temperature or top-k
- **THEN** Coquena displays them as suggestions attributed to the publisher
- **AND** it does not apply them to any preset field

#### Scenario: Present a model-implied runtime constraint as advisory
- **WHEN** a model declares an exotic quantization file type, an architecture, or hybrid attention/SSM keys implying a minimum runtime capability
- **THEN** Coquena reports the implied constraint as informational alongside the model
- **AND** it does not select a runtime, launch one, or write the constraint into a preset field

## ADDED Requirements

### Requirement: Classify a GGUF by its declared type
Coquena SHALL classify every GGUF artifact by the `general.type` value declared in its header, distinguishing at least `model` and `mmproj`, and SHALL apply the same classification to a resolved remote candidate and to an installed local file through bounded random access. Classification SHALL NOT require downloading a complete artifact. An artifact whose declared type is absent or unrecognised SHALL be reported with an explicit unknown classification rather than an inferred one.

#### Scenario: Classify a remote candidate without downloading it
- **WHEN** Coquena classifies a resolved remote GGUF candidate
- **THEN** it reports the artifact's declared type using bounded range access
- **AND** it does not retrieve the artifact's tensor payload

#### Scenario: Classify an installed artifact from disk
- **WHEN** Coquena classifies an installed GGUF
- **THEN** it reports the same declared type it would report for the identical remote artifact

#### Scenario: Report an unrecognised type explicitly
- **WHEN** an artifact declares no type, or a type Coquena does not recognise
- **THEN** Coquena reports the classification as unknown
- **AND** it does not infer a type from the artifact's architecture or filename

### Requirement: Distinguish an artifact's type from its assigned role
Coquena SHALL treat an artifact's intrinsic type and the role it plays as independent. It SHALL derive the `projector` role from an `mmproj` type automatically, and SHALL require the `principal` and `draft` roles to be assigned by the operator, since a draft model is indistinguishable from a principal model by its metadata alone.

#### Scenario: Derive the projector role automatically
- **WHEN** an artifact's declared type is `mmproj`
- **THEN** Coquena assigns it the projector role without operator input

#### Scenario: Do not infer a draft role from metadata
- **WHEN** an artifact's declared type is `model`
- **THEN** Coquena does not classify it as a draft on its own
- **AND** the draft role becomes effective only once an operator assigns it

### Requirement: Compute companion compatibility as a criterion
Coquena SHALL express companion compatibility as a computed criterion evaluated against candidate artifacts rather than as a stored pin to a specific file, so that several installed artifacts may satisfy the same criterion simultaneously. A projector SHALL be admissible for a principal when the projector's projection dimension equals the principal's architecture-specific embedding length. A draft SHALL be admissible for a target when both declare the same tokenizer identity, vocabulary size, special token IDs, and vocabulary digest. Coquena SHALL accept the known variant spellings of projector metadata keys and SHALL report an unrecognised spelling as unknown rather than guessing.

#### Scenario: Admit a projector matching its principal
- **WHEN** a projector's projection dimension equals the principal model's embedding length
- **THEN** Coquena reports the projector as compatible with that principal

#### Scenario: Reject a projector whose dimensions disagree
- **WHEN** a projector's projection dimension differs from a principal model's embedding length
- **THEN** Coquena does not report them as compatible
- **AND** it does not fall back to matching them by filename or repository

#### Scenario: Admit several artifacts satisfying one criterion
- **WHEN** more than one installed artifact satisfies a companion criterion
- **THEN** Coquena reports each of them as an admissible option
- **AND** it does not silently select one

#### Scenario: Admit a draft sharing a tokenizer family
- **WHEN** two models declare the same tokenizer identity, vocabulary size, special token IDs, and vocabulary digest
- **THEN** Coquena reports the smaller one as an admissible draft for the other

#### Scenario: Refuse a draft from a different tokenizer family
- **WHEN** two models declare different tokenizer identities or vocabulary digests
- **THEN** Coquena does not report them as a valid draft/target pair
