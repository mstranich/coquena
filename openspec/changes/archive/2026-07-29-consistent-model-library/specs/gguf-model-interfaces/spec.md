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

#### Scenario: Nest a companion beneath its principal
- **WHEN** the library holds a principal model and a projector compatible with it
- **THEN** the listing does not nest the projector beneath that principal
- **AND** the nesting this scenario previously required is withdrawn, because a projector is compatible with every quantization of its principal and nesting it under one asserts a relation that does not exist

#### Scenario: List an unmatched companion
- **WHEN** the library holds a projector compatible with no installed principal
- **THEN** the listing reports it with its type
- **AND** it reports the projector as unusable, naming the absent compatible principal as the reason

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

#### Scenario: List an unreconciled model
- **WHEN** the configured models directory contains a `.gguf` without a valid Coquena sidecar
- **THEN** the listing reports it distinctly from an installed model, with its path-derived repository and filename
- **AND** the command issues no network request

### Requirement: Reconcile a model without a valid sidecar
Coquena SHALL provide `coquena model reconcile <reference> --online`, which reconciles the artifacts of one repository in a single action. It SHALL resolve the repository named by the `user/repo[@revision]:QUANT` reference grammar, enumerate that repository's remote candidates with their classification without downloading any complete artifact, and match them against the artifacts present on disk by filename, corroborated by metadata. A reference naming a repository that holds a principal and its companions SHALL reconcile all of them, since they are parts of one logical unit; a reference matching more than one principal SHALL be refused as ambiguous, naming the candidates, since choosing among quantizations is the operator's decision. Reconciliation SHALL persist a full Hugging Face-provenance sidecar for each artifact whose locally computed SHA-256 digest agrees with the content hash Hugging Face reports, SHALL refuse any artifact whose digest disagrees, and SHALL report a remote candidate that is absent locally rather than omitting it. Coquena SHALL provide `coquena model reconcile <reference> --accept` for a single artifact, persisting a `local`-provider sidecar from path-derived identity alone without any network request. Reconciliation SHALL operate on one artifact or one repository per invocation and SHALL NOT accept an option reconciling the whole configured models directory. Reconciliation SHALL remain an explicit operator action, SHALL NOT run as a side effect of any listing command, and SHALL NOT accept `--json`, since it prompts and therefore cannot emit exactly one document.

#### Scenario: Reconcile a listed artifact by its reference
- **WHEN** an operator reconciles an artifact using the reference form the listing reports for it
- **THEN** Coquena resolves the repository that reference names and reconciles the artifacts it holds
- **AND** the operator does not have to supply a filesystem path

#### Scenario: Report an unmatched reconciliation reference
- **WHEN** a reference names no artifact awaiting reconciliation
- **THEN** the diagnostic reports that no such artifact matched
- **AND** it does not report the artifact as not installed, which would invert the subject of the command

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

#### Scenario: Reconcile online against Hugging Face
- **WHEN** an operator reconciles a referenced repository with `--online` and its artifacts match the corresponding Hugging Face artifacts
- **THEN** Coquena persists a full sidecar for each as if they had been downloaded by Coquena
- **AND** the models become resolvable and bindable afterward under the same references

#### Scenario: Refuse an online reconciliation on content mismatch
- **WHEN** a local artifact's computed digest disagrees with the content hash Hugging Face reports for the matched sibling
- **THEN** Coquena refuses that artifact and writes no sidecar for it
- **AND** the diagnostic reports that the artifact may be from an earlier revision rather than asserting corruption

#### Scenario: Accept a model offline without network access
- **WHEN** an operator reconciles a single referenced `.gguf` with `--accept`
- **THEN** Coquena persists a `local`-provider sidecar from the derived repository and filename
- **AND** it issues no network request

#### Scenario: Reconcile a whole directory in one pass
- **WHEN** an operator asks to reconcile every artifact beneath the configured models directory in one invocation
- **THEN** Coquena reports that the option is not accepted
- **AND** the whole-directory pass this scenario previously required is withdrawn, because it conflated cases needing an operator decision with cases that do not

#### Scenario: Continue a bulk reconciliation past one failure
- **WHEN** one artifact of a reconciled repository cannot be reconciled
- **THEN** Coquena reports that artifact's diagnostic and reconciles the remaining ones
- **AND** it does not abandon the repository

#### Scenario: Reject reconciling with neither or both modes
- **WHEN** an operator invokes `reconcile` without `--online`/`--accept` or with both
- **THEN** Coquena reports a usage error
- **AND** it writes no sidecar

#### Scenario: Reject reconciling the whole library
- **WHEN** an operator asks to reconcile every artifact beneath the configured models directory in one invocation
- **THEN** Coquena reports that the option is not accepted
- **AND** it writes no sidecar

#### Scenario: Reject a machine-readable reconciliation
- **WHEN** an operator invokes reconciliation with `--json`
- **THEN** Coquena reports that the option does not apply to this command
- **AND** it does not silently ignore the option
