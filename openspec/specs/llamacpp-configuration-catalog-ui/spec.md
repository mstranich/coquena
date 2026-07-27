## Purpose

Define Coquena's browser-only UI for a typed llama.cpp configuration field catalog and reusable presets.
## Requirements
### Requirement: Navigate configuration catalog workspaces
The Angular Web UI SHALL present Settings as the global Coquena configuration destination and SHALL expose the Fields workspace from Settings. The Models workspace SHALL expose the Presets workspace because presets are executable configurations for a global default or particular model. Each workspace SHALL be addressable by its own client-side route.

#### Scenario: Open the fields workspace from Settings
- **WHEN** a user selects the preset-fields entry point in Settings
- **THEN** the UI displays the field catalog workspace without losing the in-memory application state

#### Scenario: Open the presets workspace from Models
- **WHEN** a user selects a global or model-specific preset configuration affordance in Models
- **THEN** the UI displays the preset workspace within the Models context

### Requirement: Provide compact keyboard-accessible catalog controls
The field catalog workspace SHALL use semantic controls and compact custom styling, with Angular CDK used for keyboard-managed interactions where required. The UI SHALL NOT require Angular Material or a remote icon font.

#### Scenario: Create a field by kind
- **WHEN** a user activates one of the five compact add-field controls for string, integer, boolean, void, or enum at the bottom of the catalog
- **THEN** the UI opens a new editor preselected to that kind and moves keyboard focus to the first editable field

#### Scenario: Filter catalog fields by name
- **WHEN** a user enters text in the catalog name filter
- **THEN** the catalog displays only fields whose argument name matches the filter

#### Scenario: Remove a catalog field from its row
- **WHEN** a user activates a field's accessible delete control and the field is not referenced
- **THEN** the UI removes that field from the catalog

### Requirement: Enable child authoring on confirmed fields
A confirmed field SHALL expose a child-authoring toggle; draft fields SHALL NOT expose it. When enabled, the field SHALL show its existing child fields recursively and then all child type controls below those children.

#### Scenario: Enable child authoring
- **WHEN** a user enables the child-authoring toggle on a confirmed field
- **THEN** the field shows its child nodes followed by String, Integer, Boolean, Flag, and Enum creation controls

#### Scenario: Nest a child field
- **WHEN** a child draft is added to a parent
- **THEN** the child is rendered beneath that parent and above the parent's child creation controls

#### Scenario: Allow nested descendants
- **WHEN** a confirmed child field enables child authoring
- **THEN** that child renders its own nested children region using the same interaction

#### Scenario: Keep drafts focused on completion
- **WHEN** a field is still a draft
- **THEN** no child-authoring toggle or child-type controls are shown

#### Scenario: Disable child authoring with children
- **WHEN** a user attempts to disable child authoring while child fields exist
- **THEN** the UI rejects the change and keeps child authoring enabled

#### Scenario: Save an existing field
- **WHEN** a user changes an existing field's metadata and activates that card's update control
- **THEN** the catalog stores the changes without changing the field kind

#### Scenario: Reject an invalid INI key name
- **WHEN** a user attempts to save a field whose name contains `--`, whitespace, or `=`
- **THEN** the UI rejects the save because catalog names are bare INI keys

#### Scenario: Reject a duplicate argument name
- **WHEN** a user attempts to save a field whose argument name already belongs to another catalog field
- **THEN** the UI rejects the save and identifies the duplicate-name problem

#### Scenario: Configure an enumerative field
- **WHEN** a user selects the enum kind and supplies a non-empty set of distinct options
- **THEN** the catalog saves those options as the allowed values for the field

#### Scenario: Add an enum option incrementally
- **WHEN** a user enters one option in the enum option input and activates its add control
- **THEN** the option appears in the field editor as an individual option with an accessible delete control

#### Scenario: Remove an enum option
- **WHEN** a user activates the delete control for an enum option
- **THEN** the UI removes only that option and continues to enforce the non-empty distinct option requirement when saving

#### Scenario: Reject an enum without usable options
- **WHEN** a user attempts to save an enum field with no options or duplicate options
- **THEN** the UI rejects the save and explains the invalid option list

#### Scenario: Configure a boolean false argument
- **WHEN** a user defines a boolean field with an optional argument name for false
- **THEN** the catalog retains that false-argument metadata with the field

#### Scenario: Represent a void field
- **WHEN** a user selects the void kind
- **THEN** the field editor does not require or retain a value or default value

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

### Requirement: Enforce parent activation prerequisites
The catalog SHALL allow a child field to be created from a parent field and declare one parent-field prerequisite plus the parent state required to make the child meaningful. The UI SHALL present dependent fields as children of their parent and SHALL only permit a preset to add a child after its prerequisite is satisfied.

#### Scenario: Enable a dependent field with a void parent
- **WHEN** a preset contains a void parent field marked present
- **THEN** the preset displays that parent's eligible child fields as available chips

#### Scenario: Add a child from its parent
- **WHEN** a user activates one of the child type buttons below a parent's existing children
- **THEN** the catalog inserts an empty child draft of that type nested under the parent

#### Scenario: Hide an inactive dependent field
- **WHEN** a field's parent prerequisite is not satisfied in a preset
- **THEN** the preset does not display that child field among its available chips

#### Scenario: Protect configured children when changing a parent
- **WHEN** a user attempts to remove or alter a parent entry so that configured child entries would no longer satisfy their prerequisite
- **THEN** the UI blocks the change and identifies the dependent entries

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

