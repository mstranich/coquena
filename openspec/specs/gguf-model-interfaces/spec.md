## Purpose

Define CLI and local WebUI interfaces for shared GGUF model management.
## Requirements
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

### Requirement: Expose model management through the local HTTP API
The local WebUI server SHALL expose schema-version-1 JSON APIs for resolving and inspecting GGUF artifacts, discovering remote repository candidates with their classification, discovering installed and incomplete model units, lazily reading an installed artifact's classification, serving cached publisher avatars, creating a download job, listing jobs, inspecting a job, and pausing, resuming, or cancelling a job. Inspection responses SHALL return the derived classification and scalar metadata only; the API SHALL NOT offer an option to return the raw GGUF metadata map. Accepted creation SHALL return `202` with an opaque job ID; job progress and terminal state SHALL be available through polling and an SSE endpoint. Invalid API paths, input, and methods SHALL return API errors or `405`, never an Angular document.

#### Scenario: Create and observe a download job
- **WHEN** a browser posts one valid resolved GGUF selection to the download API
- **THEN** it receives `202` with one job ID
- **AND** it can obtain that job's current progress and terminal status through the API

#### Scenario: Reject a raw-metadata request
- **WHEN** a browser requests an inspection with an option asking for raw metadata
- **THEN** the server rejects the unknown property rather than returning the raw metadata map

#### Scenario: Reject a malformed API operation
- **WHEN** a browser sends malformed JSON, an unknown property, an unknown job ID, or an unsupported method
- **THEN** the server returns a non-success JSON response or `405`
- **AND** it does not change job or model state

### Requirement: Present API-backed GGUF management in Models
The Angular Models workspace SHALL resolve public Hugging Face GGUF candidates, present responsive metadata summaries for an explicit selected artifact before download, discover installed and incomplete model units, create and observe download jobs, and provide pause, resume, and cancel actions as permitted by job state. The workspace SHALL visibly show backend diagnostics and SHALL use GGUF metadata as read-only reference rather than modifying manual fields or presets.

The inspection view SHALL present metadata as a two-column `Metadata`/`Value` table rather than a raw JSON document and SHALL not render implementation-oriented omission notices.

Each discovered candidate SHALL own independent inspection/loading state and SHALL reveal its collapsible metadata table within that candidate's entry, including the candidate's declared type. Installed-model library responses SHALL omit metadata; expanding an installed entry SHALL lazily load its classification and render the same collapsible table.

Discovery entries SHOULD display the publisher avatar from Hugging Face when available. Incomplete and installed entries SHOULD display the namespace-level avatar cached beneath the configured models directory and SHALL NOT depend on a remote image after job creation.

#### Scenario: Download a selected variant from Models
- **WHEN** an operator selects one resolved GGUF variant and requests download
- **THEN** the workspace starts only that artifact's job and renders its progress and current state
- **AND** it refreshes the model/job view after a terminal outcome

#### Scenario: Observe multiple incomplete models
- **WHEN** the configured models tree contains multiple incomplete downloads or a new download starts
- **THEN** each model appears immediately as its own library entry with independent state and progress
- **AND** pause, resume, and cancel act directly on that entry without selecting a shared download-management panel

#### Scenario: Expand an installed entry's classification
- **WHEN** an operator expands an installed model entry
- **THEN** the workspace lazily loads that artifact's classification and renders the collapsible table
- **AND** it does not request or render a raw metadata document

#### Scenario: Inspect metadata while configuring a preset
- **WHEN** an operator views a downloaded model's metadata and its manual preset controls
- **THEN** the workspace displays metadata as a reference
- **AND** it does not automatically create, change, or select a preset value

### Requirement: Query installed revision through the CLI
Coquena SHALL expose `coquena model revision <model-reference>` using the shared model service. The revision command SHALL emit the installed resolved SHA in human output and repository, filename, requested revision, and resolved revision in schema-version-1 JSON output.

#### Scenario: Read an installed model revision
- **WHEN** an operator invokes `coquena model revision` for a consistent installed model
- **THEN** human output contains its resolved commit SHA
- **AND** `--json` includes its repository, filename, requested revision, and resolved revision

### Requirement: Delegate model download management to the global lifecycle
The model CLI SHALL keep `coquena model download` as a model-specific creation
and convenience entry point. Its `list`, `status`, `pause`, `resume`, and
`cancel` actions SHALL use the same durable job identity, status values,
progress representation, and safety semantics as `coquena download`, filtered
to `TYPE=model`.

#### Scenario: Manage a model job through either CLI entry point
- **WHEN** an operator queries or resumes a valid model job through the model-specific or global command family
- **THEN** both commands address the same job and report equivalent state
- **AND** resume preserves the model's existing adjacent partial-file and sidecar safety guarantees

### Requirement: List installed models through the CLI
Coquena SHALL provide a CLI command that lists every model present in the configured model directory at the artifact level. For each installed artifact it SHALL report its canonical `user/repo[@revision]:filename.gguf` reference, exact filename, resolved revision, and any logical `user/repo[@revision]:QUANT` selector that describes its quantization class. The logical selector SHALL be presented as a potentially non-unique class and SHALL NOT replace the canonical artifact reference.

The listing SHALL report each unreconciled `.gguf` separately with its path-derived identity and a status distinguishing it from an installed model. It SHALL NOT nest companions beneath principals, because doing so asserts a relation that does not hold across all quantizations. Each entry SHALL report the artifact's declared type and whether it is usable, with a reason when it is not. The command SHALL issue no network request and SHALL report an empty list rather than an error when no model is present.

#### Scenario: List canonical and logical references
- **WHEN** an operator lists installed artifacts
- **THEN** each installed entry reports its canonical exact-filename reference
- **AND** it may additionally report the logical quantization selector without claiming that selector is unique

#### Scenario: List two variants in one quantization class
- **WHEN** two installed filenames share the same declared quantization
- **THEN** the listing reports two different canonical artifact references and the shared logical selector
- **AND** neither artifact is hidden or selected as the preferred variant

#### Scenario: Report a companion at the same level as any other artifact
- **WHEN** the library holds a principal model and a projector compatible with it
- **THEN** the listing reports both as independent entries with canonical references and declared types
- **AND** it does not nest either beneath the other

#### Scenario: Nest a companion beneath its principal
- **WHEN** the library holds a principal model and a projector compatible with it
- **THEN** the listing does not nest the projector beneath that principal
- **AND** the nesting this scenario previously required remains withdrawn

#### Scenario: List an unmatched companion
- **WHEN** the library holds a projector compatible with no installed principal
- **THEN** the listing reports it with its canonical reference and type
- **AND** it reports the projector as unusable, naming the absent compatible principal as the reason

#### Scenario: Report an unreconciled artifact
- **WHEN** the configured models directory contains a `.gguf` without a valid matching sidecar
- **THEN** the listing reports its path-derived repository and filename distinctly from a canonical installed identity
- **AND** it does not fabricate revision provenance

#### Scenario: Report the type and reason for an unusable artifact
- **WHEN** the listing reports a sidecar-less artifact or an orphaned projector
- **THEN** it reports the artifact's declared type read from its own header
- **AND** it names a usability reason distinguishing unreconciled from orphaned

#### Scenario: List an empty library
- **WHEN** the configured models directory contains no model artifacts
- **THEN** the command reports an empty list and succeeds
- **AND** it issues no network request

#### Scenario: List models as JSON
- **WHEN** an operator requests JSON output
- **THEN** it reports the same canonical references, logical selectors, types, revisions, and usability as machine-readable fields
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

### Requirement: Download a principal artifact with its companions
Coquena SHALL download exactly one artifact per `coquena model download` invocation by default. It SHALL accept an explicit opt-in flag that additionally enqueues the companions identified for the selected principal, creating one independent durable job per artifact and reporting every created job ID. The flag SHALL NOT change the default single-artifact behavior when it is absent.

#### Scenario: Download one artifact by default
- **WHEN** an operator downloads a selected artifact without the companion flag
- **THEN** Coquena creates exactly one job for that artifact

#### Scenario: Enqueue companions with the principal
- **WHEN** an operator downloads a principal with the companion flag and the repository holds a compatible projector
- **THEN** Coquena creates one job per artifact and reports every job ID
- **AND** each job remains independently pausable, resumable, and cancellable

#### Scenario: Request companions where none exist
- **WHEN** an operator downloads with the companion flag from a repository holding no companion
- **THEN** Coquena creates the principal's job alone and reports that no companion was identified
