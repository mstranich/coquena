## REMOVED Requirements

### Requirement: Persist portable per-artifact model sidecars

Coquena SHALL no longer accept a sidecar written at a previous schema version,
nor carry a single role-less digest, nor tolerate a sidecar with no digest at
all. Coquena has not been released, so there is no installed base whose upgrade
path has to be preserved, and the reader's legacy branches cost complexity in
the one place it is least affordable. The requirement is replaced by *Persist a
portable per-artifact sidecar of self-describing evidence*, which keeps every
storage rule that still holds — location, provenance, the derivation block, the
prohibition on a raw metadata map and on SQLite — and drops the compatibility
apparatus around them.


### Requirement: Report whether an artifact is usable

Coquena SHALL no longer decide usability as "can this take part in a runtime
invocation", nor report a companion unusable because no compatible principal is
currently installed. The question was answered against the contents of the
library at read time, which is not what usability is for. It is replaced by
*Report whether an artifact is eligible for a preset*, which states the question
being answered and reverses the companion rule per RFC amendment A6. Every other
case the removed requirement covered — unreconciled, unclassifiable, altered
bytes, and an unattested artifact remaining usable — is carried into the
replacement unchanged.

## ADDED Requirements

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

### Requirement: Report whether an artifact is eligible for a preset

Coquena SHALL report, for every artifact in the library, whether it is eligible
to be referenced by a preset. Usability SHALL mean exactly that eligibility: it
SHALL NOT be an independent judgement of an artifact's quality, and no artifact
SHALL be reported unusable for lacking evidence about its origin.

An artifact SHALL be usable when it is reconciled, its declared type is known,
and no recorded verification found its bytes altered. An artifact whose last
recorded verification found a mismatch SHALL be reported unusable with a reason
distinguishing it from an unreconciled artifact. An artifact whose declared type
is unknown SHALL be reported unusable. An unattested artifact SHALL remain
usable, since a privately obtained model has no repository to be attested
against and is not thereby deficient.

An artifact classified as a companion SHALL be usable irrespective of which
principals the library currently holds. A companion cannot perform inference
alone, but neither can any companion, and which principal it accompanies is
decided by the preset that binds them rather than by the contents of the library
at the moment it is read. Coquena MAY report that no compatible principal is
currently installed as advice, and SHALL NOT report it as a reason the artifact
is unusable. A principal SHALL likewise remain usable when no compatible
companion is installed.

Usability SHALL be derived when the library is read rather than persisted.

#### Scenario: Report a reconciled principal as usable
- **WHEN** a reconciled artifact is classified as a principal model
- **THEN** Coquena reports it as usable

#### Scenario: Report a principal without its companion as usable
- **WHEN** a reconciled principal has no compatible projector installed
- **THEN** Coquena still reports the principal as usable

#### Scenario: Report a companion without its principal as usable
- **WHEN** a reconciled projector is compatible with no installed principal
- **THEN** Coquena reports it as usable
- **AND** it may note that no compatible principal is installed as advice
- **AND** that note is not a reason the artifact is unusable

#### Scenario: Import a companion before the model it will accompany
- **WHEN** an operator imports a projector intending to fetch its principal next
- **THEN** the projector is usable from the moment it is imported
- **AND** it does not become usable only once some other artifact arrives

#### Scenario: Report an unreconciled artifact as unusable
- **WHEN** an artifact has no valid sidecar
- **THEN** Coquena reports it as unusable
- **AND** the reason names reconciliation as what it requires

#### Scenario: Report an unclassifiable artifact as unusable
- **WHEN** an artifact's declared type cannot be determined
- **THEN** Coquena reports it as unusable rather than listing it without a type

#### Scenario: Report an artifact whose bytes were found altered as unusable
- **WHEN** the last recorded verification of an artifact found its bytes
  disagreeing with its `integrity` digest
- **THEN** Coquena reports it as unusable
- **AND** the reason distinguishes altered bytes from an absent sidecar
- **AND** the artifact's file is not hashed to reach that answer

#### Scenario: Report an unattested artifact as usable
- **WHEN** an artifact carries an `integrity` digest and no attestation
- **THEN** Coquena reports it as usable
- **AND** it is not reported as deficient for lacking one

#### Scenario: Reflect a change in the library without rewriting sidecars
- **WHEN** a compatible principal is installed alongside an existing projector
- **THEN** the advice that no compatible principal was installed is no longer
  reported on the next read
- **AND** no sidecar was rewritten to record the change

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
