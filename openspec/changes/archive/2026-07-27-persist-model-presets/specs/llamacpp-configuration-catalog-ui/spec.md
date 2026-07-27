## RENAMED Requirements
- FROM: `### Requirement: Maintain in-memory presets from catalog fields`
- TO: `### Requirement: Maintain API-backed presets from catalog fields`

## MODIFIED Requirements

### Requirement: Maintain API-backed presets from catalog fields
The UI SHALL let a user create, list, edit, and delete named presets sourced from the local preset API. A preset entry SHALL reference a catalog field and SHALL NOT declare a field kind. The UI SHALL NOT hold presets as the authoritative state and SHALL NOT claim a preset change was stored before the API confirms it.

#### Scenario: Add a field to a preset
- **WHEN** a user adds an available catalog field to a preset
- **THEN** the preset stores a reference to that field through the preset API and presents a value control derived from the field kind

#### Scenario: Show available fields at a glance
- **WHEN** a user views a preset
- **THEN** every currently eligible and unused catalog field is visible as a chip-like button without opening a menu

#### Scenario: Select an available field chip
- **WHEN** a user activates an available field chip
- **THEN** the field is added to the preset and removed from the available chip set

#### Scenario: Prevent duplicate fields in a preset
- **WHEN** a user attempts to add a catalog field already used by the same preset
- **THEN** the UI prevents a duplicate entry for that field

#### Scenario: Delete a preset
- **WHEN** a user deletes a preset
- **THEN** the preset is removed through the preset API and its field references no longer prevent catalog-field deletion

#### Scenario: Survive a reload
- **WHEN** a user creates a preset and reloads the application
- **THEN** the presets workspace renders that preset from the preset API

#### Scenario: Render a rejected preset change
- **WHEN** the preset API rejects a submitted preset change
- **THEN** the workspace displays the returned safe error
- **AND** it does not claim the change was stored

### Requirement: Edit preset values according to field kind
The preset editor SHALL derive the value editor and validation from the selected field definition, and SHALL store each edited value through the preset API.

#### Scenario: Enter an integer value
- **WHEN** a user adds an integer field to a preset
- **THEN** the editor accepts only integer values for that entry

#### Scenario: Select an enumerative value
- **WHEN** a user adds an enum field to a preset
- **THEN** the editor presents only that field's catalog options and rejects values outside them

#### Scenario: Configure a boolean value
- **WHEN** a user adds a boolean field to a preset
- **THEN** the editor presents a boolean control and stores a boolean value without asking for the field kind

#### Scenario: Persist a preset value
- **WHEN** a user changes the value control for a preset entry
- **THEN** the updated concrete value is stored through the preset API
- **AND** it is still reported after a reload

#### Scenario: Identify a preset value control
- **WHEN** a preset contains a string or integer field
- **THEN** its value input has an accessible field-specific label and a unique form name

#### Scenario: Include a void flag
- **WHEN** a user adds a void field to a preset
- **THEN** the editor represents whether the flag is present and does not request a scalar value

## REMOVED Requirements

### Requirement: Keep presets browser-memory only
**Reason**: Presets are now persisted and served by the local preset API, so nothing in this slice is browser-memory only and the constraint no longer describes any behavior.
**Migration**: The behavior this requirement protected — a preset surviving client-side navigation — is now covered by durable persistence and by the reload scenarios of the API-backed preset requirements.
