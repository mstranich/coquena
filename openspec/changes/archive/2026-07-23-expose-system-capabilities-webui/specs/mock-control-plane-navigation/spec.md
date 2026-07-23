## ADDED Requirements

### Requirement: Present detected hardware in operational workspaces
The Dashboard and Runtimes workspaces SHALL each include the reusable detected-hardware view sourced from the local system-capability API. The Runtimes workspace SHALL NOT present its mock Windows/CUDA/GPU line as detected hardware.

#### Scenario: Compare hardware across operational workspaces
- **WHEN** a user navigates between Dashboard and Runtimes during a browser session with an available system report
- **THEN** each workspace presents the same detected capability information
- **AND** the view reflects the API report rather than page-specific mock hardware
