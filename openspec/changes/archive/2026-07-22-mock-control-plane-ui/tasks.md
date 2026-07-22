## 1. Control-plane shell and navigation

- [x] 1.1 Add Dashboard, Models, Runtimes, and Settings routes and replace the current top-level Fields/Presets navigation with exactly those four destinations.
- [x] 1.2 Place the existing Fields editor under Settings and the existing Presets editor under Models without losing their shared browser-memory state.
- [x] 1.3 Add route and shell tests for the new default Dashboard and top-level destinations.

## 2. Mock operational workspaces

- [x] 2.1 Implement a Dashboard mock view with runtime state, model summaries, request activity, and prompt/generation metric cards.
- [x] 2.2 Implement a Runtimes mock view with default runtime selection, hardware summary, version state, and capabilities.
- [x] 2.3 Implement Models Local and Discover mock sections with model states, quantization details, download affordances, and non-operational preset actions.

## 3. Settings and preset inheritance

- [x] 3.1 Implement Settings General with a mock models-directory control and entry point to the Fields editor.
- [x] 3.2 Display global preset configuration and distinguish overridden from inherited values in the model preset UI, with global and per-model Configure actions opening the Presets editor in Models context.
- [x] 3.3 Add component tests for the mock information hierarchy and inheritance labels.

## 4. Verification

- [x] 4.1 Run the frontend test suite and production build.
- [x] 4.2 Run `task verify` and confirm no backend, persistence, process-control, download, or live-metrics integration was introduced.
