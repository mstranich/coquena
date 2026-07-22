## ADDED Requirements

### Requirement: Navigate the four control-plane workspaces
The Angular Web UI SHALL expose exactly Dashboard, Models, Runtimes, and Settings as top-level navigation items. The default route SHALL open Dashboard; Fields and Presets SHALL NOT appear as top-level navigation items.

#### Scenario: Open a top-level workspace
- **WHEN** a user selects Dashboard, Models, Runtimes, or Settings in the application navigation
- **THEN** the corresponding mock workspace is displayed in the shared Coquena shell

#### Scenario: Load the default workspace
- **WHEN** a user opens the application root route
- **THEN** the Dashboard workspace is displayed

### Requirement: Present mock operational status
The Dashboard SHALL show mock runtime state, installed runtime version, mode, metrics availability, active and queued request counts, prompt throughput, generation throughput, and available model summaries.

#### Scenario: Inspect runtime performance
- **WHEN** a user opens Dashboard
- **THEN** prompt and generation throughput are labeled as runtime metrics alongside request activity

### Requirement: Present mock runtime inventory
The Runtimes workspace SHALL display a selectable default runtime, detected hardware summary, installed and latest versions, platform, active status, and declared capabilities.

#### Scenario: Inspect active runtime capabilities
- **WHEN** a user opens Runtimes
- **THEN** the active llama.cpp runtime and its router, metrics, multimodal, model-preset, and CUDA capabilities are visible

### Requirement: Present mock model library and discovery
The Models workspace SHALL display local model artifacts with status and presets, a global preset entry point, and a mock Hugging Face discovery result with quantization, size, hardware fit, and download affordance.

#### Scenario: Inspect a local model
- **WHEN** a user opens Models
- **THEN** each displayed local model shows its quantization, size, availability state, and named presets with Run and Configure affordances

#### Scenario: Inspect a discoverable model
- **WHEN** a user views the discovery section
- **THEN** a model result shows a quantization, size, recommendation, hardware-fit indicator, and download affordance
