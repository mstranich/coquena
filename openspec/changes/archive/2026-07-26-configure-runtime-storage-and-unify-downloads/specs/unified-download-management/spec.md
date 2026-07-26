## ADDED Requirements

### Requirement: Maintain durable typed download jobs
Coquena SHALL represent model and llama.cpp runtime downloads as durable jobs
with an opaque ID, resource kind, status, display identity, timestamps, exact
byte progress when known, and safe terminal error.  Nonterminal jobs SHALL
survive process restart and interrupted work SHALL become resumable or safely
failed rather than being reported as completed. Shared durable-job behavior for
adjacent `<asset>.partial` transfer bytes, `<asset>.download.json` sidecars, recovery, progress, and
state transitions SHALL be reused between models and runtimes where their
semantics are identical.

#### Scenario: Restart during a runtime download
- **WHEN** Coquena is interrupted while a runtime job is downloading
- **THEN** a later invocation lists the same job as resumable or failed with a safe diagnostic
- **AND** it exposes no partially published runtime as installed

### Requirement: Provide global and resource-filtered download CLI views
`coquena download list|status|pause|resume|cancel` SHALL manage every supported
download kind without filtering. `list` and `status` without a job ID SHALL
return the same table view of all matching jobs, while `status <job-id>` MAY
inspect one exact matching job. `coquena model download list|status|pause|resume|cancel`
SHALL operate only on model jobs, and `coquena runtime download list|status|pause|resume|cancel`
SHALL operate only on runtime jobs. Human list/status output SHALL use columns
`ID`, `TYPE`, `NAME`, `STATUS`, `PROGRESS`, and `PATH`; `TYPE` SHALL distinguish
the resource kind, `NAME` SHALL be resource-neutral, and `PROGRESS` SHALL be a
separate human-readable percentage plus IEC transferred/total sizes when known
or an em dash when the total is unknown. JSON SHALL retain exact
schema-versioned byte fields including the kind.

#### Scenario: List model and runtime jobs together
- **WHEN** model and runtime jobs exist
- **THEN** `coquena download list` returns entries for both kinds with unambiguous IDs and `TYPE` values
- **AND** each entry uses a resource-neutral `NAME` and a separate `PROGRESS` value

#### Scenario: Show all download statuses without a job ID
- **WHEN** model and runtime jobs exist and an operator runs `coquena download status`
- **THEN** it returns the same unfiltered table view as `coquena download list`

#### Scenario: List only model jobs through the model namespace
- **WHEN** model and runtime jobs exist and an operator runs `coquena model download list` or `coquena model download status`
- **THEN** the result contains only model jobs

#### Scenario: List only runtime jobs through the runtime namespace
- **WHEN** model and runtime jobs exist and an operator runs `coquena runtime download list` or `coquena runtime download status`
- **THEN** the result contains only runtime jobs

#### Scenario: Act on a runtime job through the global CLI
- **WHEN** an operator pauses, resumes, cancels, or queries a runtime job ID through `coquena download`
- **THEN** Coquena applies the same lifecycle semantics as for a model job
- **AND** it does not require a separate runtime-specific management grammar

### Requirement: Preserve resource-specific download entry points
`coquena model download` and `coquena runtime download` SHALL create their
respective typed jobs and SHALL delegate management actions to the shared job
lifecycle with their resource-kind filter. They SHALL retain resource-specific
selection and validation before a job is created.

#### Scenario: Reject invalid runtime selection before job creation
- **WHEN** a runtime download selection is malformed, unsupported, ambiguous, or incompatible
- **THEN** Coquena creates no download job and performs no runtime filesystem mutation

### Requirement: Pause, resume, and cancel safely
Pausing or cancellation SHALL leave no published partial artifact.  Resume
SHALL continue only after its resource-specific integrity/provenance checks
pass; runtime resume SHALL verify the final official digest before extraction
and publication.

#### Scenario: Resume a runtime after a paused transfer
- **WHEN** an operator resumes a paused runtime job
- **THEN** Coquena safely continues or restarts its verified transfer
- **AND** it publishes the runtime only after all existing archive, probe, and atomic-publication checks pass
