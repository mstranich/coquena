## 1. Fail instead of degrading a classification

- [x] 1.1 Change `deriveFile` in `internal/model/library.go` to return `(Derivation, error)`, returning the open and scan errors instead of a `TypeUnknown` derivation
- [x] 1.2 Propagate the error through `Publish` so a completed download that cannot be classified publishes no sidecar, with a test asserting no `.json` and no `.revision` is left behind
- [x] 1.3 Propagate the error through `PublishInPlace` so import and reconcile write nothing when classification fails, with a test per caller
- [x] 1.4 Fail the derivation repair in `internal/model/verify.go` and leave the recorded derived evidence as it was, with a test asserting the derivation is unchanged and the verification that ran is still recorded
- [x] 1.5 Fail the artifact in `internal/model/reconcile_repository.go` when `InspectRemote` errors, instead of substituting `Classification{Type: TypeUnknown}`, with a test asserting the artifact is reported failed and not dropped from the target set
- [x] 1.6 Leave `classifyUnreconciled` best-effort and add a test that an unreadable file is still enumerated rather than failing the walk

## 2. Derive the seven states

- [x] 2.1 Add `ArtifactState` with the seven constants in `internal/model/types.go`
- [x] 2.2 Implement `stateOf` as the diagram's ordered cascade, with the `incomplete` gate present, its predicate constantly false, and a comment naming `address-quantization-bundles`
- [x] 2.3 Test the ordering directly: an altered artifact that also carries an attestation reports `altered`, and an artifact with no sidecar reports `untracked` without consulting a verification
- [x] 2.4 Test `local`, `detached`, `synced` and `upgradable` against records built on the evidence axes, including `upgradable` from an attestation and observation naming different revisions
- [x] 2.5 Test that deriving states over a multi-artifact library reads no artifact bytes

## 3. Separate the job status from the artifact state

- [x] 3.1 Add `JobStatus` to `LibraryEntry` and change `State` to `ArtifactState`
- [x] 3.2 Fill `JobStatus` for in-flight jobs in `Manager.Library` and leave their artifact state empty
- [x] 3.3 Derive `State` through `stateOf` for every enumerated file on disk
- [x] 3.4 Test that a library holding one in-flight job and one artifact on disk reports the job's status and the artifact's state in separate fields

## 4. Derive usability from the state alone

- [x] 4.1 Delete `UnusableReason` and the `Reason` and `Detail` fields from `Usability`, keeping `Advice`
- [x] 4.2 Rewrite `usabilityOf` as the set membership test over the four usable states
- [x] 4.3 Replace the `entry.State != "installed"` gate in `installedPrincipals` with the usable-state test
- [x] 4.4 Replace the same gate in `internal/presets/companion.go`
- [x] 4.5 Update the usability tests to assert eligibility per state, including that `detached` and an unattested artifact are usable and that `untracked` and `altered` are not

## 5. Retire the old strings from the remaining consumers

- [x] 5.1 Replace the state comparisons in `internal/model/offline.go` so reference resolution selects on usable states rather than `"installed"`
- [x] 5.2 Replace the state switch in `internal/model/reconcile.go`
- [x] 5.3 Replace the remaining comparisons and the `RepositoryOutcome` value in `internal/model/reconcile_repository.go`
- [x] 5.4 Grep the tree for `"installed"` and `"unreconciled"` and confirm no occurrence remains outside archived documents

## 6. Split reconcile into import and reconcile

- [x] 6.1 Add a `model import <reference>` handler that adopts local bytes with no network request, refusing `--json` and any whole-library option
- [x] 6.2 Reduce `model reconcile <reference>` to the online path with no mode flags, leaving an unmatched artifact `detached`
- [x] 6.3 Delete the `--accept` and `--online` flags and the usage error for supplying neither or both
- [x] 6.4 Test each command end to end, including that `reconcile` never adopts an artifact as `local` when no match is established

## 7. Finish the command surface

- [x] 7.1 Rename `check-updates` to `update`, leaving its behaviour unchanged
- [x] 7.2 Remove the `--with-companions` flag and the `companionFlag` constant, always enqueuing the identified companions, and reject the flag as a usage error
- [x] 7.3 Remove the `resolve` and `revision` subcommands from the dispatcher
- [x] 7.4 Reject `--json` on `download`, `import`, `reconcile` and `verify` as a usage error, keeping it on `discover`, `list`, `update` and the download-job query subcommands
- [x] 7.5 Test that `--json` on each acting command exits unsuccessfully, creates no job and writes no sidecar
- [x] 7.6 Confirm `download` reports every created job identity on standard output one per line, with a test covering a bundle of several artifacts
- [x] 7.7 Rewrite `modelUsage` to the surviving surface and its `--json` division, naming no removed spelling and offering no replacement hint
- [x] 7.8 Test that each removed spelling exits unsuccessfully with usage and changes no state

## 8. Render the new vocabulary

- [x] 8.1 Replace `statePriority` with the seven-state ordering and update the sort
- [x] 8.2 Remove the two-vocabulary filter from `runModelList`
- [x] 8.3 Render the STATE and USABLE columns from the derived state and drop the reason from human output
- [x] 8.4 Update the JSON entry shape: state name, usable, advice, job status, no reason
- [x] 8.5 Test that human and JSON output report the same state names for the same library

## 9. Close the change

- [x] 9.1 Rewrite `docs/model-commands.md` for the new surface
- [x] 9.2 Run `task verify` and fix what it reports
- [x] 9.3 Close `adopt-artifact-state-vocabulary` and `align-model-command-surface` in `docs/registry.yaml`, moving both entries under `docs/backlog/archive/`, and re-point the command-surface entry's `produces` at this change
- [x] 9.4 Run `task docs:validate` and confirm it exits zero
