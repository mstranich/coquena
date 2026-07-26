## Purpose

Define CLI and local WebUI interfaces for shared GGUF model management.

## Requirements

### Requirement: Expose shared model management through the CLI
Coquena SHALL expose `coquena model resolve <repository-or-url>`, `coquena model inspect <repository-or-url[:selector]>`, and `coquena model download <repository-or-url[:selector]>`, plus download management as `coquena model download list`, `status <job-id>`, `pause <job-id>`, `resume <job-id>`, and `cancel <job-id>`. The shorter top-level `coquena download ...` forms SHALL remain accepted aliases. These commands SHALL invoke the shared model service and SHALL support one `--json` option that emits exactly one schema-version-1 JSON document on standard output.

#### Scenario: Automate model inspection
- **WHEN** an operator invokes a supported model command with `--json`
- **THEN** standard output contains only a newline-terminated schema-version-1 JSON document
- **AND** operational diagnostics do not contaminate that document

#### Scenario: Reject invalid model command input
- **WHEN** an operator supplies malformed, ambiguous, unsupported, or missing command input
- **THEN** the CLI exits unsuccessfully with usage or a safe diagnostic
- **AND** it creates no job or model artifact

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
The local WebUI server SHALL expose schema-version-1 JSON APIs for resolving and inspecting GGUF artifacts, discovering installed and incomplete model units, lazily reading installed metadata, serving cached publisher avatars, creating a download job, listing jobs, inspecting a job, and pausing, resuming, or cancelling a job. Accepted creation SHALL return `202` with an opaque job ID; job progress and terminal state SHALL be available through polling and an SSE endpoint. Invalid API paths, input, and methods SHALL return API errors or `405`, never an Angular document.

#### Scenario: Create and observe a download job
- **WHEN** a browser posts one valid resolved GGUF selection to the download API
- **THEN** it receives `202` with one job ID
- **AND** it can obtain that job's current progress and terminal status through the API

#### Scenario: Reject a malformed API operation
- **WHEN** a browser sends malformed JSON, an unknown property, an unknown job ID, or an unsupported method
- **THEN** the server returns a non-success JSON response or `405`
- **AND** it does not change job or model state

### Requirement: Present API-backed GGUF management in Models
The Angular Models workspace SHALL resolve public Hugging Face GGUF candidates, present responsive metadata summaries for an explicit selected artifact before download, discover installed and incomplete model units, create and observe download jobs, and provide pause, resume, and cancel actions as permitted by job state. Large tokenizer arrays SHALL be summarized rather than rendered into the DOM; explicit API/CLI raw inspection MAY return the complete document. The workspace SHALL visibly show backend diagnostics and SHALL use GGUF metadata as read-only reference rather than modifying manual fields or presets.

The inspection view SHALL present metadata as a two-column `Metadata`/`Value` table rather than a raw JSON document and SHALL not render implementation-oriented omission notices.

Each discovered candidate SHALL own independent inspection/loading state and SHALL reveal its collapsible metadata table within that candidate's entry. Installed-model library responses SHALL omit full metadata; expanding an installed entry SHALL lazily load its validated JSON sidecar and render the same collapsible table.

Discovery entries SHOULD display the publisher avatar from Hugging Face when available. Incomplete and installed entries SHOULD display the namespace-level avatar cached beneath the configured models directory and SHALL NOT depend on a remote image after job creation.

#### Scenario: Download a selected variant from Models
- **WHEN** an operator selects one resolved GGUF variant and requests download
- **THEN** the workspace starts only that artifact's job and renders its progress and current state
- **AND** it refreshes the model/job view after a terminal outcome

#### Scenario: Observe multiple incomplete models
- **WHEN** the configured models tree contains multiple incomplete downloads or a new download starts
- **THEN** each model appears immediately as its own library entry with independent state and progress
- **AND** pause, resume, and cancel act directly on that entry without selecting a shared download-management panel

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
