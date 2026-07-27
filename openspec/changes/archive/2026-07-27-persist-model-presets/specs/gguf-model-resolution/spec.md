## ADDED Requirements

### Requirement: Resolve a model reference against the installed library
Coquena SHALL resolve a `user/repo:QUANT` model reference against the installed model library without contacting Hugging Face or any other network service, applying the same reference grammar accepted for remote resolution. Resolution SHALL report the matched artifact's exact filename and its recorded resolved revision. Resolution SHALL fail with a distinguishable diagnostic when no installed artifact matches and when more than one matches.

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
- **WHEN** a reference is resolved offline
- **THEN** Coquena reports the revision already recorded in the artifact's sidecar
- **AND** it does not require the reference to carry a revision
