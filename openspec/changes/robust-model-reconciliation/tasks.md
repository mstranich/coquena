## 1. Reference grammar: revision axis and exact selector

- [x] 1.1 Add an optional `@revision` segment to the short `user/repo[@revision]:QUANT` form in `parseRef`, defaulting to `main` when absent, without changing the full resolve-URL path.
- [x] 1.2 Treat a selector ending in `.gguf` (case-insensitive) as an exact-filename match instead of substring containment, in both `Resolve`/`resolveRepository` and `ResolveInstalled`.
- [x] 1.3 Add tests for a pinned revision in the short form, the default-`main` case unchanged, an exact-filename selector matching one artifact, an exact-filename selector matching none, and a substring selector's existing ambiguity behavior unchanged.

## 2. Library scan: unreconciled entries

- [x] 2.1 Stop discarding a `.gguf` whose `ReadRecord` fails; derive a best-effort `Source` (`Repository` from the two path segments above the file, `Filename` from the base name, no provider/revision/URL) and report it with `State: "unreconciled"`.
- [x] 2.2 Verify `ResolveInstalled` continues to exclude any non-`installed` state, so an unreconciled entry is not bindable without `--allow-missing` and needs no new refusal path.
- [x] 2.3 Add tests for a sidecar-less `.gguf` appearing as `unreconciled` with its derived identity, a path that does not fit the two-segment shape reporting a partial identity rather than failing the whole scan, and an existing valid Coquena model still reporting `installed` unchanged.

## 3. `local` provider and relaxed validation

- [ ] 3.1 Add `provider: "local"` as a second accepted value in `Source.Validate()`, requiring only `Repository` and `Filename` for it and leaving the `huggingface` provider's existing requirements unchanged.
- [ ] 3.2 Add tests for a valid `local` source, a `local` source missing repository or filename, and the unchanged `huggingface` validation.

## 4. Content hash: computed and persisted

- [ ] 4.1 Compute a SHA-256 digest while streaming a normal Coquena-managed download to disk and persist it in `Artifact.SHA256` at publish time, with no extra read pass over the file.
- [ ] 4.2 Add tests confirming a completed download's sidecar carries the correct digest and that resuming a paused download still produces a correct whole-file digest.

## 5. `coquena model reconcile <path>`

- [ ] 5.1 Add `coquena model reconcile <path> --online`: treat the path-derived repository as a candidate, resolve it against Hugging Face, locate the matching sibling, hash the local file, compare against the sibling's LFS `oid` when Hugging Face reports one, and on agreement write the same `.json`/`.revision` sidecar pair a normal download produces with `Artifact.SHA256` populated.
- [ ] 5.2 Refuse `--online` reconciliation with a clear diagnostic when the local hash disagrees with Hugging Face's reported `oid`, writing no sidecar.
- [ ] 5.3 Add `coquena model reconcile <path> --accept`: no network access; write a reduced `local`-provider sidecar with path-derived repository/filename and `Artifact.SHA256` computed from the local file.
- [ ] 5.4 Require exactly one of `--online`/`--accept`; refuse with usage when neither or both are given.
- [ ] 5.5 Add tests for successful `--online` reconciliation, hash-mismatch refusal, successful `--accept` reconciliation, no-LFS-`oid`-available degradation reporting a partial comparison, and the mutual-exclusivity refusal.

## 6. `coquena model verify <ref>`

- [ ] 6.1 Resolve the reference against the installed library, re-hash the file, and compare against the persisted `Artifact.SHA256`.
- [ ] 6.2 Distinguish three outcomes: hash match, hash mismatch (mark unusable, report the option to re-download or re-reconcile), and no baseline recorded (pre-existing artifact from before this change or from a `local` reconciliation that predates hashing).
- [ ] 6.3 Add tests for each of the three outcomes and for an unresolvable reference.

## 7. `coquena model check-updates <ref>`

- [ ] 7.1 Resolve the reference against the installed library, then query Hugging Face's current revision info using only the sidecar's repository/requested-revision/filename — no local file access.
- [ ] 7.2 Compare the current commit SHA and, when available, the sibling's LFS `oid` against the sidecar's recorded values, reporting up-to-date, update-available (naming the new revision), or partial (no `oid` available, commit-SHA-only comparison).
- [ ] 7.3 Refuse with a clear diagnostic on a `local`-provider entry, since there is no Hugging Face identity recorded to compare against.
- [ ] 7.4 Add tests for up-to-date, update-available, partial comparison, and the `local`-provider refusal.

## 8. CLI wiring and listing

- [ ] 8.1 Add `reconcile`, `verify`, and `check-updates` to `coquena model` usage text and command dispatch, each supporting `--json`.
- [ ] 8.2 Report `unreconciled` entries from `coquena model list` (human and `--json`), distinguished from `installed` and from an in-progress download job.
- [ ] 8.3 Add tests for `model list` reporting a mix of `installed`, `unreconciled`, and in-progress entries in both output modes.

## 9. Verification

- [ ] 9.1 Run the Go build, vet, formatting check, and full test suite.
- [ ] 9.2 Validate the change with `openspec validate robust-model-reconciliation --strict`.
- [ ] 9.3 Demonstrate the end-to-end path manually: drop a sidecar-less `.gguf` under a `publisher/repo` directory, see it as `unreconciled` in `model list`, reconcile it `--accept`, bind it to a preset, then reconcile a second file `--online` against its real Hugging Face repository and confirm `verify` and `check-updates` both succeed against it.
