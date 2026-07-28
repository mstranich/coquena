## 1. Metadata reading without array payloads

- [ ] 1.1 Add a metadata read that decodes scalar values and skips array payloads instead of materializing them, computing the span for fixed-width element types and walking length prefixes for string arrays without allocating.
- [ ] 1.2 Keep the existing full read for the paths that genuinely need array contents, so the tokenizer vocabulary digest computed at reconciliation time is unchanged.
- [ ] 1.3 Add tests confirming the skipping read returns the same scalar keys as the full read for a real-shaped header, that it allocates nothing per array element, and that a malformed array still fails rather than being silently skipped.

## 2. Classification at library enumeration

- [ ] 2.1 Classify every artifact when `Manager.Library` enumerates the library: from the sidecar when one records a classification, from the artifact's own header through the skipping read when none does.
- [ ] 2.2 Confirm that classifying an unreconciled artifact grants it no standing: it stays `unreconciled`, stays excluded from `ResolveInstalled`, and no file is written.
- [ ] 2.3 Add tests for an unreconciled artifact reporting its declared type, for an installed artifact still reporting the sidecar's classification, and for enumeration writing nothing.

## 3. Usability

- [ ] 3.1 Derive usability when the library is read: reconciled and typed is usable; a projector is usable only when a compatible installed principal exists; an unreconciled or unclassifiable artifact is unusable.
- [ ] 3.2 Report a reason alongside an unusable artifact, distinguishing an unreconciled artifact from an orphaned projector from an unclassifiable one.
- [ ] 3.3 Confirm a principal with no installed projector stays usable.
- [ ] 3.4 Add tests for each usability outcome and for an orphaned projector becoming usable once a compatible principal is installed, with no sidecar rewritten.

## 4. Repository-driven reconciliation

- [ ] 4.1 Reconcile the artifacts of one repository in a single action: resolve the repository, enumerate its remote candidates with their classification without downloading any complete artifact, and match them against the artifacts on disk by filename corroborated by metadata.
- [ ] 4.2 Reconcile a principal together with its companions when a reference names a repository holding both, since they are parts of one logical unit.
- [ ] 4.3 Refuse as ambiguous a reference matching more than one principal, naming the candidates, since choosing among quantizations is the operator's decision.
- [ ] 4.4 Report a remote candidate that is absent locally rather than omitting it.
- [ ] 4.5 Refuse an artifact whose computed digest disagrees with the reported content hash, with a diagnostic that says the artifact may be from an earlier revision rather than asserting corruption.
- [ ] 4.6 Add tests for a repository holding a principal and a projector, a repository holding two principals, a companion absent locally, a digest disagreement, and a local artifact matching no remote sibling.

## 5. `--accept` restricted to one artifact

- [ ] 5.1 Keep `--accept` operating on exactly one referenced artifact, persisting a `local`-provider sidecar with no network request.
- [ ] 5.2 Refuse `--accept` combined with repository-wide or library-wide reconciliation, since accepting asserts an identity and that claim cannot be made in bulk.
- [ ] 5.3 Add tests for the single-artifact acceptance and for the refusal.

## 6. Triage over the library

- [ ] 6.1 Replace the current library-wide bulk reconciliation with an online triage that reconciles every repository resolvable without an operator decision.
- [ ] 6.2 Define automatic resolvability as: the repository resolves remotely, every local artifact matches a remote sibling by filename, every matched artifact's content hash agrees, and no ambiguity among principals remains.
- [ ] 6.3 Report every remaining case with its reason and the exact command that resolves it, continuing the pass past a per-repository failure.
- [ ] 6.4 Report the artifact count and total size and require confirmation before hashing, accepting an explicit option to proceed without prompting.
- [ ] 6.5 Add tests for a fully resolvable library, a library mixing resolvable and undecidable repositories, the pass continuing past a failure, and the confirmation being required and bypassable.

## 7. Remove the tree and the superseded bulk mode

- [ ] 7.1 Remove the tree nesting from `coquena model list` in both output modes, reporting every artifact at the same level with its declared type and usability.
- [ ] 7.2 Remove the library-wide artifact-by-artifact bulk reconciliation superseded by section 6, including its result type where nothing else consumes it.
- [ ] 7.3 Reject `--json` on reconciliation explicitly rather than accepting and ignoring it, given the flag is extracted before subcommand dispatch.
- [ ] 7.4 Add tests for the flat listing in both modes, for the type and usability columns, and for the rejected `--json`.

## 8. Companion CLI surface

- [ ] 8.1 Decide, and record in the change, whether the companion package API is exposed through the CLI or removed; the listing no longer draws the relation, so leaving it unreachable is a choice rather than an omission.
- [ ] 8.2 If exposed: add preset subcommands to assign and unassign a companion role and to report the installed artifacts admissible for a preset's criterion, selecting none.
- [ ] 8.3 If exposed: report companion assignments in `preset show`, in both output modes.
- [ ] 8.4 If exposed: settle the spelling of the override that assigns a companion failing its criterion, replacing the name currently advertised by a diagnostic but implemented nowhere.
- [ ] 8.5 If removed: delete the companion package API and its tests, and remove the companion requirement from the preset specification in this change.
- [ ] 8.6 Add tests for whichever path is taken.

## 9. Documentation and verification

- [ ] 9.1 Update `README.md` for the flat listing, repository-driven reconciliation, triage, the `--accept` restriction, and the removal of `--json` from reconciliation.
- [ ] 9.2 Run the Go build, vet, formatting check, and full test suite.
- [ ] 9.3 Validate the change with `openspec validate consistent-model-library --strict`.
- [ ] 9.4 Demonstrate against the operator's real library: list it and confirm every artifact reports a declared type and a usability verdict before anything is reconciled; reconcile one repository holding a principal and a projector and confirm both are reconciled in one action; confirm a repository holding two principals is refused as ambiguous; and run the triage pass and confirm it reports its size estimate, asks before hashing, and names a command for every case it leaves unresolved.
