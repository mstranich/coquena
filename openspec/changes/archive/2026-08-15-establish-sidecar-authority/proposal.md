## Why

The sidecar records one digest, `Artifact.SHA256`, and treats it as though it
answered every question that can be asked about an artifact's bytes. It does
not. The closed RFC [`model-artifact-attestation`](../../../docs/rfcs/archive/model-artifact-attestation.md)
settled, over nine review passes, that the sidecar needs several **independent
evidence axes** and that nothing today separates them. Its exit criterion was a
field-level answer sufficient to write the schema; this change writes it.

What is broken now:

- **`verify` throws its answer away.** It hashes the file, prints `matched` or
  `mismatched`, and writes nothing. `hashFile` is called in exactly three places
  — download, `verify` and `reconcile` — and the listing never hashes. So the
  `altered` state is underivable in practice: either the listing lies by omission
  about a damaged artifact, or listing a library becomes gigabytes of reads per
  invocation. RFC amendment A2; tracked as `record-verify-outcome-in-sidecar`.
- **An unattested artifact is indistinguishable from an attested one.** When
  `reconcile --online` resolves a revision but cannot compare bytes, the result is
  `partial` — a boolean on one run, printed once and discarded. The RFC requires
  a CLI vocabulary distinguishing `verified`, `partial` and `unattested`, and
  none exists. Tracked as `distinguish-unattested-artifacts`.
- **An observation is confused with an attestation.** Resolving a repository's
  current revision says where the followed channel points; it does not say the
  local bytes belong to that revision. The RFC ruling (Q2) keeps them on separate
  axes precisely so a later observation cannot launder remote recency into byte
  provenance.
- **`Artifact.ETag` holds a value whose meaning changes between hops.** Coquena
  HEADs with a redirect-following client and keeps the ETag off the *last* hop,
  which for a Hugging Face artifact is the Xet CAS hash — a content identifier in
  a domain Coquena cannot reproduce, stored under a field name suggesting an
  ordinary HTTP cache validator. The ruling (Q1) removes it from the authority
  model outright. Tracked as `remove-etag-from-sidecar`.

The common thread is that the sidecar has no notion of **which command owns which
field**, and no field declares what its value means. This change establishes that
partition. Seven backlog entries name it as their prerequisite; it is the single
largest unblocker in the backlog.

## What Changes

**BREAKING (sidecar schema 3 → 4), with no compatibility path.** `Artifact` stops
being a flat bag and becomes the evidence axes the ruling defines, and **only the
current schema is read**. A sidecar at any earlier version is invalid; its
artifact falls to the recovery path that already exists for an artifact without
one, and `reconcile` or `import` restores it.

Coquena has not been released. There is no installed base to migrate, so
carrying a preceding-schema reader would buy nothing and cost complexity in the
reader — the component where a wrong branch is most expensive. Recovery is
non-destructive: model bytes are never touched, and re-establishing a sidecar
costs a local hash and one metadata request.

That decision also removes a case rather than adding one. With no legacy shape to
admit, **a valid sidecar always carries an `integrity` digest**, because writing
one requires computing it. "Managed artifact whose integrity cannot be checked"
stops being a state the schema can express, which is why `VerifyNoBaseline`
disappears instead of being carried forward.

- **Digests become self-describing.** Each declares its **role** (`integrity` —
  Coquena hashed the local file; `repository-match` — the repository published
  it), its **algorithm**, and its **domain** (`raw-file`, `git-blob`, `xet-cas`).
- **Evidence is accepted by reproducibility, not by algorithm.** Q1 accepted
  SHA-256 over `raw-file` and nothing else; **A1 supersedes that**. A published
  identifier is accepted as `repository-match` evidence when Coquena can
  reproduce it from the complete local bytes. Today that is SHA-256 over
  `raw-file` and SHA-1 over `git-blob`, which between them cover every artifact
  the Hub serves: files above 10 MiB cannot be stored in Git and therefore carry
  `lfs.sha256`, and smaller ones carry a Git blob OID. `xet-cas` is recorded and
  not accepted — reproducible in principle, but Coquena does not implement the
  chunker and gains nothing by doing so.
- **Five independent axes**, none derived from another:
  1. *local* — the integrity digest and when it was computed;
  2. *verification* — what `verify` last found and when;
  3. *observation* — the latest remote repository, requested revision, resolved
     commit, artifact path and published identifier, and when it was seen;
  4. *attestation* — established once, binding repository, resolved commit,
     artifact path and digest, with its `method` and `verifiedAt`;
  5. *ownership* — the operator's `accepted` assertion and `acceptedAt`.
- **`verify` persists what it found**, and is the only writer of that axis.
- **The listing derives `altered` from the record, never from a fresh hash.** An
  artifact whose last `verify` found a mismatch is reported unusable.
- **Attestation records how it was established.** `method: download` for bytes
  fetched from a revision-pinned URL, which are attested by construction (RFC
  F2); `method: remote-comparison` for bytes reconstructed after the fact by
  comparing a locally computed SHA-256 against the repository's `lfs.sha256`.
- **A new observation never erases an existing attestation** (Q2), and reconciling
  online never erases the operator's ownership assertion (Q5).
- **Reconciliation that establishes no match still writes a sidecar** (A3) and
  records the observation with attestation left unset. This includes a
  comparison that *disagreed*: A4 removed `diverged` precisely because the three
  causes of a non-match — the repository could not be consulted, the bytes belong
  to a revision that no longer exists, or they are not from that repository — are
  indistinguishable from the artifact. Today `reconcileAgainst` aborts on
  disagreement and writes nothing, which discards what was learned and leaves the
  operator with an error instead of a working model.
- **A companion without an installed principal is usable** (A6). `usable` means
  eligible to be referenced by a preset, and which principal a projector
  accompanies is the preset's decision, not an accident of what the library
  happens to hold. The current rule reports it unusable.
- **`Artifact.ETag` leaves the record.** It is not promoted to a digest role and
  no derivation reads it. `Job.ETag` is untouched: there it is live transport
  state for a resumable download, not persisted evidence.
- **`verified` / `partial` / `unattested`** enter the CLI vocabulary, satisfying
  the resolution requirement the RFC left to its consuming change.

### Non-goals

- **The seven-state vocabulary is not adopted here.** The listing keeps reporting
  `installed` / `unreconciled`. Renaming those strings is load-bearing across
  sorting, filtering and usability gating, and belongs to
  `adopt-artifact-state-vocabulary` (RFC A5) as its own reviewable step. This
  change only makes the evidence those states need *derivable*.
- **No command is renamed or split.** `import`, `reconcile`, `update` and
  `upgrade` are `align-model-command-surface` (RFC A7).
- **Nothing writes the observation axis from an update check.** The axis is
  defined and reconciliation populates what it saw; polling for remote change is
  `detect-model-updates`, which this change unblocks.
- **`upgrade` and `--force` are not implemented.** Q6's preconditions are
  consumed by `support-model-upgrade`.
- **Repository identity is not settled.** Whether the sidecar records the
  requested or the canonical repository name is RFC F3, which no review answered
  and the ruling did not settle; it is `record-canonical-repository-name`. The
  observation axis records the repository as requested today, which is what the
  code already does — this change adds no new commitment to it.
- **No new listing column.** The attestation distinction surfaces through
  `verify` output and structured output. It deliberately does *not* surface
  through usability: an unattested artifact is usable, so usability cannot carry
  the distinction. The listing gains it with the state vocabulary.

## Capabilities

### New Capabilities

None. This change restructures evidence an existing capability already claims to
persist.

### Modified Capabilities

- `gguf-model-library`: the sidecar requirement gains digest roles and the five
  evidence axes, and loses the ETag field. *Report whether an artifact is usable*
  is **removed** and replaced by *Report whether an artifact is eligible for a
  preset* — the question it answers changed, so modifying it in place would have
  understated the change and silently dropped a scenario the tooling correctly
  refused to lose.

## Impact

- `internal/model/types.go` — `Artifact` restructured; `RecordSchemaVersion` → 4.
- `internal/model/record_json.go` — schema 4 marshal/unmarshal only; the
  preceding-schema read path is deleted. Unknown-key preservation via `Extra` is
  retained, but as protection for hand-written annotations rather than as a
  version bridge.
- `internal/model/verify.go` — writes its outcome; `VerifyNoBaseline` is removed,
  since a valid sidecar always carries an `integrity` digest.
- `internal/model/reconcile.go`, `reconcile_repository.go` — persist observation,
  attestation and ownership in place of the transient `Partial` boolean.
- `internal/model/manager.go` — download records an attestation with
  `method: download`, plus the integrity digest it already computes.
- `internal/model/usability.go` — new unusable reason for an altered artifact.
- `internal/model/huggingface.go` — stop propagating the last-hop ETag into the
  record; carry the published identifier with its domain.
- `cmd/coquena/model.go` — render `verified` / `partial` / `unattested` and the
  verification record.
- Existing sidecars on disk become invalid and are re-established by `reconcile`
  or `import`. No migration code is written, and no model file is re-downloaded.
