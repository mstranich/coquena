## Purpose

Define the browser-only presentation of global and model-specific executable presets.

## Requirements

### Requirement: Explain preset inheritance
The Models workspace SHALL distinguish global preset values inherited by a model-specific preset from values overridden for that model-specific preset.

#### Scenario: Inspect a preset value source
- **WHEN** a user views a displayed model preset
- **THEN** overridden values are labeled as overridden and inherited values are visibly muted and labeled with their global source

### Requirement: Edit presets from Models
The Models workspace SHALL expose the existing Presets editor as the configuration workflow for the global default preset and future model-specific presets. Settings SHALL NOT own preset editing.

#### Scenario: Configure the global default preset
- **WHEN** a user activates the global preset configuration affordance from Models
- **THEN** the UI opens the existing Presets editor in Models context

#### Scenario: Configure a model preset
- **WHEN** a user activates Configure for a displayed model preset
- **THEN** the UI opens the existing Presets editor in that model's context
