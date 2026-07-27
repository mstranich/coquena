## 1. Field model

- [x] 1.1 Add a llama.cpp field catalog package with the field model: canonical bare INI name as identity, required immutable kind, short-argument and environment-variable aliases, parent prerequisite, and provider-qualified availability.
- [x] 1.2 Add whole-catalog validation covering bare-INI-key names, known kinds, name and alias uniqueness across canonical names and aliases, enum option validity, and resolvable parent references.
- [x] 1.3 Add tests for every valid kind and every rejection class.

## 2. Availability evaluation

- [x] 2.1 Add ordered comparison of official `b<digits>` release tags and range evaluation producing `admitted`, `not-admitted`, or `unknown`.
- [x] 2.2 Add tests for in-range, below-range, at-and-after-exclusion, unbounded, and unorderable release inputs.
- [x] 2.3 Verify that availability never blocks authoring, saving, or deleting a field.

## 3. Persisted catalog

- [x] 3.1 Add reading of `config/llamacpp-fields.json` with `schemaVersion` 1, resolving an absent document as an empty catalog and returning actionable diagnostics for malformed and unsupported-version documents.
- [x] 3.2 Add atomic publication reusing the established temporary-file-and-rename sequence, leaving the previous document intact on failure.
- [x] 3.3 Add define, upsert, and remove behavior, where remove deletes the named field and its descendant subtree, and every mutation validates the whole catalog before publishing.
- [x] 3.4 Add tests for empty resolution, first publication, restart durability, upsert replacing an existing field, subtree removal, atomic write failure, and every rejection class.

## 4. Local WebUI catalog API

- [x] 4.1 Add `GET /api/fields` returning the catalog with per-field availability, following the established `/api/<noun>` routing convention.
- [x] 4.2 Add `POST /api/fields` refusing an existing name, and `GET`, `PUT`, `DELETE /api/fields/<name>` with strict request decoding, name-addressed resolution, upsert on `PUT`, and safe JSON error mapping.
- [x] 4.3 Preserve the Angular fallback boundary for non-API routes only.
- [x] 4.4 Add HTTP contract tests for list, define, duplicate refusal, read, upsert creating and replacing, delete with subtree removal, unsupported methods, malformed and unknown JSON fields, and uninitialized installations.

## 5. CLI catalog management

- [x] 5.1 Add `coquena field` parsing and usage text for `define`, `remove`, `list`, and `show`.
- [x] 5.2 Add `define <kind> <name>` flags for default value, repeatable enum option, `--parent` prerequisite and required parent state, short-argument and environment-variable aliases, provider, first admitting and first excluding releases, and `--overwrite`.
- [x] 5.3 Add overwrite handling that upserts with `--overwrite`, confirms interactively without it, and fails with a diagnostic when the terminal is not interactive.
- [x] 5.4 Add `remove <name>` with descendant subtree removal, and `list`/`show` text and JSON output identifying name, kind, and availability.
- [x] 5.5 Resolve `show` and `remove` by canonical name or alias and report the field under its canonical name.
- [x] 5.6 Connect every command to the shared catalog behavior without duplicating validation.
- [x] 5.7 Add CLI unit and executable integration tests for define, upsert, remove, subtree removal, list, show by name and alias, every invalid-definition class, overwrite with and without the flag on both interactive and non-interactive terminals, and pre-init behavior.

## 6. Seed script

- [x] 6.1 Add a seed script that defines a starter catalog through `coquena field define ... --overwrite`, covering the arguments documented in the upstream `--models-preset` examples and the core server arguments already in use.
- [x] 6.2 Keep the script host-neutral per the repository's Unix-style text conventions and document how to run it.
- [x] 6.3 Add a test that replaying the script twice produces an identical catalog.

## 7. API-backed Fields workspace

- [x] 7.1 Replace the in-memory field state in `catalog-store.ts` with an Angular service using `HttpClient`, RxJS async boundaries, and signal-based state for loading, saving, and error handling.
- [x] 7.2 Keep preset state in browser memory and keep the existing preset interactions unchanged.
- [x] 7.3 Render rejected saves as visible errors without claiming success or discarding the last successfully loaded catalog.
- [x] 7.4 Add Angular tests for load, save, reload durability, and rejected save.

## 8. Verification

- [x] 8.1 Run Go formatting and the complete Go test suite.
- [x] 8.2 Run frontend formatting, linting, tests, and production build; refresh embedded WebUI assets if the repository workflow requires it.
- [x] 8.3 Validate the OpenSpec Change and manually verify catalog persistence across a restart, seeding through the script, defining a field through the CLI and observing it in the Fields workspace, and the reverse.
