## 1. Unblock the end-to-end flow

The operator flow this change exists for — discover a repository, read which
quantizations it offers, download one by its logical name, see it listed, and
bind it to a preset — depends only on this section. Everything after it serves
artifacts Coquena did not download. This section comes first so the flow is
usable before the rest lands.

- [x] 1.1 Stop printing the repository model card from `coquena model discover`, whose length buries the candidate list the command exists to present.
- [x] 1.2 Leave `Resolver.ModelCard` and the discovery field carrying it in place, unreferenced, with a comment recording that the output was deliberately turned off pending a later change that reintroduces it behind an explicit option.
- [x] 1.3 Remove the tree nesting from `coquena model list` in both output modes, reporting every artifact at the same level with its declared type.
- [x] 1.4 Add tests for discovery reporting the candidates without the model card, and for the flat listing in both output modes.
- [x] 1.5 Verify the whole flow end to end against a stubbed Hugging Face repository, using the existing test transport rather than a real multi-gigabyte transfer: discover a repository holding several quantizations and a projector, download one quantization by `user/repo:QUANT`, confirm the listing reports it with its declared type, and bind a preset to it by the same reference. Report any step that does not work rather than adjusting the test to pass.

## 2. Metadata reading without array payloads

- [x] 2.1 Add a metadata read that decodes scalar values and skips array payloads instead of materializing them, computing the span for fixed-width element types and walking length prefixes for string arrays without allocating.
- [x] 2.2 Keep the existing full read for the paths that need array contents, so the tokenizer vocabulary digest computed at reconciliation time is unchanged.
- [x] 2.3 Add tests confirming the skipping read returns the same scalar keys as the full read for a real-shaped header, and that a malformed array still fails rather than being silently skipped.

## 3. Classification at library enumeration

- [x] 3.1 Classify every artifact when `Manager.Library` enumerates the library: from the sidecar when one records a classification, from the artifact's own header through the skipping read when none does.
- [x] 3.2 Confirm that classifying an unreconciled artifact grants it no standing: it stays `unreconciled`, stays excluded from `ResolveInstalled`, and no file is written.
- [x] 3.3 Add tests for an unreconciled artifact reporting its declared type, for an installed artifact still reporting the sidecar's classification, and for enumeration writing nothing.

## 4. Usability

- [x] 4.1 Derive usability when the library is read: reconciled and typed is usable; a projector is usable only when a compatible installed principal exists; an unreconciled or unclassifiable artifact is unusable.
- [x] 4.2 Report a reason alongside an unusable artifact, distinguishing an unreconciled artifact from an orphaned projector from an unclassifiable one.
- [x] 4.3 Confirm a principal with no installed projector stays usable.
- [x] 4.4 Report usability in `coquena model list` in both output modes.
- [x] 4.5 Add tests for each usability outcome and for an orphaned projector becoming usable once a compatible principal is installed, with no sidecar rewritten.

## 5. Repository-driven reconciliation

- [x] 5.1 Reconcile the artifacts of one repository in a single action: resolve the repository, enumerate its remote candidates with their classification without downloading any complete artifact, and match them against the artifacts on disk by filename corroborated by metadata.
- [x] 5.2 Reconcile a principal together with its companions when a reference names a repository holding both.
- [x] 5.3 Refuse as ambiguous a reference matching more than one principal, naming the candidates.
- [x] 5.4 Report a remote candidate that is absent locally rather than omitting it.
- [x] 5.5 Refuse an artifact whose computed digest disagrees with the reported content hash, with a diagnostic that says the artifact may be from an earlier revision rather than asserting corruption.
- [x] 5.6 Keep `--accept` operating on exactly one referenced artifact, persisting a `local`-provider sidecar with no network request.
- [x] 5.7 Add tests for a repository holding a principal and a projector, a repository holding two principals, a companion absent locally, a digest disagreement, and a local artifact matching no remote sibling.

## 6. Turn off library-wide reconciliation

- [x] 6.1 Stop accepting `--all` on `coquena model reconcile`, reporting that the option is not accepted rather than ignoring it, and remove it from the usage text.
- [x] 6.2 Leave `Manager.ReconcileAll` and its result types in place, unreferenced, with the same comment convention as 1.2.
- [x] 6.3 Reject `--json` on reconciliation explicitly rather than accepting and ignoring it, given the flag is extracted before subcommand dispatch.
- [x] 6.4 Add tests for the refused `--all` and the refused `--json`.

## 7. Companion CLI surface

- [ ] 7.1 Decide, and record in the change, whether the companion package API is exposed through the CLI or removed; the listing no longer draws the relation, so leaving it unreachable is a choice rather than an omission.
- [ ] 7.2 If exposed: add preset subcommands to assign and unassign a companion role and to report the installed artifacts admissible for a preset's criterion, selecting none.
- [ ] 7.3 If exposed: report companion assignments in `preset show`, in both output modes, and settle the spelling of the override currently advertised by a diagnostic but implemented nowhere.
- [ ] 7.4 If removed: delete the companion package API and its tests, and drop the companion requirement from the preset specification in this change.
- [ ] 7.5 Add tests for whichever path is taken.

## 8. Documentation and verification

- [ ] 8.1 Update `README.md` for the flat listing, repository-driven reconciliation, the removal of `--all` and of `--json` from reconciliation, and discovery no longer printing the model card.
- [ ] 8.2 Run the Go build, vet, formatting check, and full test suite.
- [ ] 8.3 Validate the change with `openspec validate consistent-model-library --strict`.
- [ ] 8.4 Demonstrate the operator flow against the real Hugging Face and the operator's own library: discover a repository and read its quantizations; download one by `user/repo:QUANT`; confirm the listing reports it with its type and usability; bind a preset to it by the same reference; then reconcile a repository written by another application and confirm its principal and projector are reconciled in one action.
