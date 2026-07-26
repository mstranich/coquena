## ADDED Requirements

### Requirement: Resolve and persist an effective runtimes directory
Coquena SHALL resolve an effective runtimes directory after portable
initialization.  Without an override it SHALL be `<resolved-application-home>/runtimes`.
An override SHALL be an existing absolute writable directory; empty, relative,
nonexistent, non-directory, or non-writable values SHALL be rejected without
changing configuration. Runtime-owned installations, `.partial` transfer
files, adjacent `.json` job sidecars, temporary verified archives, and locks
SHALL remain below the effective directory. Coquena SHALL not use
`<home>/downloads` for runtime state.

#### Scenario: Persist a valid runtime directory
- **WHEN** an operator saves an existing absolute writable runtime directory
- **THEN** later CLI and WebUI reads return that effective directory
- **AND** subsequent runtime downloads keep their `<asset>.partial` and `<asset>.download.json` files below that configured directory

#### Scenario: Reject an unusable runtime directory
- **WHEN** an operator supplies an invalid runtime directory
- **THEN** Coquena returns a path-specific validation error
- **AND** preserves the prior effective directory and its configuration

### Requirement: Configure the runtimes directory through CLI and local API
Coquena SHALL expose `coquena config runtimes-directory [<absolute-path>] [--json]`,
`GET /api/config/runtimes-directory`, and `PUT /api/config/runtimes-directory`
with a JSON body containing exactly `path`.  Successful machine responses SHALL
be schema-version-1 documents; invalid input and initialization failures SHALL
be non-success responses and SHALL not change configuration.

#### Scenario: Read and update runtime directory from each interface
- **WHEN** an initialized operator reads or saves a valid runtime directory through CLI or API
- **THEN** the interface returns the effective saved path
- **AND** a later read through the other interface returns the same path

### Requirement: Present an API-backed runtimes-directory setting
The Angular Settings workspace SHALL load, render, and independently save the
effective runtimes directory next to the models directory, visibly rendering
API errors and only reporting success after the API accepts the update.

#### Scenario: Runtime directory validation fails in Settings
- **WHEN** the update API rejects the entered runtime path
- **THEN** Settings displays the returned safe error
- **AND** retains the last successfully loaded runtime directory
