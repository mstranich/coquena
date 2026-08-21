## ADDED Requirements

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

## REMOVED Requirements

### Requirement: Report whether an artifact is eligible for a preset

**Reason**: The requirement made usability a judgement over several independent
conditions — reconciliation, declared type, verification outcome — each with its
own reason string. The decision `model-artifact-states` settles that usability is
derived wholly from the state, which makes the reason enumeration a second place
to keep the same answer correct.

**Migration**: Replaced by "Derive an artifact's eligibility for a preset from
its state". Eligibility is unchanged for every artifact that has a state; the
`unclassifiable` case disappears because Coquena no longer records a managed
artifact it could not classify.

### Requirement: List a GGUF without a valid sidecar as unreconciled

**Reason**: The state `unreconciled` is retired. It was named after the
`reconcile` command, which this change splits into `import` and `reconcile`, and
it described the artifact by which command had not been run on it rather than by
what the artifact is.

**Migration**: Replaced by "Derive an artifact's state from the evidence axes".
Enumeration still includes every `.gguf` with a path-derived identity; the state
it reports is now `untracked`. The rule that such an entry is never selected when
resolving a reference is preserved by usability deriving from the state.
