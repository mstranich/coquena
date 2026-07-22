## MODIFIED Requirements

### Requirement: Navigate configuration catalog workspaces
The Angular Web UI SHALL present Settings as the global Coquena configuration destination and SHALL expose the Fields workspace from Settings. The Models workspace SHALL expose the Presets workspace because presets are executable configurations for a global default or particular model. Each workspace SHALL be addressable by its own client-side route.

#### Scenario: Open the fields workspace from Settings
- **WHEN** a user selects the preset-fields entry point in Settings
- **THEN** the UI displays the field catalog workspace without losing the in-memory application state

#### Scenario: Open the presets workspace from Models
- **WHEN** a user selects a global or model-specific preset configuration affordance in Models
- **THEN** the UI displays the preset workspace within the Models context
