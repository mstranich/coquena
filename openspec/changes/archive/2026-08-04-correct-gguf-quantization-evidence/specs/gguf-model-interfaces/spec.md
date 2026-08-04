## MODIFIED Requirements

### Requirement: Expose shared model management through the CLI

Coquena SHALL expose `coquena model resolve <repository-or-url>`, `coquena model
discover <repository-or-url[:selector]>`, `coquena model download
<repository-or-url[:selector]>`, `coquena model revision <reference>`,
`coquena model list`, `coquena model verify <reference>`, `coquena model
check-updates <reference>`, and the accepted reconciliation and download-job
subcommands. The CLI SHALL NOT expose `coquena model inspect`; local byte
evidence and recorded evidence SHALL be compared through `verify`, while the
internal inspection projection remains available to non-CLI workflows.

Non-interactive commands SHALL support one `--json` option that emits exactly
one schema-versioned JSON document on standard output. Commands addressing one
installed artifact SHALL use the currently supported model reference grammar,
shall refuse ambiguity safely, and SHALL NOT require a filesystem path.

#### Scenario: Reject the removed inspect command

- **WHEN** an operator invokes `coquena model inspect`
- **THEN** the CLI reports usage that does not advertise the removed command
- **AND** no local or remote state changes

#### Scenario: Preserve internal inspection for download

- **WHEN** a managed download completes after its GGUF header has been inspected
- **THEN** the download workflow may use the internal inspection projection to
  construct the sidecar
- **AND** removing the CLI command does not require parsing the header twice

#### Scenario: Automate a supported model command

- **WHEN** an operator invokes a supported non-interactive model command with
  `--json`
- **THEN** standard output contains only one newline-terminated,
  schema-versioned JSON document
- **AND** diagnostics do not contaminate that document

#### Scenario: Reject invalid model command input

- **WHEN** an operator supplies malformed, ambiguous, unsupported, or missing
  command input
- **THEN** the CLI exits unsuccessfully with usage or a safe diagnostic
- **AND** it creates no job or model artifact

#### Scenario: Address an installed artifact by its logical name

- **WHEN** an operator reads the revision, verifies, or checks updates for an
  installed artifact
- **THEN** Coquena accepts the same currently supported reference family
- **AND** it does not require a filesystem path

#### Scenario: Name candidates behind an ambiguous selection

- **WHEN** a command requiring one artifact matches zero or several GGUF
  artifacts
- **THEN** the CLI reports the available candidate filenames
- **AND** it does not choose one implicitly

#### Scenario: Manage a download below the model command

- **WHEN** an operator invokes `coquena model download status <job-id>` or
  another supported job action
- **THEN** Coquena applies that action to the named durable model job
- **AND** it does not interpret the action name as a repository reference

### Requirement: Verify local model integrity and repair derived evidence on demand

Coquena SHALL provide `coquena model verify <ref>`, which re-hashes the resolved
installed file and compares it with the sidecar's persisted SHA-256 baseline.
Default verification SHALL distinguish a hash match, hash mismatch, and absence
of a baseline; it SHALL make no network request and SHALL NOT modify local
state.

Coquena SHALL accept `--repair-derived` for one referenced artifact. Repair
SHALL proceed only after a hash match and SHALL recalculate only locally derived
GGUF evidence and its derivation version. It SHALL preserve model bytes, the
persisted digest baseline, source/provenance, attestation, remote drift, and
operator ownership. Hash mismatch or absent baseline SHALL refuse repair. The
command SHALL NOT accept a bulk repair mode.

#### Scenario: Verify an intact model read-only

- **WHEN** an operator verifies a model whose current digest matches its sidecar
  without requesting repair
- **THEN** Coquena reports the model as verified
- **AND** it writes no sidecar or verification timestamp

#### Scenario: Detect local corruption

- **WHEN** an operator verifies a model whose current digest disagrees with its
  sidecar
- **THEN** Coquena reports verification failure and an appropriate recovery
  direction
- **AND** it does not modify bytes or metadata

#### Scenario: Verify a model with no recorded digest

- **WHEN** an operator verifies a model with no persisted digest baseline
- **THEN** Coquena reports that no baseline is available
- **AND** it does not report a match or repair derived evidence

#### Scenario: Repair stale derived evidence

- **WHEN** an operator invokes `verify <ref> --repair-derived` for an artifact
  whose digest matches and whose derivation is stale
- **THEN** Coquena writes current classification, parameter count, physical
  evidence when available, and derivation version
- **AND** it preserves every non-derived authority field and the model bytes

#### Scenario: Refuse repair after a digest mismatch

- **WHEN** `verify <ref> --repair-derived` computes a digest that differs from
  the stored baseline
- **THEN** Coquena refuses the repair
- **AND** the sidecar and model bytes remain unchanged

#### Scenario: Repair a supported legacy sidecar

- **WHEN** an otherwise valid schema-version-2 sidecar is verified with
  `--repair-derived` and its digest matches
- **THEN** Coquena publishes the current sidecar shape with current derivation
- **AND** it preserves the legacy record's valid source and artifact evidence

#### Scenario: Reject bulk repair

- **WHEN** an operator requests `verify --all` or an equivalent library-wide
  repair
- **THEN** Coquena rejects the invocation
- **AND** it does not repair any artifact
