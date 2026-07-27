## MODIFIED Requirements

### Requirement: Explain preset inheritance
The Models workspace SHALL distinguish global preset values inherited by a model-specific preset from values overridden for that model-specific preset, using the inheritance source reported by the preset API rather than deriving inheritance in the browser. The workspace SHALL offer returning an overridden value to its inherited state.

#### Scenario: Inspect a preset value source
- **WHEN** a user views a displayed model preset
- **THEN** values the API reports as owned are labeled as overridden and values it reports as inherited are visibly muted and labeled with their global source

#### Scenario: Return a value to inheritance
- **WHEN** a user activates the affordance to stop overriding a value
- **THEN** the UI removes the override through the preset API
- **AND** the value is redisplayed as inherited from global

### Requirement: Edit presets from Models
The Models workspace SHALL expose the existing Presets editor as the configuration workflow for the global default preset and model-specific presets. Settings SHALL NOT own preset editing. The workspace SHALL present a model preset's model binding and the runtimes that admit it.

#### Scenario: Configure the global default preset
- **WHEN** a user activates the global preset configuration affordance from Models
- **THEN** the UI opens the existing Presets editor in Models context

#### Scenario: Configure a model preset
- **WHEN** a user activates Configure for a displayed model preset
- **THEN** the UI opens the existing Presets editor in that model's context

#### Scenario: Present the model binding
- **WHEN** a user views a model preset
- **THEN** the UI presents the bound model reference
- **AND** it indicates when the API reports that model as not installed or stale

#### Scenario: Present the admitting runtimes
- **WHEN** a user views a model preset
- **THEN** the UI presents the declared runtime constraint and the constraint computed from the preset's fields
- **AND** it attributes each computed bound to the field the API reports as its cause
