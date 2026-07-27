## MODIFIED Requirements

### Requirement: List installed models through the CLI
Coquena SHALL provide a CLI command that lists the models present in the configured model directory, reporting for each installed one the reference form accepted when binding it and its resolved artifact filename, and reporting each unreconciled `.gguf` separately with its path-derived identity and a status distinguishing it from an installed model. The command SHALL issue no network request and SHALL report an empty list rather than an error when no model is present.

#### Scenario: List installed models
- **WHEN** an operator lists models with artifacts installed
- **THEN** the output reports each installed model's repository, quant reference form, and resolved filename
- **AND** the command issues no network request

#### Scenario: List an empty library
- **WHEN** an operator lists models with no artifact installed
- **THEN** the command reports an empty list and succeeds

#### Scenario: List models as JSON
- **WHEN** an operator requests JSON output
- **THEN** the output is machine-readable and reports the same installed models

#### Scenario: List an unreconciled model
- **WHEN** the configured models directory contains a `.gguf` without a valid Coquena sidecar
- **THEN** the listing reports it distinctly from an installed model, with its path-derived repository and filename
- **AND** the command issues no network request

## ADDED Requirements

### Requirement: Reconcile a model without a valid sidecar
Coquena SHALL provide `coquena model reconcile <path> --online` and `coquena model reconcile <path> --accept`, exactly one of which SHALL be required per invocation. `--online` SHALL match the file against Hugging Face using its path-derived repository, compare a locally computed SHA-256 digest against the matched artifact's reported content hash when Hugging Face provides one, and on agreement persist a full Hugging Face-provenance sidecar. `--accept` SHALL persist a `local`-provider sidecar from path-derived identity alone, without any network request. Both modes SHALL persist a computed SHA-256 digest.

#### Scenario: Reconcile online against Hugging Face
- **WHEN** an operator reconciles a path-identified `.gguf` with `--online` and its content matches the corresponding Hugging Face artifact
- **THEN** Coquena persists a full sidecar as if the file had been downloaded by Coquena
- **AND** the model becomes resolvable and bindable afterward

#### Scenario: Refuse an online reconciliation on content mismatch
- **WHEN** the local file's computed digest disagrees with the content hash Hugging Face reports for the matched artifact
- **THEN** Coquena refuses to reconcile and writes no sidecar
- **AND** it reports the mismatch rather than asserting a false identity

#### Scenario: Accept a model offline without network access
- **WHEN** an operator reconciles a path-identified `.gguf` with `--accept`
- **THEN** Coquena persists a `local`-provider sidecar from the derived repository and filename
- **AND** it issues no network request
- **AND** the model becomes resolvable and bindable afterward

#### Scenario: Reject reconciling with neither or both modes
- **WHEN** an operator invokes `reconcile` without `--online`/`--accept` or with both
- **THEN** Coquena reports a usage error
- **AND** it writes no sidecar

### Requirement: Verify local model integrity on demand
Coquena SHALL provide `coquena model verify <ref>`, which re-hashes the resolved installed file and compares it against the sidecar's persisted SHA-256 digest. It SHALL distinguish a hash match, a hash mismatch, and the absence of a persisted digest, and SHALL NOT run as a side effect of any other command.

#### Scenario: Verify an intact model
- **WHEN** an operator verifies a model whose current file digest matches its sidecar
- **THEN** Coquena reports the model as verified

#### Scenario: Detect local corruption
- **WHEN** an operator verifies a model whose current file digest disagrees with its sidecar
- **THEN** Coquena marks the model unusable
- **AND** it reports the option to re-download or reconcile the model again

#### Scenario: Verify a model with no recorded digest
- **WHEN** an operator verifies a model installed before a digest was ever persisted for it
- **THEN** Coquena reports that no baseline digest is available
- **AND** it does not report this as a mismatch

### Requirement: Check for remote model updates on demand
Coquena SHALL provide `coquena model check-updates <ref>`, which queries Hugging Face using only the installed model's recorded repository, requested revision, and filename, without reading or hashing the local file. It SHALL report whether Hugging Face's current content for that path differs from what is recorded locally, degrading to a commit-revision-only comparison when Hugging Face reports no content hash for the artifact. It SHALL NOT run automatically from any listing command.

#### Scenario: Report an up-to-date model
- **WHEN** an operator checks updates for a model whose recorded revision and content hash still match Hugging Face's current state
- **THEN** Coquena reports the model as up to date

#### Scenario: Detect remote content replacement under the same path
- **WHEN** Hugging Face now serves different content at the same repository path and requested revision than what is recorded locally
- **THEN** Coquena reports that an update is available
- **AND** it names the newly observed revision

#### Scenario: Refuse to check updates for a local-provider model
- **WHEN** an operator checks updates for a model reconciled with `--accept`
- **THEN** Coquena refuses with a diagnostic explaining no Hugging Face identity is recorded for it
