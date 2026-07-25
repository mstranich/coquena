# Model directory configuration

## Purpose

Define the persisted location used for model artifacts across Coquena's CLI and WebUI.

## Requirements

### Requirement: Resolve and persist an effective models directory
Coquena SHALL resolve an effective models directory after portable initialization. When no explicit models-directory override is persisted, the effective directory SHALL be `models/` beneath the resolved Coquena application home. A persisted override SHALL be an existing absolute writable directory; Coquena SHALL reject an empty, relative, nonexistent, non-directory, or non-writable path without changing the current configuration.

#### Scenario: Resolve the portable default
- **WHEN** an initialized installation has no explicit models-directory override
- **THEN** Coquena reports `<resolved-application-home>/models` as the effective directory
- **AND** that directory exists after initialization

#### Scenario: Persist a valid explicit directory
- **WHEN** an operator supplies an existing absolute writable directory
- **THEN** Coquena atomically persists it as the models-directory override
- **AND** later CLI and WebUI reads return that directory as the effective value

#### Scenario: Reject an unusable configured directory
- **WHEN** an operator supplies an empty, relative, nonexistent, non-directory, or non-writable path
- **THEN** Coquena reports a path-specific validation error
- **AND** it preserves the prior configuration and effective directory

### Requirement: Configure the models directory through the CLI
Coquena SHALL expose `coquena config models-directory [<absolute-path>] [--json]` after initialization. Without a path it SHALL display the effective models directory; with exactly one path it SHALL validate and persist the override through the shared configuration behavior. JSON output SHALL use a schema-version-1 document.

#### Scenario: Inspect the directory through the CLI
- **WHEN** an operator runs `coquena config models-directory` after initialization
- **THEN** the command exits successfully and reports the effective models directory

#### Scenario: Update the directory through the CLI
- **WHEN** an operator runs `coquena config models-directory <existing-absolute-writable-path>`
- **THEN** the command exits successfully and reports the saved effective directory
- **AND** a later read returns that path

#### Scenario: Use the command before initialization
- **WHEN** an operator runs the models-directory command before `coquena init`
- **THEN** the command exits unsuccessfully with guidance to run `coquena init`
- **AND** it creates no configuration or models directory

### Requirement: Configure the models directory through the local HTTP API
The local WebUI server SHALL expose `GET /api/config/models-directory` to return the effective models directory and `PUT /api/config/models-directory` to update it from a JSON body containing exactly `path`. Successful responses SHALL be schema-version-1 JSON documents. Invalid input and known initialization or path validation failures SHALL be non-success JSON API responses and SHALL NOT fall back to the Angular application document.

#### Scenario: Read the effective directory through HTTP
- **WHEN** a browser requests `GET /api/config/models-directory` after initialization
- **THEN** it receives status `200` and a schema-version-1 JSON document containing the effective directory

#### Scenario: Update the directory through HTTP
- **WHEN** a browser sends `PUT /api/config/models-directory` with exactly one valid absolute writable `path`
- **THEN** it receives a successful schema-version-1 JSON document containing the saved effective directory
- **AND** a later GET returns that path

#### Scenario: Reject malformed or unusable HTTP input
- **WHEN** a browser sends an unsupported method, malformed JSON, an unknown property, or an invalid path
- **THEN** the server returns `405` or a non-success JSON API response as appropriate
- **AND** it changes no configuration or directory content

### Requirement: Present an API-backed models-directory setting
The Angular Settings workspace SHALL load and render the effective directory from the local HTTP API, permit manual absolute-path entry, and save only after the update API succeeds. It SHALL render API errors visibly without replacing the last successfully loaded value.

#### Scenario: Save a manually entered valid directory
- **WHEN** an operator enters a valid absolute writable directory and selects Save
- **THEN** the workspace updates the setting through the HTTP API
- **AND** renders the saved effective directory and success state

#### Scenario: Render a validation failure
- **WHEN** the update API rejects the entered path
- **THEN** the workspace displays the returned safe error
- **AND** it does not claim the setting was saved
