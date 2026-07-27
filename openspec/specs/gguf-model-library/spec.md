## Purpose

Define portable on-disk GGUF model units, provenance sidecars, metadata, and publisher avatars.
## Requirements
### Requirement: Persist portable per-artifact model sidecars
Coquena SHALL store each completed GGUF beneath the effective configured models directory with a separate adjacent JSON sidecar. The sidecar SHALL contain schema version, provider/repository/immutable revision/filename provenance appropriate to that provider, artifact size, a computed SHA-256 content digest, download timestamp, and complete raw GGUF metadata when available. It SHALL NOT use SQLite or a repository-wide metadata file.

#### Scenario: Preserve two variants from one repository
- **WHEN** two different GGUF variants from the same repository are downloaded
- **THEN** each GGUF has its own adjacent sidecar
- **AND** each sidecar retains the identity and raw metadata of its own artifact

#### Scenario: Persist a content digest at download time
- **WHEN** Coquena completes a managed download
- **THEN** the sidecar's SHA-256 digest reflects the downloaded content
- **AND** computing it required no additional read pass over the completed file

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
Coquena SHALL make GGUF metadata available for identification, comparison, validation, diagnosis, and user reference only. It SHALL NOT infer llama.cpp compatibility, parse runtime help, generate registered arguments, create presets, or assign metadata values to manual runtime fields.

#### Scenario: View declared context metadata
- **WHEN** a GGUF exposes an architecture-specific context length
- **THEN** Coquena displays it as raw model metadata
- **AND** it does not set any runtime context-size argument or preset value

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

