## MODIFIED Requirements

### Requirement: Validate and publish only complete models
Coquena SHALL retry transient retrieval failures within a bounded policy, verify known byte size and local GGUF metadata before completion, and atomically publish the GGUF and its sidecar as one logical model unit. Publication SHALL derive the artifact's classification digest from the completed file's own header and persist it in the sidecar, without retaining the raw metadata map. A failed retry, validation, or publication SHALL preserve prior completed models and SHALL NOT expose the partial as a completed model.

#### Scenario: Complete a validated download
- **WHEN** all expected bytes are retrieved and local GGUF inspection succeeds
- **THEN** Coquena atomically places the GGUF and sidecar in the model library
- **AND** the published sidecar carries the artifact's classification digest
- **AND** removes the adjacent download record

#### Scenario: Reject an incomplete artifact
- **WHEN** the retrieved byte count differs from known remote size or local inspection fails
- **THEN** Coquena marks the job failed
- **AND** it leaves no new published model or sidecar

### Requirement: Create durable resumable GGUF download jobs
Coquena SHALL create a durable job beside its configured model target as `<model>.gguf.download.json` with content in `<model>.gguf.partial`. The persisted job SHALL record source identity, partial and target paths, downloaded and expected size, available ETag/Last-Modified values, and status. It SHALL support `pending`, `downloading`, `paused`, `completed`, `failed`, and `cancelled` states. A request covering a principal artifact and its companions SHALL create one independent job per artifact rather than a single composite job.

#### Scenario: Start a model download
- **WHEN** an operator requests download of one resolved GGUF artifact
- **THEN** Coquena creates one job and reports its opaque ID and initial state
- **AND** it writes only partial content until validation and publication succeed

#### Scenario: Create independent jobs for a companion set
- **WHEN** a request covers a principal artifact and one companion
- **THEN** Coquena creates one durable job per artifact
- **AND** cancelling one job leaves the other's state and partial content untouched

#### Scenario: Recover an interrupted transfer
- **WHEN** Coquena encounters a persisted job that was downloading when the process stopped
- **THEN** it exposes the job as paused with its existing partial bytes
- **AND** it does not resume network activity automatically

#### Scenario: Discover an incomplete model after a cold restart
- **WHEN** Coquena scans the configured models directory after restart
- **THEN** it discovers adjacent `.gguf.download.json` and `.gguf.partial` files as an incomplete model entry
- **AND** exposes the entry for later resume without depending on `<home>/downloads`
