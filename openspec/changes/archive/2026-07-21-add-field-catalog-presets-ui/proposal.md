## Why

Coquena needs to expose llama.cpp configuration without hard-coding a flag set that becomes stale as upstream and custom builds evolve. Before adding persistence or process launching, the product needs a usable browser interaction for defining available fields and composing their values into reusable presets.

## What Changes

- Add a navigation shell for the Angular Web UI with routes for field catalogs and presets.
- Add an in-memory, application-level Angular state store shared by both routes.
- Use Angular CDK interaction primitives with semantic HTML, compact custom SCSS, and local SVG icons; do not add Angular Material.
- Let users create, edit, filter, list, and delete field definitions with string, integer, boolean, void, and enumerative kinds.
- Provide compact, keyboard-accessible field-type add controls, incremental enum-option editing, and parent prerequisite relationships between fields.
- Let users create, edit, list, and delete presets that select catalog fields and enter only values compatible with each selected field; a preset never selects a field type.
- Show every currently eligible, unused preset field as a visible chip-like button.
- Derive field-specific editors for string, integer, boolean, void, and enumerative values, including prerequisite-aware availability.
- Keep all data in browser memory for this slice; do not add Go APIs, disk persistence, llama.cpp binary inspection, command execution, or automatic field discovery.

## Capabilities

### New Capabilities
- `llamacpp-configuration-catalog-ui`: Browser UI for maintaining an in-memory catalog of llama.cpp launch fields and composing typed preset values from it.

### Modified Capabilities

None.

## Impact

- Affected code: `apps/web/src/app/` Angular routes, navigation shell, components, and shared state.
- Affected product surface: client-side routes served by the existing embedded Web UI.
- Dependencies: Angular router and signals already present in the project, plus Angular CDK for future interaction primitives; no full visual component library or external state-management package.
- No backend API, persisted configuration format, runtime catalog discovery, or llama.cpp invocation behavior is introduced.
