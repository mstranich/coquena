## 1. Shared catalog state

- [x] 1.1 Define the Angular field-definition, prerequisite, preset, and preset-entry models, including kind-specific metadata and browser-local ids.
- [x] 1.2 Implement an injectable Signals-based `FieldCatalogStore` with read-only catalog/preset state and CRUD mutation methods.
- [x] 1.3 Validate unique argument names, enum option lists, parent prerequisites, preset field references, typed values, and duplicate field entries within a preset.
- [x] 1.4 Prevent deletion and kind changes for fields referenced by presets, and prevent parent changes that invalidate configured children.
- [x] 1.5 Add unit tests for store CRUD, defaults, typed validation, prerequisite availability, and reference-integrity behavior.

## 2. Application navigation

- [x] 2.1 Add Angular CDK and a small local SVG icon set without adding Angular Material or a remote icon font.
- [x] 2.2 Add routed Fields and Presets workspaces to the Angular application.
- [x] 2.3 Replace the placeholder application shell with persistent navigation and a routed content area using compact custom SCSS.
- [x] 2.4 Add route and shell tests proving both workspaces render through client-side navigation.

## 3. Field catalog workspace

- [x] 3.1 Implement an empty and populated catalog tree with a keyboard-focused name filter, compact add controls for all five kinds, and per-field delete entry points.
- [x] 3.2 Implement the field editor for unique argument names, parent prerequisites, and the string, integer, boolean, void, and enum kinds.
- [x] 3.3 Show kind-specific controls for defaults, incremental enum options, optional boolean false-argument metadata, and parent activation state; omit values for void fields.
- [x] 3.4 Surface invalid field definitions, prerequisite constraints, and referenced-field mutation blocks in the workspace.
- [x] 3.5 Add component tests for field creation shortcuts, filtering, enum option editing, void behavior, prerequisite rendering, and referenced-field protections.

## 6. UX correction

- [x] 6.1 Replace the global field editor with immutable-kind, per-card drafts and save/update/delete controls.
- [x] 6.2 Focus each new draft's argument-name input and create child drafts from a parent card action.
- [x] 6.3 Render the catalog recursively with each parent's child controls after its existing child nodes.

## 4. Preset workspace

- [x] 4.1 Implement an empty and populated preset list with create, edit, and delete entry points.
- [x] 4.2 Display all available and prerequisite-eligible catalog fields as chip buttons, and remove selected fields from that set.
- [x] 4.3 Render typed preset value controls for string, integer, boolean, void, and enum fields, seeding catalog defaults when defined.
- [x] 4.4 Surface invalid preset values, prerequisite violations, and parent-removal blocks; remove field references when an entry or its preset is deleted.
- [x] 4.5 Add component tests for cross-route shared state, typed value controls, menu keyboard interaction, prerequisite availability, duplicate prevention, and preset deletion.

## 5. Verify the browser-only slice

- [x] 5.1 Run the Angular test suite and production build.
- [x] 5.2 Manually verify creating a field, navigating to presets, configuring a preset, and observing the in-memory-only scope.
