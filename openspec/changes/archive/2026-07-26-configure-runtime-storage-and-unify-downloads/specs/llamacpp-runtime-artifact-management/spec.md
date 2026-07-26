## MODIFIED Requirements

### Requirement: Verified atomic publication
Runtime download SHALL execute as a durable managed job rooted in the effective
configured runtimes directory.  It SHALL require a supported SHA-256 `digest`
from official GitHub metadata for every asset, stage bounded content beneath
that directory through adjacent `<asset>.partial` transfer files and `<asset>.download.json` job
sidecars, reject unsafe archives, require and probe `llama-server`, write
versioned metadata, and atomically publish only after every check succeeds. It
SHALL NOT use `<home>/downloads`.

#### Scenario: Composite download succeeds in a configured directory
- **WHEN** primary and dependency assets pass digest, extraction, file, and probe validation
- **THEN** Coquena publishes one complete runtime beneath the effective runtimes directory
- **AND** records the primary public ID and metadata for every contributing asset

#### Scenario: Large dependency transfer exceeds metadata request duration
- **WHEN** an official runtime archive or dependency requires longer than the bounded GitHub metadata request timeout to transfer
- **THEN** the archive transfer continues under the job's caller-controlled cancellation context
- **AND** it is not failed merely because the metadata timeout duration elapsed

#### Scenario: Installation is not initialized
- **WHEN** a runtime download job runs without a valid portable installation
- **THEN** it fails with initialization guidance
- **AND** writes no runtime elsewhere

### Requirement: Discover incomplete runtime downloads from runtime storage
Coquena SHALL discover incomplete, paused, downloading, and failed runtime jobs
by scanning the effective configured runtimes directory for valid runtime `.json`
sidecars and their adjacent `.partial` artifacts. It SHALL not depend on an
in-memory operation record or a central portable-home downloads directory.

#### Scenario: Restart finds a paused runtime transfer
- **WHEN** Coquena restarts after a runtime transfer leaves a valid sidecar and partial file in the configured runtimes directory
- **THEN** runtime and global download management list the job with its durable state and progress
- **AND** resume operates from that discovered state

### Requirement: Present self-managing runtime entries in WebUI
The WebUI SHALL present installed and incomplete runtimes in one managed list
analogous to the model library. It SHALL NOT represent runtime work as a global
"Runtime Operation" card. Each runtime entry SHALL expose its current state,
known byte progress, verification/extraction/publication phase text, safe error,
and only the lifecycle actions applicable to that entry.

#### Scenario: Paused runtime is managed from its own entry
- **WHEN** the configured runtime directory contains a paused runtime job
- **THEN** the Runtimes page lists that job alongside installed runtimes
- **AND** its entry shows progress with Resume and Cancel actions

#### Scenario: No runtime downloads are incomplete
- **WHEN** the runtime download catalog is empty
- **THEN** its API result is an empty JSON array rather than null
- **AND** the WebUI continues rendering installed and remotely available runtimes

#### Scenario: Composite runtime progress is shown without changing meaning
- **WHEN** a runtime contains a primary archive and one or more dependency archives
- **THEN** the WebUI labels current-asset progress separately from overall runtime progress
- **AND** pausing does not relabel persisted asset bytes as aggregate runtime bytes

### Requirement: Concurrent mutation safety
Runtime download publication and removal SHALL use an effective-runtimes-directory-scoped
exclusive fail-fast mutation lock while read-only listing remains lock-free.

#### Scenario: Another mutation owns the effective-directory lock
- **WHEN** a runtime mutation starts while another holds the lock for the same effective runtimes directory
- **THEN** Coquena returns a busy diagnostic and makes no partial mutation
