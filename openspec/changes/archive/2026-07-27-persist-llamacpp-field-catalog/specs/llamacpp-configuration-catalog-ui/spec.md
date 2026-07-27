## RENAMED Requirements
- FROM: `### Requirement: Maintain an in-memory field catalog`
- TO: `### Requirement: Maintain an API-backed field catalog`
- FROM: `### Requirement: Keep this slice browser-memory only`
- TO: `### Requirement: Keep presets browser-memory only`

## MODIFIED Requirements

### Requirement: Maintain an API-backed field catalog
The UI SHALL let a user create, list, edit, and delete field definitions sourced from the local field-catalog API. A definition SHALL have a unique INI key name without a leading `--` and exactly one immutable kind: string, integer, boolean, void, or enum. Each field SHALL expose its own save/update and delete controls; the workspace SHALL NOT require a separate global field-save form.

#### Scenario: Create a string field
- **WHEN** a user activates the String type control
- **THEN** the catalog inserts an empty string-kind draft, focuses its argument-name input, and does not offer a kind-change control

#### Scenario: Finish a field draft
- **WHEN** a user enters a valid unique argument name in a draft and activates that card's save control
- **THEN** the UI persists the field through the catalog API
- **AND** the field becomes available to preset editing after the API confirms the update

#### Scenario: Survive a reload
- **WHEN** a user saves a field and reloads the application
- **THEN** the Fields workspace renders that field from the catalog API

#### Scenario: Render a rejected save
- **WHEN** the catalog API rejects a submitted field
- **THEN** the workspace displays the returned safe error
- **AND** it does not claim the field was saved

### Requirement: Keep presets browser-memory only
Presets SHALL be shared across client-side route navigation during the active application session and SHALL not be persisted, sent to a backend, discovered from a binary, or used to execute a command. The field catalog is excluded from this constraint and is served by the local catalog API.

#### Scenario: Navigate between workspaces
- **WHEN** a user creates a preset and then navigates to fields
- **THEN** the new preset remains available during that application session

#### Scenario: No backend interaction
- **WHEN** a user creates or edits a preset
- **THEN** the UI completes the operation without issuing a backend preset-persistence or command-execution request

### Requirement: Keep field references consistent
The UI SHALL remove a selected field, its descendant subtree, and every preset entry that references any removed field. The UI SHALL prevent changing the immutable kind of a field.

#### Scenario: Remove a referenced field
- **WHEN** a user removes a field used by one or more presets
- **THEN** the UI removes the field and its entries from those presets

#### Scenario: Remove a field subtree
- **WHEN** a user removes a field that has descendants
- **THEN** the UI removes that field, every descendant, and all corresponding preset entries

#### Scenario: Block kind change of a referenced field
- **WHEN** a user attempts to change the kind of a field used by a preset
- **THEN** the UI rejects the update and retains the original kind
