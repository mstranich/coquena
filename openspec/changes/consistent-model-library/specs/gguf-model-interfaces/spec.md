## MODIFIED Requirements

### Requirement: List installed models through the CLI
Coquena SHALL provide a CLI command that lists the models present in the configured model directory, reporting for each installed one the reference form accepted when binding it and its resolved artifact filename, and reporting each unreconciled `.gguf` separately with its path-derived identity and a status distinguishing it from an installed model. The listing SHALL report every artifact at the same level and SHALL NOT nest one artifact beneath another, since a projector is compatible with every quantization of its principal and nesting it beneath one of them asserts a relation the domain does not have. Each entry SHALL report the artifact's declared type and whether it is usable, with a reason when it is not. The command SHALL issue no network request and SHALL report an empty list rather than an error when no model is present.

#### Scenario: List installed models
- **WHEN** an operator lists models with artifacts installed
- **THEN** the output reports each installed model's repository, quant reference form, and resolved filename
- **AND** the command issues no network request

#### Scenario: Report a companion at the same level as any other artifact
- **WHEN** the library holds a principal model and a projector compatible with it
- **THEN** the listing reports both as independent entries with their declared types
- **AND** it does not nest either beneath the other

#### Scenario: Report the type of an artifact that has no sidecar
- **WHEN** the configured models directory contains a `.gguf` written by another application
- **THEN** the listing reports its declared type read from its own header
- **AND** it reports the artifact as unreconciled and unusable

#### Scenario: Report why an artifact is unusable
- **WHEN** the listing reports an artifact that cannot take part in an invocation
- **THEN** it names the reason
- **AND** the reason distinguishes an unreconciled artifact from an orphaned projector

#### Scenario: List an empty library
- **WHEN** an operator lists models with no artifact installed
- **THEN** the command reports an empty list and succeeds

#### Scenario: List models as JSON
- **WHEN** an operator requests JSON output
- **THEN** the output is machine-readable and reports the same artifacts with their type and usability

### Requirement: Reconcile a model without a valid sidecar
Coquena SHALL provide `coquena model reconcile <reference> --online`, which reconciles the artifacts of one repository in a single action. It SHALL resolve the repository named by the `user/repo[@revision]:QUANT` reference grammar, enumerate that repository's remote candidates with their classification without downloading any complete artifact, and match them against the artifacts present on disk by filename, corroborated by metadata. A reference naming a repository that holds a principal and its companions SHALL reconcile all of them, since they are parts of one logical unit; a reference matching more than one principal SHALL be refused as ambiguous, naming the candidates, since choosing among quantizations is the operator's decision. Reconciliation SHALL persist a full Hugging Face-provenance sidecar for each artifact whose locally computed SHA-256 digest agrees with the content hash Hugging Face reports, SHALL refuse any artifact whose digest disagrees, and SHALL report a remote candidate that is absent locally rather than omitting it. Coquena SHALL provide `coquena model reconcile <reference> --accept` for a single artifact, persisting a `local`-provider sidecar from path-derived identity alone without any network request; `--accept` SHALL NOT be combined with reconciliation of a whole repository or library, since accepting an artifact asserts its identity and that claim cannot be made in bulk. Reconciliation SHALL remain an explicit operator action, SHALL NOT run as a side effect of any listing command, and SHALL NOT accept `--json`, since it prompts and therefore cannot emit exactly one document.

#### Scenario: Reconcile a repository holding a principal and its companion
- **WHEN** an operator reconciles a reference naming a repository that holds a principal model and a compatible projector
- **THEN** Coquena reconciles both artifacts in one action
- **AND** neither is left unreconciled

#### Scenario: Refuse a reference matching several principals
- **WHEN** a reference matches more than one principal model in the repository
- **THEN** Coquena refuses as ambiguous and names the matching artifacts
- **AND** it does not choose a quantization on the operator's behalf

#### Scenario: Report a companion missing from disk
- **WHEN** a repository's remote candidates include a companion that is not present locally
- **THEN** Coquena reports the companion as available remotely and absent locally
- **AND** it reconciles the artifacts that are present

#### Scenario: Refuse an artifact whose content disagrees
- **WHEN** a local artifact's computed digest disagrees with the content hash Hugging Face reports for the matched sibling
- **THEN** Coquena refuses that artifact and writes no sidecar for it
- **AND** the diagnostic reports that the artifact may be from an earlier revision rather than asserting corruption

#### Scenario: Accept one artifact offline
- **WHEN** an operator reconciles a single referenced `.gguf` with `--accept`
- **THEN** Coquena persists a `local`-provider sidecar from the derived repository and filename
- **AND** it issues no network request

#### Scenario: Reject accepting a whole repository or library
- **WHEN** an operator combines `--accept` with reconciliation of more than one artifact
- **THEN** Coquena reports a usage error
- **AND** it writes no sidecar

#### Scenario: Reject a machine-readable reconciliation
- **WHEN** an operator invokes reconciliation with `--json`
- **THEN** Coquena reports that the option does not apply to this command
- **AND** it does not silently ignore the option

## ADDED Requirements

### Requirement: Reconcile a whole library as triage
Coquena SHALL accept `--all` in place of a reference, reconciling online every repository beneath the configured models directory that can be resolved without an operator decision, and reporting every remaining case rather than resolving it. A repository SHALL be automatically resolvable when it resolves remotely, each local artifact matches a remote sibling by filename, each matched artifact's content hash agrees, and no ambiguity among principals remains. Every case that is not automatically resolvable SHALL be reported with its reason and with the command that resolves it. A per-repository failure SHALL NOT abandon the pass. Because the pass computes a content hash over every artifact, Coquena SHALL report the number of artifacts and their total size and SHALL require confirmation before beginning, accepting an explicit option to proceed without prompting.

#### Scenario: Reconcile every unambiguous repository
- **WHEN** an operator runs a triage pass over a library whose repositories all resolve and match
- **THEN** Coquena reconciles each of them
- **AND** it reports no remaining case

#### Scenario: Report a case needing a decision
- **WHEN** a repository cannot be reconciled without an operator decision
- **THEN** Coquena reports the reason and the command that resolves it
- **AND** it continues with the remaining repositories

#### Scenario: Confirm before hashing a large library
- **WHEN** an operator begins a triage pass
- **THEN** Coquena reports how many artifacts it will hash and their total size
- **AND** it does not begin until the operator confirms

#### Scenario: Proceed without prompting
- **WHEN** an operator supplies the option accepting the pass in advance
- **THEN** Coquena begins without prompting
