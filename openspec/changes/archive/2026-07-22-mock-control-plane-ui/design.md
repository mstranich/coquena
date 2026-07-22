## Context

The Angular UI currently has only two top-level routes: `/fields` and `/presets`. The Fields screen is a runtime configuration-field catalog; the Presets screen is a launch-configuration editor backed by the same browser-memory store. The requested experience introduces the surrounding control-plane vocabulary—runtime, downloaded model artifact, executable model preset, global configuration, and runtime metrics—without yet introducing APIs or persistence.

## Goals / Non-Goals

**Goals:**
- Validate navigation and information hierarchy with representative mock data.
- Reuse the Fields screen as Settings-owned runtime field-catalog configuration.
- Reuse the Presets screen from Models as the editor for the global default preset and future model-specific overrides.
- Make preset inheritance and model/runtime state legible before live integrations exist.

**Non-Goals:**
- Downloading models or runtimes, starting processes, changing versions, reading hardware, collecting metrics, or persisting settings.
- Defining backend APIs or a durable model/preset data model.

## Decisions

- Use exactly four Angular client-side top-level routes—Dashboard, Models, Runtimes, and Settings—and redirect the root to Dashboard. This validates direct navigation and keeps each view independently testable; a single conditional page would not validate the intended application structure.
- Keep mock screen data local to UI components. A mock HTTP layer would imitate an API contract that this change explicitly defers.
- Keep the current `FieldCatalogStore` unchanged. Nest or link the Fields editor from Settings and nest or link the Presets editor from Models. This preserves validated editor behavior while putting each existing screen in the semantic owner specified by the proposal.
- Show both overridden and inherited values in model preset cards, with inherited values visually muted and a route/action to global configuration. This directly tests the source-of-value affordance.

## Risks / Trade-offs

- [Mock data may drift from future backend models] → Keep mocks small, visibly non-operational, and avoid public API contracts.
- [Static actions can be mistaken for working controls] → Label the workspace as mock/browser-memory and limit interactions to existing catalog behavior.
- [New shell can obscure existing editor routes] → Retain direct routes, but scope catalog URLs under Settings and preset URLs under Models.
