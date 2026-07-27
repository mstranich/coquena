## MODIFIED Requirements

### Requirement: Expose shared model management through the CLI
Coquena SHALL expose `coquena model resolve <repository-or-url>`, `coquena model discover <repository-or-url[:selector]>`, `coquena model inspect <model-path>`, and `coquena model download <repository-or-url[:selector]>`, plus download management as `coquena model download list`, `status <job-id>`, `pause <job-id>`, `resume <job-id>`, and `cancel <job-id>`. The shorter top-level `coquena download ...` forms SHALL remain accepted aliases. `inspect` SHALL operate only on a local artifact and SHALL issue no network request; remote exploration SHALL be reached through `discover`. Human `inspect` output SHALL render the artifact's classification and its scalar metadata as a `Metadata`/`Value` table rather than the resolved revision alone. These commands SHALL invoke the shared model service and SHALL support one `--json` option that emits exactly one schema-version-1 JSON document on standard output.

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

### Requirement: List installed models through the CLI
Coquena SHALL provide a CLI command that lists the models present in the configured model directory, reporting for each installed one the reference form accepted when binding it and its resolved artifact filename, and reporting each unreconciled `.gguf` separately with its path-derived identity and a status distinguishing it from an installed model. The listing SHALL be rendered as a tree in which a companion artifact appears nested beneath the principal it is compatible with, and a companion matching no installed principal SHALL be reported at the top level rather than omitted. The command SHALL issue no network request and SHALL report an empty list rather than an error when no model is present.

#### Scenario: List installed models
- **WHEN** an operator lists models with artifacts installed
- **THEN** the output reports each installed model's repository, quant reference form, and resolved filename
- **AND** the command issues no network request

#### Scenario: Nest a companion beneath its principal
- **WHEN** the library holds a principal model and a projector compatible with it
- **THEN** the listing shows the projector nested beneath that principal

#### Scenario: List an unmatched companion
- **WHEN** the library holds a projector compatible with no installed principal
- **THEN** the listing reports it at the top level with its type
- **AND** it does not omit the artifact

#### Scenario: List an empty library
- **WHEN** an operator lists models with no artifact installed
- **THEN** the command reports an empty list and succeeds

#### Scenario: List models as JSON
- **WHEN** an operator requests JSON output
- **THEN** the output is machine-readable and reports the same installed models and their nesting

#### Scenario: List an unreconciled model
- **WHEN** the configured models directory contains a `.gguf` without a valid Coquena sidecar
- **THEN** the listing reports it distinctly from an installed model, with its path-derived repository and filename
- **AND** the command issues no network request

### Requirement: Reconcile a model without a valid sidecar
Coquena SHALL provide `coquena model reconcile <path> --online` and `coquena model reconcile <path> --accept`, exactly one of which SHALL be required per invocation. `--online` SHALL match the file against Hugging Face using its path-derived repository, compare a locally computed SHA-256 digest against the matched artifact's reported content hash when Hugging Face provides one, and on agreement persist a full Hugging Face-provenance sidecar. `--accept` SHALL persist a `local`-provider sidecar from path-derived identity alone, without any network request. Both modes SHALL persist a computed SHA-256 digest and a classification digest read from the artifact's own header. Coquena SHALL additionally accept `--all` in place of a path, reconciling every unreconciled `.gguf` beneath the configured models directory in one pass that reads each header, classifies each artifact, and computes the companion relations among them. Reconciliation SHALL remain an explicit operator action and SHALL NOT run as a side effect of any listing command.

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

#### Scenario: Reconcile a whole directory in one pass
- **WHEN** an operator reconciles with `--all` over a directory holding several unreconciled artifacts
- **THEN** Coquena classifies every artifact and persists a sidecar for each reconciled one
- **AND** it reports the companion relations it computed among them

#### Scenario: Continue a bulk reconciliation past one failure
- **WHEN** one artifact cannot be reconciled during an `--all` pass
- **THEN** Coquena reports that artifact's diagnostic and reconciles the remaining ones
- **AND** it does not abandon the pass

#### Scenario: Reject reconciling with neither or both modes
- **WHEN** an operator invokes `reconcile` without `--online`/`--accept` or with both
- **THEN** Coquena reports a usage error
- **AND** it writes no sidecar

## ADDED Requirements

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
