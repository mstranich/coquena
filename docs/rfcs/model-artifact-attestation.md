# RFC: Model artifact attestation semantics

**Status:** open
**Opened:** 2026-08-05
**Moderator:** project owner
**Derives from:** `gguf-model-artifact-identity` (closed 2026-08-03)
**Blocks:** `attest-partial-model-reconciliation`, `record-remote-drift-state`,
`support-model-upgrade`

---

## The question

**What does it mean for local model bytes to be attested as belonging to a
remote revision, how strong is that claim, and how is it recorded?**

That is the whole subject. Everything below either serves this question or
declares itself outside it.

## Exit criterion

This RFC is finished when the six questions in *Questions under investigation*
have owner-accepted answers, and those answers are specific enough that
`establish-sidecar-authority` can be written as an OpenSpec change without
further architectural discussion.

It is not finished when reviewers agree. It is not extended by finding new
problems; see *Process rules*.

## Process rules

These exist because the preceding RFC ran for a week and closed at
2,400 lines. The causes were identifiable and are addressed here directly.

1. **Reviewer declarations of closure are advisory.** Only the moderator closes
   this RFC. Reviewers should not declare consensus, sign off, or announce
   completion; they should answer the questions and state disagreement plainly.
2. **The question list is fixed at opening.** Anything discovered that is not
   an answer to one of the six questions goes to `docs/backlog/`, with a
   `relatedRFCs` entry pointing here. It does not get absorbed into this
   document.
3. **Out of scope is not re-litigated.** The list below is a moderator
   decision, not an opening position.
4. **Inherited decisions are inputs, not subjects.** See *Accepted inputs*.
   Reopening one requires new evidence, stated as such.
5. **Claims are verified against primary sources or the repository.** A claim
   that could not be verified is marked unverified rather than left to settle
   by repetition.

## Out of scope

- The GGUF evidence decisions settled by the preceding RFC: enum domains,
  parser traversal, block-scoped inference, guessed-bit handling, derived-block
  versioning, and the `verify --repair-derived` contract.
- Reference grammar and addressing. `user/repo[@revision][:selector]` is
  settled and implemented.
- Any bulk or `--all` operation. Refused by the moderator and not reopened.
- WebUI. Postponed by standing owner decision until explicitly resumed.
- Implementing `model upgrade`. This RFC defines the evidence it will require;
  the operation itself is `support-model-upgrade`.
- Filtering, quantization display, and the remaining backlog items unrelated to
  attestation.

## Accepted inputs

Not under discussion. Listed so answers can rely on them.

**The authority partition.** Each sidecar field has exactly one writer:

```text
Source, Artifact attestation   ->  download, reconcile --online
derived block                  ->  verify --repair-derived
drift                          ->  check-updates
ownership                      ->  reconcile --accept
```

**Command boundaries.** `verify` is local and read-only by default. `reconcile`
carries external authority and may transfer ownership. `check-updates` consults
the remote and, per moderator ruling, may record what it observed without
altering model bytes. No command silently selects among candidates.

**Attestation precedes derivation.** Derived evidence is never recomputed from
bytes that failed verification.

**Independent axes, not a state machine.** Attestation, ownership, integrity,
derivation and drift are orthogonal. An artifact may be attested and locally
modified at once. They are recorded per axis, and current state is derived from
the fields rather than replayed from a history.

## Witness

The locally cached Gemma 4 12B repository. Online reconciliation completed with
a diagnostic equivalent to:

```text
partial: Hugging Face reported no content hash to compare against
```

This is not a failure. Coquena hashed the local bytes and resolved the
repository's current revision, but had no remote content hash to compare
against. The sidecar can therefore record a resolved remote revision beside
local bytes that were never shown to belong to it.

An older local file with the same filename can be attributed to a newer
revision without byte-level evidence, and nothing in the record says so.

**Amended 2026-08-05.** The diagnostic is causally misattributed: see
*Verified findings*. Hugging Face publishes the hash; Coquena does not request
it. The representation defect below stands regardless, and is the part of this
witness that belongs to this RFC.

The defect the witness actually demonstrates is visible in the operator's
listing. Before reconciliation the artifact was `unreconciled`. After a
reconciliation that reported `partial` on every artifact, it became:

```text
installed  current  model  yes  ...@aaec3dd...:gemma-4-12B-it-QAT-Q4_0.gguf
```

Indistinguishable from artifacts that are genuinely attested. No column
records that the revision binding has no byte-level evidence behind it. **A
partial result is presented as a complete one**, and that is a representation
problem, not an evidence-availability problem.

## Questions under investigation

**Q1 — Which field is authoritative for local-byte identity?**
`Artifact.SHA256` is currently both the local integrity baseline and the value
compared against a remote LFS object ID. Those are two jobs. Whether one field
can hold both, or whether local baseline and remote attestation must separate,
is the first structural decision.

**Q2 — How is a partially reconciled artifact represented?**
Three candidates: preserve the prior attested revision and refuse the new one;
record an observed revision separately from an attested revision; or leave
revision identity unresolved. Each has a different failure mode when the
operator later runs `upgrade`.

**Q3 — What remote evidence is acceptable besides a remote content hash?**
**Amended 2026-08-05, premise corrected.** This question was opened on the
assumption that Hugging Face often exposes no content hash. It does expose one;
Coquena does not request it. See *Verified findings*. Every repository in the
operator's library publishes `lfs.sha256`.

What remains of the question is narrower: which artifacts still fail to reach
an attested state once the hash is requested correctly, and whether that
residue justifies accepting weaker evidence at all. Answer this **after**
`request-remote-content-hash` lands, not before — the residue is the input, and
it is currently unknown rather than large.

**Q4 — Does drift interact with attestation, and how?**
`check-updates` will record what it observed remotely. Whether that observation
can strengthen, weaken, or supersede an attestation — or is strictly parallel
to it — decides whether the two axes stay independent in practice or only on
paper.

**Q5 — Does operator ownership survive a later online reconciliation?**
An operator who accepts local bytes makes an assertion about trust, not a
derivation. If a later `reconcile --online` attests the same artifact, whether
the earlier acceptance is overwritten, retained as history, or reduced to a
single "was ever manually accepted" fact is unresolved.

**Q6 — What precondition must byte replacement enforce?**
`upgrade` replaces installed bytes. Which attestation states permit it to
start, and whether authority must exist before download or may be established
after a complete transfer, is the question that makes the preceding five
consequential rather than cosmetic.

## Verified findings

Findings established against primary sources or the repository, per process
rule 5. Each records what it changes.

### F1 — The remote content hash is available and unrequested

`internal/model/huggingface.go:92` requests
`GET /api/models/{repo}/revision/{rev}` with no parameters. Verified against
the witness repository, that response contains zero occurrences of `"lfs"`.
Blob detail requires `?blobs=true`.

With the parameter, the hash is published under `lfs.sha256`. The response
contains zero occurrences of `"oid"`, while `huggingface.go:30-32` parses
`LFS.OID` — so the field would read empty even once the parameter is added.

```json
"rfilename": "gemma-4-12B-it-QAT-Q4_0.gguf",
"size": 6975879008,
"lfs": { "sha256": "f568ac5de71c…eeec", "size": 6975879008, "pointerSize": 135 }
```

The reported size matches exactly the `sizeBytes` Coquena logged from its own
local inspection. `lfs.sha256` is the SHA-256 of the file contents, directly
comparable to `Artifact.SHA256`.

**Changes Q3**, whose premise is corrected above. Owned by
`request-remote-content-hash`, which should land first.

### F2 — Downloaded artifacts are attested by construction

Bytes Coquena downloaded came from a specific revision URL, so their provenance
never depended on a sibling hash. Only artifacts adopted from an existing local
cache require the remote hash to establish after the fact that these bytes
belong to that revision.

**Relevant to Q1 and Q6**: attestation has at least two sources of differing
character — established during transfer, or reconstructed afterward by
comparison — and the schema must be able to say which one a given artifact has.

### F3 — Repository renames diverge silently from recorded identity

Two repositories in the operator's library return HTTP 307:

```text
deepreinforce-ai/Ornith-1.0-9B-GGUF   -> ornith-ai/Ornith-1.0-9B-GGUF
deepreinforce-ai/Ornith-1.0-35B-GGUF  -> ornith-ai/Ornith-1.0-35B-GGUF
```

Go's HTTP client follows redirects, so Coquena reaches the data and nothing
appears to be wrong. But the sidecar records the requested name, which is no
longer canonical, and no field distinguishes the two.

**Relevant to Q1 and Q2**: an attestation binds bytes to a revision *of a
repository*. If the repository identity itself can drift without notice, the
attested revision inherits that ambiguity. Whether the sidecar records the
requested name, the canonical name, or both is unresolved and belongs to this
RFC rather than to the client fix.

## What a resolution must produce

- A field-level answer to Q1 through Q5, sufficient to write the sidecar
  schema.
- The CLI vocabulary distinguishing at least `verified`, `partial`, and
  `unattested`, and where each surfaces.
- A stated precondition for Q6 that `support-model-upgrade` can consume.
- For each answer, whether it is a decision or a deferral, and if deferred,
  which backlog item owns it.

## Reviews

Reviewers append below. State standing at the top of each entry. Answer
questions by number. Do not declare the RFC closed.
