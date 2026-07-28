## MODIFIED Requirements

### Requirement: Classify a GGUF by its declared type
Coquena SHALL classify every GGUF artifact by the `general.type` value declared in its header, distinguishing at least `model` and `mmproj`, and SHALL apply the same classification to a resolved remote candidate and to an installed local file through bounded random access. Classification SHALL NOT require downloading a complete artifact. An artifact whose declared type is absent or unrecognised SHALL be reported with an explicit unknown classification rather than an inferred one. Coquena SHALL classify an artifact whenever it enumerates the library, reading the artifact's own header when no sidecar records a classification, so an artifact's type is reported before anything is written about it. Reading a header for classification SHALL NOT decode array payloads, and SHALL NOT grant the artifact any standing it did not already have.

#### Scenario: Classify a remote candidate without downloading it
- **WHEN** Coquena classifies a resolved remote GGUF candidate
- **THEN** it reports the artifact's declared type using bounded range access
- **AND** it does not retrieve the artifact's tensor payload

#### Scenario: Classify an installed artifact from disk
- **WHEN** Coquena classifies an installed GGUF
- **THEN** it reports the same declared type it would report for the identical remote artifact

#### Scenario: Classify an artifact that has no sidecar
- **WHEN** the library is enumerated and an artifact has no valid sidecar
- **THEN** Coquena reports its declared type read from the artifact's own header
- **AND** the artifact remains unreconciled and unresolvable for binding

#### Scenario: Enumerate without decoding vocabulary arrays
- **WHEN** Coquena classifies an artifact whose metadata contains vocabulary token, merge, or token-type arrays
- **THEN** it reads the scalar metadata it needs without decoding the array contents

#### Scenario: Report an unrecognised type explicitly
- **WHEN** an artifact declares no type, or a type Coquena does not recognise
- **THEN** Coquena reports the classification as unknown
- **AND** it does not infer a type from the artifact's architecture or filename

## ADDED Requirements

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
