## ADDED Requirements

### Requirement: Create durable resumable GGUF download jobs
Coquena SHALL create a durable job beside its configured model target as `<model>.gguf.download.json` with content in `<model>.gguf.partial`. The persisted job SHALL record source identity, partial and target paths, downloaded and expected size, available ETag/Last-Modified values, and status. It SHALL support `pending`, `downloading`, `paused`, `completed`, `failed`, and `cancelled` states.

#### Scenario: Start a model download
- **WHEN** an operator requests download of one resolved GGUF artifact
- **THEN** Coquena creates one job and reports its opaque ID and initial state
- **AND** it writes only partial content until validation and publication succeed

#### Scenario: Recover an interrupted transfer
- **WHEN** Coquena encounters a persisted job that was downloading when the process stopped
- **THEN** it exposes the job as paused with its existing partial bytes
- **AND** it does not resume network activity automatically

#### Scenario: Discover an incomplete model after a cold restart
- **WHEN** Coquena scans the configured models directory after restart
- **THEN** it discovers adjacent `.gguf.download.json` and `.gguf.partial` files as an incomplete model entry
- **AND** exposes the entry for later resume without depending on `<home>/downloads`

### Requirement: Pause resume and cancel safely
Coquena SHALL pause by stopping the active transfer while retaining a resumable partial and persisted state. Resume SHALL reconcile safely when the partial is ahead of the latest persisted checkpoint and use HTTP Range with the recorded resource validator when available. Cancel SHALL stop active transfer, remove the adjacent partial and download record, and never remove a published model.

#### Scenario: Resume an unchanged partial download
- **WHEN** an operator resumes a paused job whose remote resource identity remains valid
- **THEN** Coquena requests bytes beginning at the saved partial length
- **AND** continues progress from the saved byte count

#### Scenario: Prevent unsafe resume after source changes
- **WHEN** the remote validator changed, Range is unavailable, or the partial is smaller than its persisted checkpoint
- **THEN** Coquena marks the job failed with a safe diagnostic
- **AND** it does not append incompatible bytes or publish a model

### Requirement: Validate and publish only complete models
Coquena SHALL retry transient retrieval failures within a bounded policy, verify known byte size and local GGUF metadata before completion, and atomically publish the GGUF and its sidecar as one logical model unit. A failed retry, validation, or publication SHALL preserve prior completed models and SHALL NOT expose the partial as a completed model.

#### Scenario: Complete a validated download
- **WHEN** all expected bytes are retrieved and local GGUF inspection succeeds
- **THEN** Coquena atomically places the GGUF and sidecar in the model library
- **AND** removes the adjacent download record

#### Scenario: Reject an incomplete artifact
- **WHEN** the retrieved byte count differs from known remote size or local inspection fails
- **THEN** Coquena marks the job failed
- **AND** it leaves no new published model or sidecar
### Requirement: Retain revision-pinned download identity
Coquena SHALL persist each job's requested revision, resolved commit SHA, and SHA-pinned source URL before transfer begins. A resumed job SHALL use precisely its persisted resolved revision and URL, regardless of later changes to the requested branch.

#### Scenario: Resume after main changes
- **WHEN** an operator resumes a paused job after the repository's `main` branch changed
- **THEN** Coquena resumes against the job's original commit-pinned URL
- **AND** it does not mix bytes from the newer branch state
