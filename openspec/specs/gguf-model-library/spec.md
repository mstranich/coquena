## Purpose

Define portable on-disk GGUF model units, provenance sidecars, metadata, and publisher avatars.
## Requirements

### Requirement: Persist a portable per-artifact sidecar of self-describing evidence

Coquena SHALL store each completed GGUF beneath the effective configured models
directory with a separate adjacent JSON sidecar. The sidecar SHALL contain
schema version, provider/repository/immutable revision/filename provenance
appropriate to that provider, artifact size, download timestamp, one or more
content digests, and a compact derivation block computed from the artifact's
GGUF evidence.

Every persisted digest SHALL be self-describing, declaring the **role** it fills,
the **algorithm** that produced it, and the **domain** the algorithm was applied
to. Two roles are defined: `integrity`, a digest Coquena computed from the local
file, and `repository-match`, a digest the repository published for the artifact
it claims these bytes are. No derivation SHALL infer a role, an algorithm or a
domain from a digest's value.

A valid sidecar SHALL carry an `integrity` digest. Writing a sidecar SHALL
require computing one, so an artifact under Coquena's management is never
recorded without the means to detect a later change to its bytes. A sidecar
carrying no `integrity` digest SHALL be treated as invalid rather than as an
artifact whose integrity is merely unknown.

Coquena SHALL accept a published identifier as `repository-match` evidence when
it can reproduce that identifier from the complete local bytes. SHA-256 over the
`raw-file` domain and SHA-1 over the `git-blob` domain SHALL both be accepted on
that basis. A digest in a domain Coquena cannot currently reproduce SHALL be
recorded, stating its domain, and SHALL NOT be treated as evidence — and SHALL
NOT invalidate the sidecar. Acceptance SHALL rest on reproducibility rather than
on the algorithm's name, so declining a domain is a statement about what Coquena
implements and not about whether the identifier describes the bytes.

The sidecar SHALL NOT persist an HTTP entity tag. A value read from a transport
header whose meaning depends on which hop answered SHALL NOT be recorded as a
digest of any role. This SHALL NOT be read as forbidding a published identifier
that a response carries in a header whose domain is unambiguous; what is
forbidden is persisting an opaque tag as though its meaning were known.

The derivation block SHALL contain an independent algorithm version, computation
time, classification, and parameter count. It SHALL keep the declared model
recipe distinct from physical tensor composition. The sidecar SHALL NOT contain
the raw GGUF metadata map and SHALL NOT use SQLite or a repository-wide metadata
file.

**Only the current schema version SHALL be read.** A sidecar at any other
version SHALL be treated as invalid, and its artifact SHALL follow the
established recovery path for an artifact without a valid sidecar. Coquena SHALL
NOT translate, migrate or partially admit an earlier document shape, and SHALL
NOT rewrite a sidecar during a read-only operation. Recovery SHALL be
non-destructive: the artifact's bytes are never touched, and reconciling or
importing it restores a current sidecar.

Unknown keys a reader does not recognise SHALL be preserved when the sidecar is
rewritten, so a hand-written annotation survives an operation that had no reason
to discard it. This SHALL NOT be read as a compatibility mechanism between
schema versions.

#### Scenario: Publish current versioned derivation

- **WHEN** Coquena completes a managed GGUF download
- **THEN** its sidecar records the current document schema and derivation
  algorithm versions
- **AND** the derived classification and parameter count describe that artifact

#### Scenario: Record a digest with its role, algorithm and domain

- **WHEN** Coquena records any content digest for an artifact
- **THEN** the persisted entry states the role it fills, the algorithm that
  produced it and the domain it was applied to
- **AND** a reader identifies its meaning without consulting the field it was
  stored under

#### Scenario: Accept a Git blob identifier as repository-match evidence

- **WHEN** the identifier a repository publishes for an artifact is a Git blob
  object ID and Coquena reproduces it from the complete local bytes
- **THEN** it is recorded as a `repository-match` digest over the `git-blob`
  domain
- **AND** it is accepted as evidence that the bytes match the repository

#### Scenario: Record an unreproducible domain without invalidating the sidecar

- **WHEN** the only identifier a repository publishes for an artifact is in a
  domain Coquena cannot reproduce from the local bytes
- **THEN** the sidecar may record it, stating its domain
- **AND** it is not treated as evidence that the bytes match the repository
- **AND** the artifact is reported as having evidence Coquena does not support,
  rather than as having none

#### Scenario: Refuse to carry a transport entity tag into the record

- **WHEN** a provider response carries an entity tag while an artifact is being
  recorded
- **THEN** the sidecar does not persist it
- **AND** no digest role is populated from it

#### Scenario: Reject a sidecar written at any other schema version

- **WHEN** Coquena reads a sidecar whose schema version is not the current one
- **THEN** it treats the sidecar as invalid regardless of how close the version is
- **AND** the artifact follows the recovery path for an artifact without a valid
  sidecar
- **AND** no field of the earlier document is admitted, translated or migrated

#### Scenario: Recover a rejected artifact without touching its bytes

- **WHEN** an artifact whose sidecar was rejected is reconciled or imported
- **THEN** a sidecar at the current schema is written for it
- **AND** the artifact's file was never modified, moved or re-downloaded

#### Scenario: Reject a sidecar carrying no integrity digest

- **WHEN** Coquena reads a sidecar at the current schema that records no
  `integrity` digest
- **THEN** it treats the sidecar as invalid
- **AND** it does not report the artifact as one whose integrity is unknown

#### Scenario: Avoid rewriting during an ordinary read

- **WHEN** `model list` or another read-only operation encounters any sidecar,
  valid or invalid
- **THEN** it does not rewrite or upgrade the sidecar
- **AND** it does not open or hash the GGUF merely to refresh derivation

#### Scenario: Preserve a hand-written annotation across a rewrite

- **WHEN** an operation rewrites a sidecar that carries a key Coquena does not
  recognise
- **THEN** the unrecognised key and its value survive the rewrite

#### Scenario: Preserve two variants from one repository

- **WHEN** two different GGUF variants from the same repository are downloaded
- **THEN** each GGUF has its own adjacent sidecar
- **AND** each sidecar retains the identity and derived evidence of its own
  artifact

#### Scenario: Persist a content digest at download time

- **WHEN** Coquena completes a managed download
- **THEN** the sidecar's `integrity` digest reflects the downloaded content
- **AND** computing it requires no additional read pass over the completed file

#### Scenario: Exclude vocabulary arrays from the sidecar

- **WHEN** Coquena writes a sidecar for an artifact whose metadata contains
  vocabulary token, merge, or token-type arrays
- **THEN** the sidecar records only the compact tokenizer and derivation facts
  required by its schema
- **AND** it does not persist the array contents themselves

### Requirement: Keep artifact evidence on independent axes

Coquena SHALL persist what it knows about an artifact on five axes, each written
by the operation that owns it and none derived from another:

| Axis | Records | Written by |
|---|---|---|
| local | the `integrity` digest and when it was computed | any operation that hashes the file |
| verification | what the last verification found, and when | the verification command only |
| observation | the latest remote repository, requested revision, resolved commit, artifact path, published identifier when supplied, and when it was seen | any operation that consults the repository |
| attestation | the established binding of bytes to a repository revision | an operation that establishes it |
| ownership | the operator's acceptance of the artifact, and when | an operation acting on operator instruction |

Recording a new observation SHALL NOT erase, weaken or strengthen an existing
attestation, and SHALL NOT erase an ownership assertion. Only the latest
observation SHALL be retained; the sidecar SHALL NOT be an event log.

#### Scenario: Preserve an attestation across a later observation

- **WHEN** an artifact carrying an attestation is observed against a repository
  whose current revision has advanced
- **THEN** the recorded attestation is unchanged, including its bound revision
- **AND** the observation records the newly seen revision and when it was seen

#### Scenario: Retain only the latest observation

- **WHEN** an artifact already carrying an observation is observed again
- **THEN** the sidecar records the newer observation in place of the older one
- **AND** it does not accumulate a history of observations

#### Scenario: Preserve ownership across online reconciliation

- **WHEN** an artifact the operator accepted is later reconciled against its
  repository
- **THEN** the ownership assertion and its timestamp are unchanged
- **AND** the artifact is not reported as though it had never been accepted

#### Scenario: Record an observation without an attestation

- **WHEN** a repository is consulted and no comparable published evidence is
  available for the artifact
- **THEN** the sidecar records the observation, including the resolved revision
- **AND** attestation remains unset

### Requirement: Record the outcome of the last integrity verification

Coquena SHALL persist, on its own axis of the sidecar, the outcome of the most
recent integrity verification of an artifact and the time it was performed. The
verification command SHALL be the only writer of that axis: no other operation
SHALL set, clear or overwrite it, including one that hashes the file for its own
purposes. An artifact that has never been verified SHALL be reported as
unverified rather than as clean, and SHALL NOT be reported as altered on that
basis alone.

A reader SHALL derive whether an artifact's bytes were found altered from this
record alone, without opening or hashing the artifact. When the artifact's file
has been modified on disk more recently than the recorded verification, the
record SHALL be reported as stale so an operator can distinguish a clean result
from an old one.

The record SHALL identify the `integrity` digest it was checked against. A record
that names a digest the sidecar no longer carries SHALL NOT be applied: the
artifact SHALL be reported unverified rather than carrying an outcome about bytes
that are no longer the recorded ones. No operation SHALL need to clear the axis
to replace an artifact's recorded evidence.

#### Scenario: Persist a mismatch so later reads need no hash

- **WHEN** verification finds an artifact's bytes disagreeing with its
  `integrity` digest
- **THEN** the sidecar records that outcome and the time it was observed
- **AND** a later listing reports the artifact as altered without hashing it

#### Scenario: Persist a match

- **WHEN** verification finds an artifact's bytes agreeing with its `integrity`
  digest
- **THEN** the sidecar records that outcome and the time it was observed
- **AND** a later listing reports the artifact as usable without hashing it

#### Scenario: Report a never-verified artifact as unverified

- **WHEN** an artifact has a valid sidecar carrying no verification record
- **THEN** Coquena reports its verification state as unverified
- **AND** it does not report the artifact as altered

#### Scenario: Leave the verification axis untouched during reconciliation

- **WHEN** reconciliation hashes an artifact and records the same `integrity`
  digest the sidecar already carried
- **THEN** the sidecar's verification record is unchanged, including its
  timestamp
- **AND** an artifact previously found altered is still reported as altered

#### Scenario: Retire a verification record when the recorded digest is replaced

- **WHEN** an operation records a different `integrity` digest for an artifact
  than the one an existing verification record names
- **THEN** that verification record is not applied
- **AND** the artifact is reported as unverified rather than as matched or
  altered

#### Scenario: Report a verification record older than the file

- **WHEN** an artifact's file modification time is later than its recorded
  verification time
- **THEN** Coquena reports the verification record as stale
- **AND** the reported outcome is still the one that was recorded

#### Scenario: Never reach verification without an integrity digest

- **WHEN** verification runs against any artifact Coquena manages
- **THEN** an `integrity` digest is always present to compare against, because a
  sidecar lacking one is invalid and its artifact is not managed
- **AND** no "nothing to compare against" outcome is defined or recorded

### Requirement: Record an attestation with the method that established it

An attestation SHALL bind an artifact's bytes to a repository, a resolved
revision and an artifact path, together with the digest that binds them, the
time it was established, and the **method** by which it was established.

Two methods are defined. `download` applies to bytes Coquena transferred from a
revision-pinned URL, whose provenance follows from where they came from rather
than from any comparison. `remote-comparison` applies to bytes already on disk
whose locally computed SHA-256 was found equal to the identifier the repository
publishes for that artifact at that revision.

Resolving a repository's current revision SHALL NOT by itself establish an
attestation. Coquena SHALL NOT record an attestation for a comparison it did not
perform, and SHALL NOT promote size, filename, modification time, an entity tag
or GGUF metadata agreement into one.

An attestation SHALL identify the digest it bound, and a recorded attestation
whose bound digest is not among the artifact's current digests SHALL NOT be
applied. The artifact SHALL be reported as carrying no attestation rather than
carrying provenance about bytes that are no longer the recorded ones. This SHALL
hold however the attestation came to be preserved, including when an operation
that does not own the attestation axis carried it forward unchanged.

A comparison that **disagrees** SHALL be recorded rather than refused. Coquena
SHALL record the observation, including the published identifier that disagreed,
and SHALL leave attestation unset; it SHALL NOT alter an existing attestation, an
existing ownership assertion, or the artifact's `integrity` digest on the
strength of a disagreement. The diagnostic SHALL allow that the bytes may belong
to an earlier revision, or to one the publisher has since destroyed, rather than
asserting corruption — those causes are not distinguishable from the artifact,
which is why a disagreement and an unavailable identifier reach the same
standing.

#### Scenario: Attest a managed download by construction

- **WHEN** Coquena completes a download from a revision-pinned URL
- **THEN** the sidecar records an attestation bound to that repository, resolved
  revision and artifact path, with method `download`
- **AND** it records the time the attestation was established

#### Scenario: Attest an adopted artifact by comparison

- **WHEN** an artifact already on disk is compared against the SHA-256 the
  repository publishes for it and the two agree
- **THEN** the sidecar records an attestation with method `remote-comparison`
- **AND** it records both the `integrity` digest and the `repository-match`
  digest, even though their values are equal

#### Scenario: Leave attestation unset when nothing was compared

- **WHEN** an artifact already on disk is reconciled against a repository that
  publishes no identifier in an accepted domain
- **THEN** the sidecar records the observation and leaves attestation unset
- **AND** the artifact is not reported as attested

#### Scenario: Refuse to attest on corroborating metadata

- **WHEN** an artifact's size, filename and GGUF metadata all agree with the
  repository's while no accepted digest is available
- **THEN** no attestation is recorded
- **AND** the agreement is not reported as evidence of byte identity

#### Scenario: Record a disagreement instead of discarding what was learned

- **WHEN** an artifact's bytes are compared against a published content hash and
  they disagree
- **THEN** the sidecar records the observation, including the identifier that
  disagreed, and leaves attestation unset
- **AND** the diagnostic allows that the artifact may belong to an earlier or
  destroyed revision rather than asserting corruption
- **AND** the operator keeps a usable artifact rather than an error and no
  sidecar

#### Scenario: Leave earlier evidence intact after a disagreement

- **WHEN** an artifact already carrying an attestation or an ownership assertion
  is compared against a published content hash and they disagree
- **THEN** the existing attestation and ownership assertion are unchanged
- **AND** the artifact's `integrity` digest is not replaced on the strength of
  the disagreement

#### Scenario: Retire an attestation when the integrity digest is replaced

- **WHEN** an operation records a different `integrity` digest for an artifact
  than the one an existing attestation bound
- **THEN** that attestation is not applied
- **AND** the artifact is reported as carrying no attestation rather than as
  attested to the earlier revision

#### Scenario: Preserve an axis without preserving a claim about other bytes

- **WHEN** an operation that does not own the attestation axis carries an
  existing attestation forward, and the artifact's `integrity` digest changed in
  the same write
- **THEN** the carried attestation does not apply
- **AND** no diagnostic reports the artifact as attested

### Requirement: Distinguish verified, partial and unattested artifacts

Coquena SHALL report an artifact's attestation standing using three
distinguishable values wherever an operator inspects an individual artifact, and
SHALL carry the same distinction in structured output.

`verified` SHALL mean an attestation is recorded. `partial` SHALL mean a
repository was consulted and no attestation could be established. `unattested`
SHALL mean no repository has been consulted for the artifact. None of the three
SHALL be presented as a failure, and an artifact SHALL NOT be reported as
deficient for being `partial` or `unattested`.

All three SHALL be derived from the evidence axes when read and SHALL NOT be
persisted. `partial` in particular SHALL NOT become a stored attestation kind:
durable attestation is either established or absent, and `partial` describes the
outcome of an operation. These three values SHALL describe an artifact's
attestation standing only; they are not the artifact's state, which is a
separate vocabulary that reads these among other axes.

#### Scenario: Report a verified artifact

- **WHEN** an artifact carries an attestation
- **THEN** Coquena reports it as verified
- **AND** it reports the method by which the attestation was established

#### Scenario: Report a partial artifact

- **WHEN** an artifact carries an observation and no attestation
- **THEN** Coquena reports it as partial
- **AND** it does not report the artifact as failing

#### Scenario: Report an unattested artifact

- **WHEN** an artifact carries neither an observation nor an attestation
- **THEN** Coquena reports it as unattested
- **AND** a privately obtained model is not reported as deficient for being so

#### Scenario: Carry the distinction in structured output

- **WHEN** an operator requests structured output for artifacts in each of the
  three standings
- **THEN** each carries its standing as a value a caller can branch on
- **AND** the standing is not inferred from the presence or absence of another
  field

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

### Requirement: Derive an artifact's state from the evidence axes

Coquena SHALL report, for every artifact it enumerates, exactly one of seven
states: `untracked`, `altered`, `incomplete`, `local`, `detached`, `synced` and
`upgradable`. The state SHALL be derived when the library is read and SHALL NOT
be persisted in any sidecar.

The derivation SHALL be an ordered cascade, and the order SHALL be normative
because several conditions can hold at once:

1. an artifact with no valid sidecar is `untracked`;
2. otherwise, an artifact whose recorded verification found its bytes
   disagreeing with the digest recorded for them is `altered`;
3. otherwise, an artifact whose principal is not fully constituted is
   `incomplete`;
4. otherwise, an artifact for which no repository has been consulted is `local`;
5. otherwise, an artifact matching the repository's current revision is
   `synced`, one matching an earlier revision is `upgradable`, and one for which
   no match was established is `detached`.

`detached` SHALL name the absence of an established match and SHALL NOT name its
cause, since a repository that could not be consulted, a revision that was
destroyed, and bytes that never belonged to that repository are not
distinguishable from the artifact.

Deriving the state SHALL NOT read or hash an artifact's bytes.

A download job's status SHALL NOT be reported as an artifact state. An artifact
whose transfer has not completed is not yet an artifact on disk, and the two
vocabularies SHALL be reported in separate fields.

#### Scenario: Report a GGUF with no sidecar as untracked
- **WHEN** the configured models directory contains a `.gguf` with no valid adjacent Coquena sidecar
- **THEN** the enumeration reports it with state `untracked` and its path-derived repository and filename
- **AND** it does not fail or skip the rest of the enumeration

#### Scenario: Report an artifact adopted without a repository as local
- **WHEN** an artifact was imported and no repository has ever been consulted for it
- **THEN** Coquena reports its state as `local`

#### Scenario: Report an artifact matching its repository's current revision as synced
- **WHEN** an artifact carries an attestation naming the repository's current resolved revision
- **THEN** Coquena reports its state as `synced`

#### Scenario: Report an artifact matching an earlier revision as upgradable
- **WHEN** an artifact carries an attestation naming a resolved revision earlier than the last observed one
- **THEN** Coquena reports its state as `upgradable`

#### Scenario: Report an artifact with no established match as detached
- **WHEN** a repository was consulted for an artifact and no match was established
- **THEN** Coquena reports its state as `detached`
- **AND** the state does not name which of the possible causes applies

#### Scenario: Prefer altered over any provenance state
- **WHEN** an artifact's last recorded verification found a mismatch and it also carries an attestation
- **THEN** Coquena reports its state as `altered`
- **AND** it does not report `synced`, `upgradable` or `detached`

#### Scenario: Prefer untracked over altered
- **WHEN** an artifact has no valid sidecar
- **THEN** Coquena reports its state as `untracked`
- **AND** it does not attempt to read a verification outcome that cannot exist

#### Scenario: Derive states without hashing the library
- **WHEN** an operator lists a library of several artifacts
- **THEN** every entry reports a state
- **AND** no artifact's bytes are read to produce it

#### Scenario: Keep a download job's status out of the state vocabulary
- **WHEN** a download job for an artifact is in flight
- **THEN** the entry reports the job's status in its own field
- **AND** the job's status is never reported as one of the seven artifact states

### Requirement: Refuse to persist a sidecar for an artifact Coquena cannot classify

Coquena SHALL NOT write a sidecar whose classification it could not derive. When
an artifact cannot be opened, cannot be parsed as GGUF, declares a type Coquena
does not recognise, or could not be classified because a required remote
inspection failed, the operation SHALL fail with a diagnostic naming what could
not be determined, and SHALL leave no sidecar behind.

This SHALL apply to every operation that persists or repairs a sidecar,
including a completed download, an import, a reconciliation, and the repair of a
stale derivation.

An artifact that cannot be classified therefore has no sidecar and SHALL be
reported as `untracked`. Coquena SHALL NOT record an artifact as managed with an
unknown type.

Enumerating an artifact that has no sidecar SHALL remain best-effort and SHALL
report an undetermined type without failing the enumeration, because nothing is
being recorded.

#### Scenario: Refuse to publish a completed download that cannot be classified
- **WHEN** a download completes and the resulting file cannot be parsed as GGUF
- **THEN** Coquena reports the failure and writes no sidecar
- **AND** the artifact is not reported as managed

#### Scenario: Refuse to import an artifact declaring an unrecognised type
- **WHEN** an operator imports a `.gguf` whose declared type Coquena does not recognise
- **THEN** Coquena reports the failure and writes no sidecar
- **AND** the artifact continues to be reported as `untracked`

#### Scenario: Refuse to reconcile when remote inspection fails
- **WHEN** reconciling a repository and the remote inspection of one of its candidates fails
- **THEN** Coquena reports the failure and writes no sidecar for the affected artifact
- **AND** it does not record that artifact with an undetermined type
- **AND** it does not silently drop that artifact from the reconciliation target set

#### Scenario: Refuse to repair a derivation that cannot be recomputed
- **WHEN** `verify` repairs a stale derivation and the artifact can no longer be parsed
- **THEN** Coquena reports the failure and leaves the existing sidecar unchanged

#### Scenario: Enumerate an unreadable file without failing the walk
- **WHEN** the configured models directory contains a file that cannot be classified and has no sidecar
- **THEN** the enumeration reports it as `untracked` with an undetermined type
- **AND** the remaining artifacts are still enumerated

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

### Requirement: Derive an artifact's eligibility for a preset from its state

Coquena SHALL report, for every artifact in the library, whether it is eligible
to be referenced by a preset. Usability SHALL mean exactly that eligibility: it
SHALL NOT be an independent judgement of an artifact's quality, and no artifact
SHALL be reported unusable for lacking evidence about its origin.

Usability SHALL be derived wholly from the artifact's state. An artifact in
state `local`, `detached`, `synced` or `upgradable` SHALL be usable; one in state
`untracked`, `altered` or `incomplete` SHALL be unusable. Coquena SHALL NOT
report a separate usability reason, because the state is the reason. An
unattested artifact SHALL remain usable, since a privately obtained model has no
repository to be attested against and is not thereby deficient.

An artifact classified as a companion SHALL be usable irrespective of which
principals the library currently holds. A companion cannot perform inference
alone, but neither can any companion, and which principal it accompanies is
decided by the preset that binds them rather than by the contents of the library
at the moment it is read. Coquena MAY report that no compatible principal is
currently installed as advice, and SHALL NOT report it as a reason the artifact
is unusable. A principal SHALL likewise remain usable when no compatible
companion is installed.

Usability SHALL be derived when the library is read rather than persisted.

#### Scenario: Report a synced principal as usable
- **WHEN** an artifact in state `synced` is classified as a principal model
- **THEN** Coquena reports it as usable

#### Scenario: Report a principal without its companion as usable
- **WHEN** a principal in a usable state has no compatible projector installed
- **THEN** Coquena still reports the principal as usable

#### Scenario: Report a companion without its principal as usable
- **WHEN** a projector in a usable state is compatible with no installed principal
- **THEN** Coquena reports it as usable
- **AND** it may note that no compatible principal is installed as advice
- **AND** that note is not a reason the artifact is unusable

#### Scenario: Import a companion before the model it will accompany
- **WHEN** an operator imports a projector intending to fetch its principal next
- **THEN** the projector is usable from the moment it is imported
- **AND** it does not become usable only once some other artifact arrives

#### Scenario: Report an untracked artifact as unusable
- **WHEN** an artifact has no valid sidecar and is therefore in state `untracked`
- **THEN** Coquena reports it as unusable
- **AND** it reports no reason beyond the state

#### Scenario: Report an artifact whose bytes were found altered as unusable
- **WHEN** the last recorded verification of an artifact found its bytes
  disagreeing with its `integrity` digest
- **THEN** Coquena reports its state as `altered` and reports it as unusable
- **AND** the artifact's file is not hashed to reach that answer

#### Scenario: Report a detached artifact as usable
- **WHEN** a repository was consulted for an artifact and no match was established
- **THEN** Coquena reports it as usable
- **AND** it is not reported as deficient for lacking an attestation

#### Scenario: Report an unattested artifact as usable
- **WHEN** an artifact carries an `integrity` digest and no attestation
- **THEN** Coquena reports it as usable
- **AND** it is not reported as deficient for lacking one

#### Scenario: Reflect a change in the library without rewriting sidecars
- **WHEN** a compatible principal is installed alongside an existing projector
- **THEN** the advice that no compatible principal was installed is no longer
  reported on the next read
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
