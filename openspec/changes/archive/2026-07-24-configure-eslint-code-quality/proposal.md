## Why

The frontend lacks an enforced, configurable code-quality baseline, so code can be compacted in ways that make Code Review needlessly difficult. Establishing a review-oriented lint workflow now prevents readability debt from accumulating before further feature work.

## What Changes

- Add an ESLint 10-based quality gate for the Angular/TypeScript workspace, using flat configuration and `@stylistic` rules for explicitly chosen, auto-fixable readability conventions.
- Add Angular-aware linting for TypeScript and templates, plus npm and Task commands for checking and applying safe fixes.
- Add dprint as the configurable formatter for TypeScript and Angular HTML templates, with separate check and explicit formatting commands.
- Make the full workspace verification fail when frontend lint checks fail, without modifying source files during verification.
- Remove the unused Prettier development dependency; Prettier is not part of the project workflow.

### Non-Goals

- Do not introduce a Rust- or Go-based replacement linter in this change.
- Do not configure pre-commit hooks, CI workflows, editor extensions, or automatic formatting on save.
- Do not refactor code unrelated to the initial lint findings beyond the minimum normalization required to establish the baseline.

## Capabilities

### New Capabilities
- `frontend-code-quality`: Configurable, review-oriented static analysis and style enforcement for the Angular frontend.

### Modified Capabilities

- None.

## Impact

- Affects `apps/web/package.json`, its lockfile, new frontend ESLint and dprint configurations, and `Taskfile.yml` verification tasks.
- Adds ESLint 10, TypeScript/Angular ESLint integrations, ESLint Stylistic, and dprint as frontend development dependencies; removes Prettier.
- Changes developer workflow by exposing explicit format/lint check and fix commands and by making both checks required verification steps.
