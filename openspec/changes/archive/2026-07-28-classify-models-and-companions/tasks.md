## 1. Classification digest type and extraction

- [x] 1.1 Add a `Classification` type in `internal/model` holding declared type, architecture, size label, file type, context length, embedding length (principal) or projection dimension and encoder flags (projector), an SSM presence flag, a tokenizer sub-digest (identity, pre, vocabulary size, bos/eos/pad/unk IDs, vocabulary digest), and publisher sampling recommendations.
- [x] 1.2 Add `Classify(meta map[string]any) Classification`, deriving the declared type from `general.type` and reporting an explicit unknown type when the key is absent or unrecognised, without inferring one from architecture or filename.
- [x] 1.3 Read the architecture-namespaced keys through the artifact's own `general.architecture` value rather than a fixed prefix, so `gemma4.embedding_length` and `qwen35.embedding_length` are both found.
- [x] 1.4 Accept both known projector key spellings — `clip.vision.projector_type` and `clip.projector_type` — and record an unrecognised spelling as unknown rather than guessing.
- [x] 1.5 Compute the tokenizer vocabulary digest as a stable hash over the vocabulary arrays, so two artifacts with identical vocabularies produce an identical digest and the arrays themselves are never retained.
- [x] 1.6 Add tests covering a principal, a projector carrying both key spellings, an artifact with `<arch>.ssm.*` keys, an artifact declaring no type, and two artifacts with identical vocabularies producing equal digests.

## 2. Sidecar schema: drop raw metadata, carry the digest

- [x] 2.1 Remove `Record.GGUF` as persisted state and add the classification digest to `Record`, raising the sidecar schema version.
- [x] 2.2 Reject a sidecar written under the previous schema version when reading a record, so its artifact falls into the existing `unreconciled` path rather than being upgraded in place.
- [x] 2.3 Populate the classification digest at publish time from the completed file's own header, in `Publish` and `PublishInPlace`.
- [x] 2.4 Add tests confirming a published sidecar carries the digest and no raw metadata map, that a previous-version sidecar reads as invalid, and that its artifact is then enumerated as `unreconciled`.

## 3. Inspection projection without raw metadata

- [x] 3.1 Replace `InspectionResult` and the `Inspection` type with a derived projection reporting identity, classification, and scalar metadata keys only, removing the array-omission logic and `OmittedMetadata`.
- [x] 3.2 Change `InspectRemote`, `InspectLocal`, and `InspectPartial` to return the derived projection rather than a record carrying the raw metadata map, reading the header on demand in each case.
- [x] 3.3 Add tests confirming an inspection result contains no vocabulary array contents and that local and remote inspection of the same artifact agree on its classification.

## 4. Compatibility criteria

- [x] 4.1 Add a projector-to-principal criterion comparing the projector's projection dimension against the principal's architecture-specific embedding length, returning admissible, not admissible, or unknown when either value is missing.
- [x] 4.2 Add a draft-to-target criterion comparing tokenizer identity, pre-tokenizer, vocabulary size, special token IDs, and vocabulary digest.
- [x] 4.3 Add an evaluation returning every installed artifact satisfying a given criterion, rather than selecting one.
- [x] 4.4 Add tests using the verified real values: projection dimension 5120 against embedding length 5120 admitted, projector internal width 1152 never used as the linking value, projection dimension 3840 against embedding length 3840 admitted, mismatched dimensions refused, identical tokenizers admitted as a draft pair, and differing tokenizer families refused.

## 5. Model-implied runtime capability

- [x] 5.1 Derive an advisory runtime capability contribution from a classification: an out-of-range `general.file_type`, the declared architecture, and the presence of SSM keys.
- [x] 5.2 Extend the preset runtime constraint computation to accept the bound model's contribution as a second source of computed constraint, attributed to the model rather than to a field.
- [x] 5.3 Confirm the contribution stays advisory: it is reported, and it never assigns a preset value, selects a runtime, or blocks authoring.
- [x] 5.4 Add tests for a ternary/1-bit file type producing a contribution, an SSM-bearing model producing one, an ordinary model producing none, and the field-attributed bounds remaining reported separately.

## 6. Resolver diagnostics carrying candidates

- [x] 6.1 Extend the repository resolution ambiguity/no-match error to carry the candidate filenames alongside the count.
- [x] 6.2 Extend `ErrAmbiguousInstalledMatch` and `ErrNoInstalledMatch` to carry the matching or available filenames.
- [x] 6.3 Add tests asserting each diagnostic names the filenames rather than only how many matched.

## 7. Bulk reconciliation with classification

- [x] 7.1 Add a bulk reconciliation that walks the configured models directory, reads each unreconciled artifact's header, classifies it, and persists its sidecar in one pass.
- [x] 7.2 Compute the companion relations among the artifacts found in that pass and report them as part of the result.
- [x] 7.3 Report a per-artifact diagnostic on failure and continue the pass rather than abandoning it.
- [x] 7.4 Confirm reconciliation stays explicit: no listing command triggers it.
- [x] 7.5 Add tests for a directory mixing installed, previous-schema, and foreign artifacts; for a pass continuing past one failure; and for the computed relations appearing in the result.

## 8. CLI: discover, local inspect, tree list, companion download

- [x] 8.1 Add `coquena model discover <repository-or-url[:selector]>`, reporting every candidate with its classification, marking the one a selector would resolve to, and printing the repository model card as plain text.
- [x] 8.2 Change `coquena model inspect` to take a local path, call `Manager.InspectLocal`, and issue no network request.
- [x] 8.3 Render human `inspect` output as a `Metadata`/`Value` table including the declared type, replacing the bare resolved-revision line.
- [x] 8.4 Print the candidate filenames when a selector matches zero or several artifacts, for every command that resolves a selector.
- [x] 8.5 Render `coquena model list` as a tree nesting companions beneath their compatible principal, reporting an unmatched companion at the top level, in both human and `--json` output.
- [x] 8.6 Add `--all` to `coquena model reconcile`, mutually exclusive with a path argument, still requiring exactly one of `--online`/`--accept`.
- [x] 8.7 Add the companion opt-in flag to `coquena model download`, creating one job per artifact and reporting every job ID, leaving default single-artifact behavior unchanged.
- [x] 8.8 Update `modelUsage` for the new and changed commands.
- [x] 8.9 Add tests for discovery output, local inspection, the classified table, candidate listing on ambiguity, the tree listing in both modes, bulk reconciliation dispatch, and companion download returning several job IDs.

## 9. HTTP API and Angular workspace

- [x] 9.1 Remove the `includeRaw` inspection option from the HTTP API so the property is rejected as unknown.
- [x] 9.2 Return the classification in inspection and remote-discovery responses, and expose an installed artifact's classification for lazy loading.
- [x] 9.3 Update the Angular Models workspace to load a classification instead of a raw sidecar document, showing the declared type in each candidate's collapsible table.
- [x] 9.4 Add tests for the rejected `includeRaw` property and for classification appearing in the library and inspection responses.

## 10. Preset companion roles

- [x] 10.1 Store a companion role assignment (`draft` or `projector`) on a preset as a criterion rather than a file pin.
- [x] 10.2 Refuse an assignment failing its role's compatibility criterion, with a diagnostic naming the criterion, and accept an explicit override that records the unmet criterion as a diagnostic.
- [x] 10.3 Report every installed artifact satisfying a preset's companion criterion, selecting none automatically.
- [x] 10.4 Add tests for a compatible assignment, a refused incompatible one, an overridden one, and several admissible options reported together.

## 11. Documentation and verification

- [x] 11.1 Update `README.md`, which documents the removed raw-inspection behavior and the `includeRaw` option.
- [x] 11.2 Run the Go build, vet, formatting check, and full test suite, plus the frontend checks for the workspace change.
- [x] 11.3 Validate the change with `openspec validate classify-models-and-companions --strict`.
- [ ] 11.4 Demonstrate the end-to-end path manually against real artifacts: discover a repository holding a principal and a projector without downloading them, download both with the companion flag, confirm the published sidecars are kilobytes rather than megabytes, run `reconcile --all` over a directory holding models written by another application, and confirm `model list` nests each projector beneath the principal whose embedding length it matches.
  - Partially demonstrated. Verified against the real repository over the
    network, with no artifact downloaded: `coquena model discover
    lmstudio-community/gemma-4-12B-it-QAT-GGUF[:Q4_0]` classified both
    candidates from their headers over HTTP Range — principal
    `gemma-4-12B-it-QAT-Q4_0.gguf` (`model`/`gemma4`, embedding length 3840)
    and projector `mmproj-gemma-4-12B-it-QAT-BF16.gguf` (`mmproj`/`clip`,
    projection dimension 3840) — marked the selector's candidate, and printed
    the model card as plain text. The publisher sampling key spellings assumed
    in section 1 are confirmed correct: the real header yields temp 1,
    top_k 64, top_p 0.95 from `general.sampling.temp`/`.top_k`/`.top_p`, so
    `Classify` needs no change. Verified offline against synthetic GGUF
    artifacts placed in the models directory: `reconcile --all --accept`
    classified both, reported the projector-to-principal relation, wrote
    sub-kilobyte sidecars carrying `classification` and no raw metadata map,
    `model list` nested the projector beneath the principal in both human and
    `--json` output, `model inspect <path>` rendered the classified
    Metadata/Value table with no network request, and rewriting a sidecar as
    schema version 1 returned its artifact to `unreconciled`.
  - Remaining for the operator, by hand: `coquena model download
    lmstudio-community/gemma-4-12B-it-QAT-GGUF:Q4_0 --with-companions`
    (multi-gigabyte transfer), confirming several job IDs are reported and one
    job exists per artifact; and confirming the sidecars published by that
    download are kilobytes rather than the megabytes the previous schema cost.
  - NOTE (2026-07-28, post-merge design review): manual testing against a real
    multi-repository library (`coquena model list`, `coquena model reconcile
    <reference> --online`) surfaced that the tree rendering and the
    library-wide `--all` this task asked to confirm were themselves the wrong
    design — a projector is compatible with every quantization of its
    principal, so nesting it under one is arbitrary, and reconciling an entire
    library in one pass conflates cases that need a human decision with cases
    that do not. Both are being corrected in the follow-up change
    `repository-driven-reconciliation` (design discussion captured in Engram
    observations #345–#352), which also fixes `reconcile`'s reference
    resolution defaulting to the wrong library state, and the two-vocabulary
    inconsistency (path vs. reference) this task's own testing exposed across
    `inspect`, `revision`, and `reconcile`. The remainder of this task —
    discovery, classification, the sidecar digest, and companion-aware
    download — held up under manual testing and is not affected.
