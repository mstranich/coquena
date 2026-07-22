## Why

The current UI exposes the field catalog and presets in isolation, without a coherent way to evaluate the model, runtime, and operational workflow that Coquena will provide. A mock-only navigation slice is needed now to validate the control-plane experience before coupling it to downloads, process control, or persistence.

## What Changes

- Replace the current Fields/Presets-only shell with exactly four top-level Angular navigation destinations: Dashboard, Models, Runtimes, and Settings.
- Present mock runtime health, supported capabilities, detected hardware, model states, and llama.cpp metric summaries.
- Define a local-model view with a global preset, per-model presets, and clear inherited versus overridden values.
- Place the existing Fields editor in Settings as the runtime field-catalog configuration.
- Place the existing Presets editor in Models, where it configures the global default preset and per-model preset overrides.
- Keep all controls and data non-persistent and non-operational in this change.

## Capabilities

### New Capabilities
- `mock-control-plane-navigation`: Browser-only navigation and mock views for operating local runtimes and models.
- `model-preset-inheritance-ui`: Visual representation of global and model-specific preset inheritance.

### Modified Capabilities
- `llamacpp-configuration-catalog-ui`: Move the existing Fields workspace under Settings and the existing Presets workspace under Models.

## Impact

- Affects Angular routes, application shell, mock view components, and frontend tests.
- Does not add backend APIs, disk persistence, runtime installation, Hugging Face integration, or live metrics collection.
