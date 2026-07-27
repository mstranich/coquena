## 1. Classification digest type and extraction

- [ ] 1.1 Add a `Classification` type in `internal/model` holding declared type, architecture, size label, file type, context length, embedding length (principal) or projection dimension and encoder flags (projector), an SSM presence flag, a tokenizer sub-digest (identity, pre, vocabulary size, bos/eos/pad/unk IDs, vocabulary digest), and publisher sampling recommendations.
- [ ] 1.2 Add `Classify(meta map[string]any) Classification`, deriving the declared type from `general.type` and reporting an explicit unknown type when the key is absent or unrecognised, without inferring one from architecture or filename.
- [ ] 1.3 Read the architecture-namespaced keys through the artifact's own `general.architecture` value rather than a fixed prefix, so `gemma4.embedding_length` and `qwen35.embedding_length` are both found.
- [ ] 1.4 Accept both known projector key spellings — `clip.vision.projector_type` and `clip.projector_type` — and record an unrecognised spelling as unknown rather than guessing.
- [ ] 1.5 Compute the tokenizer vocabulary digest as a stable hash over the vocabulary arrays, so two artifacts with identical vocabularies produce an identical digest and the arrays themselves are never retained.
- [ ] 1.6 Add tests covering a principal, a projector carrying both key spellings, an artifact with `<arch>.ssm.*` keys, an artifact declaring no type, and two artifacts with identical vocabularies producing equal digests.

## 2. Sidecar schema: drop raw metadata, carry the digest

- [ ] 2.1 Remove `Record.GGUF` as persisted state and add the classification digest to `Record`, raising the sidecar schema version.
- [ ] 2.2 Reject a sidecar written under the previous schema version when reading a record, so its artifact falls into the existing `unreconciled` path rather than being upgraded in place.
- [ ] 2.3 Populate the classification digest at publish time from the completed file's own header, in `Publish` and `PublishInPlace`.
- [ ] 2.4 Add tests confirming a published sidecar carries the digest and no raw metadata map, that a previous-version sidecar reads as invalid, and that its artifact is then enumerated as `unreconciled`.

## 3. Inspection projection without raw metadata

- [ ] 3.1 Replace `InspectionResult` and the `Inspection` type with a derived projection reporting identity, classification, and scalar metadata keys only, removing the array-omission logic and `OmittedMetadata`.
- [ ] 3.2 Change `InspectRemote`, `InspectLocal`, and `InspectPartial` to return the derived projection rather than a record carrying the raw metadata map, reading the header on demand in each case.
- [ ] 3.3 Add tests confirming an inspection result contains no vocabulary array contents and that local and remote inspection of the same artifact agree on its classification.

## 4. Compatibility criteria

- [ ] 4.1 Add a projector-to-principal criterion comparing the projector's projection dimension against the principal's architecture-specific embedding length, returning admissible, not admissible, or unknown when either value is missing.
- [ ] 4.2 Add a draft-to-target criterion comparing tokenizer identity, pre-tokenizer, vocabulary size, special token IDs, and vocabulary digest.
- [ ] 4.3 Add an evaluation returning every installed artifact satisfying a given criterion, rather than selecting one.
- [ ] 4.4 Add tests using the verified real values: projection dimension 5120 against embedding length 5120 admitted, projector internal width 1152 never used as the linking value, projection dimension 3840 against embedding length 3840 admitted, mismatched dimensions refused, identical tokenizers admitted as a draft pair, and differing tokenizer families refused.

## 5. Model-implied runtime capability

- [ ] 5.1 Derive an advisory runtime capability contribution from a classification: an out-of-range `general.file_type`, the declared architecture, and the presence of SSM keys.
- [ ] 5.2 Extend the preset runtime constraint computation to accept the bound model's contribution as a second source of computed constraint, attributed to the model rather than to a field.
- [ ] 5.3 Confirm the contribution stays advisory: it is reported, and it never assigns a preset value, selects a runtime, or blocks authoring.
- [ ] 5.4 Add tests for a ternary/1-bit file type producing a contribution, an SSM-bearing model producing one, an ordinary model producing none, and the field-attributed bounds remaining reported separately.

## 6. Resolver diagnostics carrying candidates

- [ ] 6.1 Extend the repository resolution ambiguity/no-match error to carry the candidate filenames alongside the count.
- [ ] 6.2 Extend `ErrAmbiguousInstalledMatch` and `ErrNoInstalledMatch` to carry the matching or available filenames.
- [ ] 6.3 Add tests asserting each diagnostic names the filenames rather than only how many matched.

## 7. Bulk reconciliation with classification

- [ ] 7.1 Add a bulk reconciliation that walks the configured models directory, reads each unreconciled artifact's header, classifies it, and persists its sidecar in one pass.
- [ ] 7.2 Compute the companion relations among the artifacts found in that pass and report them as part of the result.
- [ ] 7.3 Report a per-artifact diagnostic on failure and continue the pass rather than abandoning it.
- [ ] 7.4 Confirm reconciliation stays explicit: no listing command triggers it.
- [ ] 7.5 Add tests for a directory mixing installed, previous-schema, and foreign artifacts; for a pass continuing past one failure; and for the computed relations appearing in the result.

## 8. CLI: discover, local inspect, tree list, companion download

- [ ] 8.1 Add `coquena model discover <repository-or-url[:selector]>`, reporting every candidate with its classification, marking the one a selector would resolve to, and printing the repository model card as plain text.
- [ ] 8.2 Change `coquena model inspect` to take a local path, call `Manager.InspectLocal`, and issue no network request.
- [ ] 8.3 Render human `inspect` output as a `Metadata`/`Value` table including the declared type, replacing the bare resolved-revision line.
- [ ] 8.4 Print the candidate filenames when a selector matches zero or several artifacts, for every command that resolves a selector.
- [ ] 8.5 Render `coquena model list` as a tree nesting companions beneath their compatible principal, reporting an unmatched companion at the top level, in both human and `--json` output.
- [ ] 8.6 Add `--all` to `coquena model reconcile`, mutually exclusive with a path argument, still requiring exactly one of `--online`/`--accept`.
- [ ] 8.7 Add the companion opt-in flag to `coquena model download`, creating one job per artifact and reporting every job ID, leaving default single-artifact behavior unchanged.
- [ ] 8.8 Update `modelUsage` for the new and changed commands.
- [ ] 8.9 Add tests for discovery output, local inspection, the classified table, candidate listing on ambiguity, the tree listing in both modes, bulk reconciliation dispatch, and companion download returning several job IDs.

## 9. HTTP API and Angular workspace

- [ ] 9.1 Remove the `includeRaw` inspection option from the HTTP API so the property is rejected as unknown.
- [ ] 9.2 Return the classification in inspection and remote-discovery responses, and expose an installed artifact's classification for lazy loading.
- [ ] 9.3 Update the Angular Models workspace to load a classification instead of a raw sidecar document, showing the declared type in each candidate's collapsible table.
- [ ] 9.4 Add tests for the rejected `includeRaw` property and for classification appearing in the library and inspection responses.

## 10. Preset companion roles

- [ ] 10.1 Store a companion role assignment (`draft` or `projector`) on a preset as a criterion rather than a file pin.
- [ ] 10.2 Refuse an assignment failing its role's compatibility criterion, with a diagnostic naming the criterion, and accept an explicit override that records the unmet criterion as a diagnostic.
- [ ] 10.3 Report every installed artifact satisfying a preset's companion criterion, selecting none automatically.
- [ ] 10.4 Add tests for a compatible assignment, a refused incompatible one, an overridden one, and several admissible options reported together.

## 11. Documentation and verification

- [ ] 11.1 Update `README.md`, which documents the removed raw-inspection behavior and the `includeRaw` option.
- [ ] 11.2 Run the Go build, vet, formatting check, and full test suite, plus the frontend checks for the workspace change.
- [ ] 11.3 Validate the change with `openspec validate classify-models-and-companions --strict`.
- [ ] 11.4 Demonstrate the end-to-end path manually against real artifacts: discover a repository holding a principal and a projector without downloading them, download both with the companion flag, confirm the published sidecars are kilobytes rather than megabytes, run `reconcile --all` over a directory holding models written by another application, and confirm `model list` nests each projector beneath the principal whose embedding length it matches.
