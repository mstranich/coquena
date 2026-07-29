## Purpose

Define CLI and local WebUI interfaces for shared GGUF model management.
## Requirements
### Requirement: Expose shared model management through the CLI
Coquena SHALL expose `coquena model resolve <repository-or-url>`, `coquena model discover <repository-or-url[:selector]>`, `coquena model inspect <reference>`, and `coquena model download <repository-or-url[:selector]>`, plus download management as `coquena model download list`, `status <job-id>`, `pause <job-id>`, `resume <job-id>`, and `cancel <job-id>`. The shorter top-level `coquena download ...` forms SHALL remain accepted aliases. Every command addressing an artifact already present in the models directory SHALL name it through the `user/repo[@revision]:QUANT` reference grammar and SHALL NOT require a filesystem path, so one logical name identifies an artifact across resolution, inspection, reconciliation, binding, verification, and update checking. `inspect` SHALL operate only on a local artifact and SHALL issue no network request; remote exploration SHALL be reached through `discover`. Human `inspect` output SHALL render the artifact's classification and its scalar metadata as a `Metadata`/`Value` table rather than the resolved revision alone. These commands SHALL invoke the shared model service and SHALL support one `--json` option that emits exactly one schema-version-1 JSON document on standard output.

#### Scenario: Automate model inspection
- **WHEN** an operator invokes a supported model command with `--json`
- **THEN** standard output contains only a newline-terminated schema-version-1 JSON document
- **AND** operational diagnostics do not contaminate that document

#### Scenario: Reject invalid model command input
- **WHEN** an operator supplies malformed, ambiguous, unsupported, or missing command input
- **THEN** the CLI exits unsuccessfully with usage or a safe diagnostic
- **AND** it creates no job or model artifact

#### Scenario: Read a classified inspection in human output
- **WHEN** an operator inspects a local artifact without `--json`
- **THEN** the output presents its declared type and scalar metadata as a two-column table
- **AND** it does not reduce the result to the resolved commit SHA

#### Scenario: Address an installed artifact by its logical name
- **WHEN** an operator inspects or reads the revision of an installed artifact
- **THEN** Coquena accepts the same reference form used to bind, verify, or check it for updates
- **AND** it does not require the operator to supply the artifact's filesystem path

#### Scenario: Refuse to address an unreconciled artifact
- **WHEN** an operator inspects or reads the revision of an artifact that has not been reconciled
- **THEN** Coquena reports that no installed artifact matches the reference
- **AND** the artifact becomes addressable only once it has been reconciled

#### Scenario: Name the candidates behind an ambiguous selection
- **WHEN** a command's selector matches zero or several GGUF artifacts
- **THEN** the CLI reports the filenames available for selection
- **AND** it does not report only how many matched

#### Scenario: Manage a download below the model command
- **WHEN** an operator invokes `coquena model download status <job-id>` or another supported job action
- **THEN** Coquena applies that action to the named durable job
- **AND** it does not interpret the action name as a Hugging Face repository reference

#### Scenario: Read human download status
- **WHEN** an operator invokes `coquena download list` without `--json`
- **THEN** Coquena renders a tabular global download view containing ID, model, status, human-readable percentage/byte progress, and target path

#### Scenario: Interrupt a resumed model download
- **WHEN** an operator presses Ctrl+C while a CLI resume is transferring a model
- **THEN** Coquena cancels the active request, persists the job as paused, and reports that the download was paused
- **AND** the job remains resumable from its reconciled partial length

#### Scenario: Read human progress logs
- **WHEN** a CLI or WebUI-backed model transfer emits its throttled operational progress log
- **THEN** downloaded and total sizes use human-readable binary units
- **AND** the log includes a percentage while JSON/API job state retains exact byte counts

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

