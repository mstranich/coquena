# Tasks — establish-sidecar-authority

Groups 1–3 restructure the record, 4–7 give each axis its single writer, 8
derives the readings, 9 renders them.

**Groups 1–3 are one breaking step and the suite will be red across them.**
Replacing `Artifact.SHA256` invalidates ~49 call sites in 14 test files, and
`SchemaVersion` appears in 21 more; there is no ordering that avoids it, and
staging it behind a deprecated accessor was considered and rejected in the
design. Land 1–3 together, repair the suite in that same commit, and expect
groups 4 onward to keep the tree green individually.

## 1. Self-describing digests

- [x] 1.1 Add `Digest{Role, Algorithm, Domain, Value, ComputedAt}` to
  `internal/model/types.go` with the `integrity` / `repository-match` roles and
  the `raw-file` / `git-blob` / `xet-cas` domains.
- [x] 1.2 Replace `Artifact.SHA256` with a digest collection; add an accessor
  returning the single digest for a `(role, algorithm, domain)` tuple and whether
  one exists, so no caller iterates.
- [x] 1.3 Accept a `repository-match` digest as evidence when Coquena can
  reproduce it locally: SHA-256 over `raw-file` and SHA-1 over `git-blob`. Record
  a digest in any other domain without treating it as evidence and without
  invalidating the sidecar.
- [x] 1.4 Implement the Git blob construction — `sha1("blob " + length + NUL +
  bytes)` — so a `git-blob` identifier can be reproduced from the local file.
  Test it against a known blob OID.
- [x] 1.5 Reject a record carrying two digests for one tuple as an invalid
  sidecar. Test the duplicate, the valid two-role case, and a `xet-cas` digest
  that is recorded but not accepted.

## 2. The five evidence axes

- [x] 2.1 Add `Local{Digest, ComputedAt}` and `Verification{Outcome, CheckedAt,
  AgainstDigest}` to the record.
- [x] 2.2 Add `Observation{Repository, RequestedRevision, ResolvedCommit,
  ArtifactPath, PublishedDigest, ObservedAt}`, retaining only the latest.
- [x] 2.3 Add `Attestation{Repository, ResolvedCommit, ArtifactPath, Digest,
  Method, VerifiedAt}` with methods `download` and `remote-comparison`.
- [x] 2.4 Add `Ownership{Accepted, AcceptedAt}`.
- [x] 2.5 Test that writing an observation leaves an existing attestation and an
  existing ownership assertion byte-identical.

## 3. Schema 4 read and write

- [x] 3.1 Bump `RecordSchemaVersion` to 4 in `types.go`.
- [x] 3.2 Marshal schema 4 in `record_json.go` — digests as self-describing
  entries, each axis as its own object — preserving the existing unknown-key
  round-trip through `Record.Extra`.
- [x] 3.3 Delete the preceding-schema read path. Any version other than 4 reads
  as invalid, with no field admitted, translated or migrated. Update every test
  asserting that an earlier schema is accepted.
- [x] 3.4 Treat a schema-4 sidecar carrying no `integrity` digest as invalid, and
  test it alongside the wrong-version case so the two share one recovery path.
- [x] 3.5 Assert that an invalid sidecar leaves the GGUF untouched, and that
  reconciling or importing the artifact writes a current sidecar without
  re-downloading anything.

## 4. ETag off the record

- [x] 4.1 Move `ETag` from `Artifact` to `Candidate` as a `json:"-"` transport
  field beside `ContentSHA256`; update `huggingface.go:143` and `manager.go:68`.
- [x] 4.2 Update `manager.go:108` to take the job's `ETag` from the candidate, and
  assert with a test that `If-Range` resume still sends it.
- [x] 4.3 Assert no sidecar written by any path contains an `etag` key, and that
  no digest of any role is ever populated from a transport header.

## 5. The verification axis, written only by `verify`

- [x] 5.1 Make `VerifyWithOptions` persist its outcome, the time, and the digest
  it compared against, writing that axis and nothing else on the record.
- [x] 5.2 Ignore a verification record naming a digest the sidecar no longer
  carries; the artifact reads unverified.
- [x] 5.3 Remove `VerifyNoBaseline` and every branch reading it. A valid sidecar
  always carries an `integrity` digest, so the outcome is unreachable; verify
  returns matched or mismatched only.
- [x] 5.4 Test that `reconcile` recording the same `integrity` digest leaves the
  verification record byte-identical, and that recording a different one retires
  it (unverified, not altered).

## 6. Attestation on the download path

- [x] 6.1 Record the computed digest as the `integrity` digest at
  `manager.go:479`.
- [x] 6.2 Record an attestation with `method: download`, bound to the job's
  repository, resolved revision and artifact path — the bytes came from a
  revision-pinned URL, so provenance follows from the transfer (RFC F2).
- [x] 6.3 Record the observation the resolution already made, including
  `Candidate.ContentSHA256` as the published digest when one was supplied.
- [x] 6.4 Test that a completed download reads as `verified` with method
  `download`, and that no comparison is asserted that was not performed.

## 7. Attestation and ownership on the reconcile path

- [x] 7.1 In `reconcileAgainst`, record an attestation with
  `method: remote-comparison` plus both digest roles when the comparison agreed.
- [x] 7.2 When no identifier in an accepted domain is available, record the
  observation and leave attestation unset — the sidecar is still written (RFC A3).
- [x] 7.3 Drop `ReconcileResult.Partial` in favour of reading the standing back
  off the record; update `BulkReconcileEntry.Partial` and
  `reconcile_repository.go` accordingly.
- [x] 7.4 Have `ReconcileAccept` record `Ownership{Accepted: true, AcceptedAt}`
  and no observation, so "never consulted" stays distinguishable from "consulted,
  nothing established".
- [x] 7.5 Replace the abort at `reconcile.go:73` with a recorded disagreement:
  persist the observation including the identifier that disagreed, leave
  attestation unset, and keep the existing diagnostic's framing that the artifact
  may belong to an earlier revision (RFC A3, A4).
- [x] 7.6 Test that reconciling an accepted artifact online preserves its
  ownership assertion, and that a disagreement leaves an existing attestation,
  ownership assertion and `integrity` digest byte-identical while recording the
  observation.

- [x] 7.7 Stop re-baselining the integrity digest on an unproven change. When a
  recorded `integrity` digest exists and the freshly computed one differs: if the
  repository comparison succeeded, replacing it is justified because the new
  bytes are proven; if the comparison failed or no acceptable evidence was
  available, keep the recorded digest, leave attestation unset, and surface the
  difference in the diagnostic. Overwriting in the unproven case destroys the
  only evidence that the bytes changed.

## 8. Derived readings, without artifact I/O

- [x] 8.0 Add `AppliedAttestation() (Attestation, bool)` to `types.go`, mirroring
  `AppliedVerification`: an attestation whose bound digest is not among the
  record's digests does not apply. Route every reader of `Record.Attestation`
  through it — the `verified` / `partial` / `unattested` derivation included.
- [x] 8.1 Derive `verified` / `partial` / `unattested` from the axes: attestation
  present, observation only, neither.
- [x] 8.2 Derive the altered reading from the verification record alone; assert
  with a test that no `hashFile` call occurs during `Library()`.
- [x] 8.3 Derive verification staleness by comparing the file's modification time
  against `CheckedAt`, using the stat enumeration already performs, and never let
  staleness change the recorded outcome.
- [x] 8.4 Add `ReasonAltered` to `usability.go` and gate on it before the
  unreconciled case, since an altered artifact needs a different remedy.
- [x] 8.5 Retire `ReasonOrphanedProjector` as a reason for unusability
  (`usability.go:109`): a companion is usable whatever principals are installed.
  Keep `hasCompatiblePrincipal`, which preset binding still needs, and surface
  its result as advice rather than as a verdict (RFC A6).
- [x] 8.6 Test that `partial` and `unattested` artifacts stay usable, that a
  never-verified artifact stays usable, and that a projector imported before any
  compatible principal is usable immediately.

## 9. What the operator sees

- [x] 9.1 Render the verification outcome, its time and its staleness in `coquena
  model verify` output.
- [x] 9.2 Render `verified` / `partial` / `unattested` wherever a single artifact
  is inspected, with the attestation method when verified, phrased as a standing
  rather than a failure.
- [x] 9.3 Carry both in structured output as values a caller can branch on, and
  leave the listing's `installed` / `unreconciled` column untouched — the
  vocabulary is `adopt-artifact-state-vocabulary`.
- [x] 9.4 Confirm an altered artifact is reported unusable in the listing with a
  reason distinguishing it from an unreconciled one.

## 10. Close out

- [x] 10.1 `task verify` green, including `task docs:validate`, and
  `openspec validate establish-sidecar-authority --strict` clean.
- [x] 10.2 Close `record-verify-outcome-in-sidecar`, `distinguish-unattested-artifacts`,
  `remove-etag-from-sidecar` and `treat-orphaned-companion-as-usable` in
  `docs/registry.yaml`, moving each into `docs/backlog/archive/`. The last is
  closed here because this change rewrites the usability requirement anyway;
  re-point its `produces` from `change:adopt-artifact-state-vocabulary` to this
  change.
- [x] 10.3 Re-point the `candidateChanges` of the entries this change did not
  cover — `adopt-artifact-state-vocabulary` and `record-canonical-repository-name` —
  at their own changes, since they no longer land inside this one.
- [x] 10.4 Bring `distinguish-unattested-artifacts` up to date before archiving
  it: its "RFC resolution consumed" section still records SHA-256-only and the
  pre-amendment state names, and its acceptance questions were answered by the
  ruling. Archiving it as written would file a contradiction.
- [x] 10.5 Confirm `verify-download-against-remote-digest` stays open and records
  that it strengthens a `method: download` attestation into a compared one,
  rather than creating the attestation.
