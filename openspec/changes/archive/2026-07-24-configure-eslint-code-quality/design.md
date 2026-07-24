## Context

`apps/web` is an Angular 22 and TypeScript workspace. It has an EditorConfig baseline and lists Prettier as a development dependency, but exposes neither a formatter nor lint commands. `task web:verify` runs tests and a production build only; backend verification currently executes `go fmt ./...`, which writes files rather than checking a clean formatting state.

The desired outcome is readable, consistently structured frontend code that can be reviewed without accepting Prettier's intentionally limited formatting choices. The project baseline already supplies Node.js 24.18.0, satisfying ESLint 10's Node requirement.

## Goals / Non-Goals

**Goals:**
- Establish ESLint 10 as the frontend static-analysis engine using its flat configuration model.
- Establish dprint as the configurable formatter for TypeScript and Angular HTML templates.
- Enforce a small, explicit set of auto-fixable TypeScript style rules through `@stylistic/eslint-plugin`, centered on indentation, whitespace, line layout, braces, and avoidance of multiple statements per line.
- Lint Angular TypeScript and HTML templates using the Angular ESLint integration.
- Provide separate non-mutating check and explicit fix commands, and include the check in workspace verification.
- Keep the existing Go formatting behavior unchanged in this narrow frontend-quality change.

**Non-Goals:**
- Do not use Prettier, Oxlint, Biome, or another formatter/linter beyond the selected ESLint and dprint tools.
- Do not lint SCSS or define a general documentation/JSON formatting policy.
- Do not add Git hooks, CI configuration, editor settings, custom lint rules, or automatic save-time fixes.
- Do not introduce type-aware lint rules unless the initial configuration requires them for an adopted Angular rule.

## Decisions

### Use ESLint 10 with flat configuration

The frontend will install the current compatible ESLint 10 release and define a committed `eslint.config.*` flat configuration. The configuration will use `@eslint/js`, `typescript-eslint`, and the current Angular ESLint packages compatible with Angular 22.

Flat configuration is ESLint's current model and makes the source files, ignores, parsers, plugins, and overrides explicit in one reviewable file.

**Alternatives considered:**
- Legacy `.eslintrc*`: rejected because it is deprecated in the ESLint 10 workflow.
- Oxlint: rejected for this change because it is a different Rust-native linter and does not provide the established Angular template lint integration required here.

### Keep style rules in ESLint Stylistic, not ESLint core or Prettier

The configuration will use `@stylistic/eslint-plugin` for the selected formatting rules, with project values matching `.editorconfig` where applicable (two spaces, LF, final newline). Rules will be intentionally limited to review readability, and each enabled non-default rule will be documented in the configuration.

The initial rule set will require multi-line, spaced, consistently indented code and prohibit multiple statements on one line. It will not impose stylistic preferences unrelated to review clarity.

**Alternatives considered:**
- ESLint core formatting rules: rejected because they are deprecated and being moved out of core.
- Prettier: rejected by project preference; its constrained option surface does not provide the desired control.

### Separate static analysis from formatting

ESLint will analyze correctness, Angular conventions, and selected TypeScript style rules that do not compete with formatting. dprint will own deterministic source reflow, indentation, and line endings for TypeScript and external Angular HTML templates. Its committed configuration will preserve the chosen review style, including two-space indentation, LF, single quotes where applicable, semicolons, trailing commas only in multiline constructs, and a 160-column line width.

This separation addresses compacted Angular templates, which Angular ESLint can analyze semantically but does not generally reflow. dprint's TypeScript and Angular-capable markup plugins will be pinned in its committed configuration so all developers and verification runs use the same formatter versions.

**Alternatives considered:**
- ESLint Stylistic alone: rejected because it does not provide general external Angular HTML template reflow; its indentation and line-ending rules are also excluded to avoid conflicting fixes with dprint.
- Prettier: rejected by project preference.

### Separate checking from fixing

`npm run format:check` will run dprint's non-mutating check and `npm run format` will run its explicit formatting mode. `npm run lint` will be non-mutating and fail on configured lint errors; `npm run lint:fix` will invoke ESLint's explicit `--fix` mode. `task web:verify` will run the non-mutating format check and lint check before tests and build, so verification never changes tracked files.

**Alternatives considered:**
- Run `--fix` during verification: rejected because it can hide formatting changes and leave a dirty working tree.
- Treat lint as warning-only: rejected because review readability is an enforced baseline.

### Normalize the initial baseline as a dedicated, reviewable step

After rules are agreed, `lint:fix` will be run only against source files covered by the new configuration. Any fixes that cannot be applied safely will be resolved manually. The baseline normalization will be committed with the configuration so later feature changes do not inherit unrelated formatting churn.

## Risks / Trade-offs

- [A broad stylistic rule set produces noisy initial diffs] → Begin with a deliberately small rule set and keep baseline normalization in the same dedicated change, separate from product work.
- [Angular ESLint package compatibility may lag ESLint 10] → Select versions with declared peer compatibility during implementation and validate `npm ci`, format check, lint, test, and build before accepting the dependency lockfile.
- [Formatter plugins are downloaded separately from the npm lockfile] → Pin plugin URLs and checksums in committed `dprint.json` and validate a clean dependency restore.
- [Template lint rules can report existing conventions as violations] → Start from the Angular recommended configuration and only elevate rules that are actionable for this codebase.
- [ESLint is slower than a native Rust linter] → This workspace is small; preserve Angular-aware correctness and template coverage rather than optimize prematurely.

## Migration Plan

1. Add the lint and formatting dependencies/configurations while removing Prettier.
2. Add format/lint check and fix scripts and wire the non-mutating checks into `task web:verify`.
3. Run the explicit formatter followed by the lint fixer, then manually resolve remaining violations and validate the entire workspace.
4. If the configuration proves incompatible, revert the dependency/configuration commit; it has no runtime or persisted-data impact.

## Open Questions

- None. Exact rule options will be selected from the existing code and recorded in the committed flat configuration during implementation.
