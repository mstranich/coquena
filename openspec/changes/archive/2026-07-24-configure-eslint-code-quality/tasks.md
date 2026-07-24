## 1. Configure the frontend quality baseline

- [x] 1.1 Add ESLint 10, the compatible TypeScript and Angular ESLint integrations, ESLint Stylistic, and dprint; remove Prettier from `apps/web/package.json` and regenerate the npm lockfile.
- [x] 1.2 Create the committed ESLint flat configuration for frontend TypeScript and Angular templates, including explicit ignores for dependencies, build output, and the Angular cache.
- [x] 1.3 Configure the agreed review-oriented, auto-fixable Stylistic rules for whitespace/layout, braces, and one statement per line, leaving indentation and line endings to dprint.
- [x] 1.4 Create the committed dprint configuration with pinned TypeScript and Angular-capable markup plugins and the agreed 160-column formatting conventions.

## 2. Expose and enforce the workflow

- [x] 2.1 Add non-mutating `format:check` and explicit `format` frontend npm scripts using dprint.
- [x] 2.2 Add non-mutating `lint` and explicit `lint:fix` frontend npm scripts.
- [x] 2.3 Update `task web:verify` so the non-mutating format check and lint check run before frontend tests and production build.

## 3. Establish and verify the baseline

- [x] 3.1 Run the explicit formatter on configured TypeScript and Angular HTML sources, then run the lint fixer and manually resolve any remaining lint errors without unrelated refactors.
- [x] 3.2 Verify that `npm run format:check` and `npm run lint` pass without changing tracked files.
- [x] 3.3 Verify `task web:verify` and `task verify` pass, confirming format or lint failure blocks verification before tests/build.
