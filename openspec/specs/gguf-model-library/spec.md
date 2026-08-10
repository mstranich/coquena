## Purpose

Define portable on-disk GGUF model units, provenance sidecars, metadata, and publisher avatars.
## Requirements
### Requirement: Persist portable per-artifact model sidecars

Coquena SHALL store each completed GGUF beneath the effective configured models
directory with a separate adjacent JSON sidecar. The current sidecar SHALL
contain schema version, provider/repository/immutable revision/filename
provenance appropriate to that provider, artifact size, a computed SHA-256
content digest, download timestamp, and a compact derivation block computed from
the artifact's GGUF evidence. The derivation block SHALL contain an independent
algorithm version, computation time, classification, and parameter count. It
SHALL keep the declared model recipe distinct from physical tensor composition.
The sidecar SHALL NOT contain the raw GGUF metadata map and SHALL NOT use SQLite
or a repository-wide metadata file.

The reader SHALL accept the immediately preceding schema version when its
source and artifact evidence remain valid, mark its unversioned derived values
as stale, and perform no implicit rewrite. Older schemas whose document meaning
is incompatible SHALL remain invalid and use their established reconciliation
path.

#### Scenario: Publish current versioned derivation

- **WHEN** Coquena completes a managed GGUF download
- **THEN** its sidecar records the current document schema and derivation
  algorithm versions
- **AND** the derived classification and parameter count describe that artifact

#### Scenario: Read a legacy sidecar without discarding provenance

- **WHEN** Coquena reads an otherwise valid schema-version-2 sidecar lacking a
  derivation version
- **THEN** it preserves the source, artifact, and integrity baseline as readable
  evidence
- **AND** it reports the derived evidence as stale until explicitly repaired

#### Scenario: Avoid rewriting during an ordinary read

- **WHEN** `model list` or another read-only operation encounters a supported
  legacy sidecar
- **THEN** it does not rewrite or upgrade the sidecar
- **AND** it does not open or hash the GGUF merely to refresh derivation

#### Scenario: Reject an incompatible older schema

- **WHEN** Coquena reads a schema whose document meaning predates the compact
  classification digest and cannot be preserved safely
- **THEN** it treats the sidecar as invalid
- **AND** the artifact follows the established unreconciled recovery path

#### Scenario: Preserve two variants from one repository

- **WHEN** two different GGUF variants from the same repository are downloaded
- **THEN** each GGUF has its own adjacent sidecar
- **AND** each sidecar retains the identity and derived evidence of its own
  artifact

#### Scenario: Persist a content digest at download time

- **WHEN** Coquena completes a managed download
- **THEN** the sidecar's SHA-256 digest reflects the downloaded content
- **AND** computing it requires no additional read pass over the completed file

#### Scenario: Exclude vocabulary arrays from the sidecar

- **WHEN** Coquena writes a sidecar for an artifact whose metadata contains
  vocabulary token, merge, or token-type arrays
- **THEN** the sidecar records only the compact tokenizer and derivation facts
  required by its schema
- **AND** it does not persist the array contents themselves

### Requirement: Preserve model and sidecar as a logical unit
Coquena SHALL validate repository and filename components before deriving storage paths, prevent traversal outside the configured models directory, and refuse to overwrite an existing conflicting artifact. It SHALL consider an existing model idempotently present only when both its GGUF and valid matching sidecar exist. A sidecar declaring provider `local` SHALL require only repository and filename provenance; it SHALL NOT require a resolved revision or a remote URL.

#### Scenario: Reject unsafe source path components
- **WHEN** a resolved source identity would produce a traversal or invalid target path
- **THEN** Coquena rejects publication
- **AND** it does not write outside the configured models directory

#### Scenario: Sidecar publication fails
- **WHEN** publishing the sidecar fails after staging a completed model
- **THEN** Coquena removes the new staged or published GGUF for that operation
- **AND** it leaves no incomplete logical model unit

#### Scenario: Accept a local-provider sidecar without remote provenance
- **WHEN** a sidecar declares provider `local` with a repository and filename but no resolved revision or URL
- **THEN** Coquena considers the model unit valid and installed
- **AND** it does not require Hugging Face provenance fields for that provider

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

### Requirement: Publish a resolved-revision sidecar
Coquena SHALL publish an adjacent `<model-base>.revision` file for every completed GGUF. It SHALL contain only the resolved remote commit SHA followed by one newline. The JSON sidecar SHALL record `requestedRevision` and `resolvedRevision` from the same resolved source; `.revision` SHALL identify remote provenance and SHALL NOT be used as a local content checksum.

#### Scenario: Publish a revision-pinned model unit
- **WHEN** Coquena completes a resolved GGUF download
- **THEN** its GGUF, JSON sidecar, and `.revision` sidecar share one base name
- **AND** the `.revision` contents equal the JSON `source.resolvedRevision` followed by a newline

### Requirement: Detect inconsistent revision sidecars
Coquena SHALL validate the JSON and `.revision` values when it reads a completed model unit. If either is missing, malformed, or disagrees with the other, it SHALL mark the artifact inconsistent and SHALL NOT silently select one revision value.

#### Scenario: JSON and revision sidecars disagree
- **WHEN** an artifact's JSON resolved revision differs from its `.revision` contents
- **THEN** Coquena reports the artifact as inconsistent
- **AND** it does not use that artifact as a valid installed identity

### Requirement: Cache the publisher avatar per namespace
Coquena SHALL attempt to cache the Hugging Face publisher avatar once beneath `<models>/<publisher>/publisher-avatar.<format>` when a model download is created. It SHALL accept only bounded PNG, JPEG, or WebP content. Avatar absence or retrieval failure SHALL NOT block the model download, and incomplete or installed model views SHALL use the local cached avatar rather than requiring remote access.

#### Scenario: Share one avatar across publisher repositories
- **WHEN** multiple model repositories from the same publisher are downloaded
- **THEN** their library entries may use the same namespace-level cached avatar
- **AND** no avatar copy is required inside each repository directory

#### Scenario: Publisher avatar is unavailable
- **WHEN** Hugging Face does not provide an accepted publisher avatar
- **THEN** Coquena continues creating or resuming the model download
- **AND** it leaves the optional avatar absent

### Requirement: List a GGUF without a valid sidecar as unreconciled
Coquena SHALL include every `.gguf` beneath the configured models directory when enumerating the library, regardless of whether it has a valid Coquena sidecar. A `.gguf` lacking a valid matching sidecar SHALL be reported with state `unreconciled` and a best-effort repository and filename derived from its path, rather than being silently omitted.

#### Scenario: Discover a foreign or sidecar-less model
- **WHEN** the configured models directory contains a `.gguf` with no adjacent Coquena sidecar, placed beneath a publisher/repository path shape
- **THEN** the library enumeration reports it with state `unreconciled` and its path-derived repository and filename
- **AND** it does not fail or skip the rest of the enumeration

#### Scenario: Unreconciled entries are not resolvable for binding
- **WHEN** a reference is resolved against the installed library
- **THEN** an entry in a state other than `installed` is never selected as a match
- **AND** resolving that reference without `--allow-missing` reports it as not installed

### Requirement: Classify a GGUF by domain-correct declared and physical evidence

Coquena SHALL classify every GGUF artifact by the `general.type` value declared
in its header, distinguishing at least `model` and `mmproj`. It SHALL interpret
`general.file_type` in the model-recipe domain and SHALL treat tensor descriptor
types as separate physical evidence. A missing or unrecognized value SHALL be
reported explicitly rather than inferred from architecture or filename.

For physical dominance, Coquena SHALL weight supported structural tensors by
their scalar parameter counts over canonical `blk.`, `enc.blk.`, `dec.blk.`,
`v.blk.`, and `a.blk.` scopes. If no supported structural tensor is present, the
physical result SHALL be unavailable and SHALL NOT fall back to unrestricted
tensors. Classification SHALL avoid decoding array payloads and SHALL NOT grant
an artifact any standing it did not already have.

#### Scenario: Keep declared recipe distinct from tensor composition

- **WHEN** a model declares recipe `Q4_K_M` and its structural tensors are
  physically dominated by `Q4_K`
- **THEN** Coquena records both facts in their respective domains
- **AND** it does not replace the declared recipe with the tensor type

#### Scenario: Weight physical composition by parameters

- **WHEN** eligible structural tensors use several physical encodings
- **THEN** Coquena determines dominance from the product of each tensor's
  dimensions grouped by tensor encoding
- **AND** it does not weight each tensor equally or weight by encoded bytes

#### Scenario: Exclude vocabulary-heavy exception tensors

- **WHEN** an embedding or output tensor contains more parameters than the
  canonical structural blocks
- **THEN** that exception tensor does not determine structural physical
  dominance
- **AND** the result remains based on the supported block scopes

#### Scenario: Refuse an unsupported physical scope

- **WHEN** an artifact contains no tensor in a supported canonical block scope
- **THEN** Coquena reports physical quantization evidence as unavailable
- **AND** it does not widen the calculation to every tensor

#### Scenario: Classify a remote candidate without downloading it

- **WHEN** Coquena classifies a resolved remote GGUF candidate
- **THEN** it obtains the required declared evidence through bounded random
  access
- **AND** it does not retrieve the artifact's tensor payload

#### Scenario: Report an unrecognized artifact type explicitly

- **WHEN** an artifact declares no `general.type`, or a value Coquena does not
  recognize
- **THEN** Coquena reports the classification as unknown
- **AND** it does not infer the type from architecture or filename

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

### Requirement: Report whether an artifact is usable
Coquena SHALL report, for every artifact in the library, whether it can take part in a runtime invocation. An artifact SHALL be usable when it is reconciled and its declared type is known. An artifact classified as a projector SHALL be usable only as a companion of a compatible installed principal, and SHALL be reported unusable with that reason when no such principal is installed. A principal SHALL remain usable when no compatible projector is installed, since a projector adds a capability rather than enabling inference. An artifact whose declared type is unknown SHALL be reported unusable. Usability SHALL be derived when the library is read rather than persisted, since it depends on which other artifacts are present.

#### Scenario: Report a reconciled principal as usable
- **WHEN** a reconciled artifact is classified as a principal model
- **THEN** Coquena reports it as usable

#### Scenario: Report a principal without its projector as usable
- **WHEN** a reconciled principal has no compatible projector installed
- **THEN** Coquena still reports the principal as usable

#### Scenario: Report an orphaned projector as unusable
- **WHEN** a reconciled projector is compatible with no installed principal
- **THEN** Coquena reports it as unusable
- **AND** the reason names the absent compatible principal

#### Scenario: Report an unreconciled artifact as unusable
- **WHEN** an artifact has no valid sidecar
- **THEN** Coquena reports it as unusable
- **AND** the reason names reconciliation as what it requires

#### Scenario: Report an unclassifiable artifact as unusable
- **WHEN** an artifact's declared type cannot be determined
- **THEN** Coquena reports it as unusable rather than listing it without a type

#### Scenario: Reflect a change in the library without rewriting sidecars
- **WHEN** a compatible principal is installed after an orphaned projector was reported unusable
- **THEN** the projector is reported usable on the next read
- **AND** no sidecar was rewritten to record the change

### Requirement: Traverse GGUF evidence through one validated scanner

Coquena SHALL use one shared GGUF traversal for metadata and tensor descriptors.
The traversal SHALL validate the supported header, consume every metadata entry
before reading tensor descriptors, decode count and dimension fields using the
specified widths and order, and skip unneeded array payloads safely. Parameter
count, declared recipe extraction, physical composition, local inspection, and
download-sidecar construction SHALL consume projections of this traversal
rather than implement independent binary offsets.

#### Scenario: Reach tensor descriptors after metadata

- **WHEN** a GGUF contains scalar and array metadata before its tensor table
- **THEN** the scanner consumes or safely skips each metadata value before
  yielding the first tensor descriptor
- **AND** descriptor decoding does not begin at a fixed header offset

#### Scenario: Count a multidimensional tensor

- **WHEN** a tensor descriptor contains dimensions `2`, `3`, and `4`
- **THEN** the parameter counter contributes `24` scalar parameters
- **AND** it does not confuse the tensor type or offset with a dimension

#### Scenario: Share traversal across consumers

- **WHEN** classification and parameter counting inspect the same GGUF
- **THEN** both use the shared descriptor semantics
- **AND** neither contains an independent manual offset loop

### Requirement: Identify stale derivation without artifact I/O

Coquena SHALL compare the sidecar's derivation algorithm version with the
current version when listing the local catalog. This comparison SHALL require
only sidecar data and SHALL NOT hash, scan, open the GGUF, query a remote
service, or mutate the sidecar.

#### Scenario: List a stale derivation cheaply

- **WHEN** an installed artifact's sidecar has no derivation version or an older
  derivation version
- **THEN** `coquena model list` reports that its derived evidence is stale
- **AND** it performs no model scan or network request

#### Scenario: List a current derivation cheaply

- **WHEN** an installed artifact's derivation version equals the current
  algorithm version
- **THEN** `coquena model list` does not report it as derivation-stale
- **AND** it still performs no model scan or network request
