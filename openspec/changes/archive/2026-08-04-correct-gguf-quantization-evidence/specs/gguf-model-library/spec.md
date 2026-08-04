## MODIFIED Requirements

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

## ADDED Requirements

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
