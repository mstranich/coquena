## 1. Offline model resolution

- [x] 1.1 Add resolution of a `user/repo:QUANT` reference against the installed library reusing the existing reference grammar, reporting the exact filename and recorded revision without any network request.
- [x] 1.2 Distinguish the no-match and multiple-match failures with separate diagnostics.
- [x] 1.3 Add tests for a single match, no match, multiple matches, an empty library, and absence of network access.

## 2. Installed model listing

- [x] 2.1 Add a `coquena model list` command reporting each installed model's repository, quant reference form, and resolved filename, with JSON output.
- [x] 2.2 Report an empty library as an empty list rather than an error.
- [ ] 2.3 Add CLI unit and executable integration tests for a populated library, an empty library, JSON output, and pre-init behavior.

## 3. Preset model

- [x] 3.1 Add a model-preset package with the preset model: kebab-case name as identity, optional title, model binding holding requested and resolved layers, runtime constraint, and the field values of one preset.
- [x] 3.2 Add the reserved global preset that holds values only, cannot be created or deleted, and carries no model or runtime binding.
- [x] 3.3 Add whole-document validation covering name form, uniqueness, the reserved `global` and `render` names, and contradiction between a runtime identity pin and a capability constraint.
- [x] 3.4 Add tests for every valid shape and every rejection class.

## 4. Value validation against the catalog

- [x] 4.1 Add validation of each authored value against its field's kind in the persisted field catalog, refusing unknown fields, wrong-kind values, and enum values outside their options.
- [x] 4.2 Resolve a field named by a short-argument or environment-variable alias to its canonical name and store it canonically.
- [x] 4.3 Add an advisory diagnostic when a field's parent prerequisite is unsatisfied, without refusing the value.
- [x] 4.4 Add tests for every kind, every rejection class, alias resolution, and the advisory parent case.

## 5. Inheritance

- [x] 5.1 Add effective-value resolution merging the global preset with a preset's own layer and labeling each value as owned or inherited.
- [x] 5.2 Add own-layer removal that returns a value to inheritance, keeping inheritance to exactly the global and own layers.
- [x] 5.3 Verify that every mutation touches only the addressed preset's own layer.
- [x] 5.4 Add tests for inheriting, overriding, returning to inheritance, an unauthored field reporting no value, and reading the global preset alone.

## 6. Model binding

- [x] 6.1 Add binding that stores the requested reference and, when resolvable offline, the resolved filename, revision, and resolution time, never rewriting the requested layer.
- [x] 6.2 Refuse an unresolvable reference unless a missing model is explicitly allowed, and refuse an ambiguous reference always.
- [x] 6.3 Add stale-resolution reporting when a resolved filename is absent from the library, and re-resolution on re-binding.
- [x] 6.4 Add tests for binding an installed model, allowing a missing model, refusal on no match and on ambiguity, replacing an existing binding without touching values, and stale detection followed by re-binding.

## 7. Runtime constraint

- [x] 7.1 Add the declared constraint of provider, backend, and release range, with an explicit installed-runtime identity pin that excludes the capability form.
- [x] 7.2 Add computation of the release range implied by the availability of the fields a preset uses, attributing each bound to the field that produced it.
- [x] 7.3 Add evaluation of a preset against a runtime producing `admitted`, `not-admitted`, or `unknown`, reusing the established availability comparison.
- [x] 7.4 Verify that neither constraint blocks authoring, storing, renaming, or removing a preset.
- [x] 7.5 Add tests for a declared constraint, a computed bound with attribution, a portable preset, contradiction refusal, and each evaluation outcome.

## 8. Persisted document

- [x] 8.1 Add reading of `config/model-presets.json` with `schemaVersion` 1, resolving an absent document as an empty preset set and returning actionable diagnostics for malformed and unsupported-version documents.
- [x] 8.2 Add atomic publication reusing the established temporary-file-and-rename sequence, leaving the previous document intact on failure.
- [x] 8.3 Add define, remove, rename, title, binding, and value mutations, where every mutation validates the whole document before publishing and rename preserves title, bindings, and values.
- [x] 8.4 Add tests for empty resolution, first publication, restart durability, refusal of a duplicate definition, rename preserving state, rename collision refusal, refusal to delete global, atomic write failure, and every rejection class.

## 9. INI rendering

- [x] 9.1 Add rendering of a `--models-preset` INI for a named runtime as a pure function of the persisted presets, the field catalog, and the runtime identity, with no network request and no file write.
- [x] 9.2 Emit the global preset as `[*]` and one section per admitted preset, flattening inheritance so an inherited value appears only in `[*]`.
- [x] 9.3 Emit only canonical long field names without leading dashes, and emit the artifact path for a resolved model or the requested `user/repo:QUANT` reference otherwise.
- [x] 9.4 Omit a preset the named runtime does not admit, reporting a diagnostic naming the unmet constraint, and render for a runtime identity that is not installed with a diagnostic.
- [x] 9.5 Add opt-in writing to an operator-supplied path or to `data/servers/<runtime-id>.ini` by default, never beneath `config/`, reporting the written path.
- [x] 9.6 Add a golden-file test asserting byte-identical output, plus tests for alias canonicalization, the installed and not-installed model forms, omission with diagnostic, inheritance flattening, an uninstalled runtime, rendering without writing, the default write path, and a supplied write path.

## 10. Field removal across both documents

- [x] 10.1 Add detection of the presets holding a value for a field or any field in its descendant subtree, including the global preset.
- [x] 10.2 Refuse an unforced removal of a referenced field with a diagnostic naming every referencing preset, leaving both documents unchanged.
- [x] 10.3 Add forced cascading removal that drops the field subtree from the catalog and every value those fields held from every preset, reporting what it removed.
- [x] 10.4 Publish both documents so that a failure leaves neither one referencing content the other dropped.
- [x] 10.5 Add a force parameter to `DELETE /api/fields/<name>` and `--force` to `coquena field remove`, keeping unforced removal of an unreferenced field unchanged.
- [x] 10.6 Add tests for an unreferenced removal, refusal on a direct reference, refusal on a descendant reference, refusal naming the global preset, forced cascade across both documents, and a publication failure leaving both intact.

## 11. Local WebUI preset API

- [x] 11.1 Add `GET` and `POST /api/presets`, reporting the global preset separately from the model presets with a kind discriminator and refusing an existing name with a conflict.
- [x] 11.2 Add `GET` and `DELETE /api/presets/<name>`, where reads report bindings and effective values with their inheritance source.
- [x] 11.3 Add `MOVE /api/presets/<name>` with a destination header, reporting the new location on success and a conflict on a used destination.
- [x] 11.4 Add `PUT` on the `title`, `model`, and `runtime` subresources and `DELETE` on the `runtime` subresource.
- [x] 11.5 Add `PUT` and `DELETE /api/presets/<name>/values/<field>`, writing only the addressed preset's own layer.
- [x] 11.6 Add `GET /api/presets/render` with an INI format parameter and a runtime parameter, returning the rendering as text and never writing a file.
- [x] 11.7 Refuse a whole-entity `PUT` on a preset and a `GET` on a single value as method not allowed, and refuse operations that cannot apply to `global`.
- [x] 11.8 Add strict request decoding, advisory diagnostics inside successful responses, safe JSON error mapping, and the established status mapping of not-found for an unknown preset and invalid-request for body failures.
- [x] 11.9 Preserve the Angular fallback boundary for non-API routes only.
- [x] 11.10 Add HTTP contract tests for every endpoint, every refusal class, `MOVE` success and conflict, advisory success, unsupported methods, malformed and unknown JSON members, and uninitialized installations.

## 12. CLI preset management

- [x] 12.1 Add `coquena preset` parsing and usage text for `define`, `rename`, `remove`, `list`, `show`, `set`, `unset`, `bind`, and `unbind`.
- [x] 12.2 Add `define <name>` with a model reference, an optional title, and an explicit allowance for a missing model.
- [x] 12.3 Add `set <name|global> <field> <value>`, `set <name> --title <text>`, and `unset <name|global> <field>`.
- [x] 12.4 Add `bind model <name> <ref>`, `bind runtime <name>` with named provider, backend, release-range, and identity flags, and `unbind runtime <name>`.
- [x] 12.5 Add `rename <old> <new>` and `remove <name>`.
- [x] 12.6 Add `list` distinguishing the global preset, and `show` reporting name, title, model binding, declared and computed runtime constraints, and every effective value labeled owned or inherited, without requiring JSON.
- [x] 12.7 Add `render` with a runtime selector, printing to standard output and writing only with `--write` and its optional path.
- [x] 12.8 Connect every command to the shared preset behavior without duplicating validation, and report refusals with a non-zero exit status leaving the document unchanged.
- [ ] 12.9 Add CLI unit and executable integration tests for every command, every refusal class, global addressing, inheritance labeling in human output, JSON output, and pre-init behavior. (Unit coverage for the core commands is in; executable-binary integration tests and explicit pre-init refusal tests are not yet added — same gap as 2.3.)

## 13. Angular presets workspace

- [x] 13.1 Replace the in-memory preset state with preset-API-backed state, keeping the existing editor interactions unchanged.
- [x] 13.2 Render owned and inherited values from the source reported by the API without deriving inheritance in the browser, and add the affordance that returns an overridden value to inheritance.
- [x] 13.3 Present the model binding, including the not-installed condition, and present the declared and computed runtime constraints with the attribution of each computed bound. (Stale-binding presentation is not surfaced yet: the API reports staleness as a diagnostic, not as a field on the preset view, so there is nothing for the component to bind to until that's added.)
- [x] 13.4 Surface returned safe errors without claiming a change was stored, and render preset state after a reload.
- [x] 13.5 Add tests for API-backed listing, authoring, inheritance labeling, returning to inheritance, binding presentation, rejection rendering, and reload.

## 14. Verification

- [x] 14.1 Run the Go build, vet, formatting check, and full test suite, and the frontend lint and test suites.
- [x] 14.2 Validate the change with `openspec validate --strict`.
- [x] 14.3 Demonstrate the end-to-end path manually: define a preset, bind a model and a runtime constraint, author global and overriding values, render the INI, restart, and confirm durability.
