# The Hub currently rejects Git-stored files above 10 MiB

**Kind:** finding · **State:** current · **Verified:** 2026-08-10

## The fact

Two independent Hub behaviours meet at 10 MiB and, for writes accepted under
the currently observed policy, make `lfs.sha256` structurally available for
an artifact large enough to be a model.

**Above the boundary, Git storage is refused.** The Hub runs a server-side
pre-receive hook that rejects any push containing a file larger than 10 MiB
that is not tracked by large-file storage. This is enforcement, not advice:
the client-side `.gitattributes` patterns and the documentation's "files
larger than 10MB *should* be tracked" prose are both weaker than what the
server actually does.

**Below it, the pointer carries a SHA-256.** The Xet upload protocol states
that computing the SHA-256 of the file contents "is required when uploading to
Git-based repositories on the Hugging Face Hub (models, datasets, Spaces)
because those repos use git LFS pointer files that reference the SHA256." Model
repositories are Git-based, so every file routed through LFS or Xet in one
carries a SHA-256 pointer — the value published as `lfs.sha256`.

Chained for a newly accepted write under this policy: **a file over 10 MiB in a
model repository cannot be stored directly in Git, must therefore go through
LFS or Xet, and therefore has a `lfs.sha256`.**

This is a strong observation about the current service, not a timeless format
invariant. The pre-receive implementation is not published as a normative
contract, historical objects and every alternative ingestion route were not
audited, and the service can change its policy. Consumers must continue to
model an absent `lfs` block rather than treating it as an impossible API shape.

## Measurement

494 repositories across the `gguf` and `text-generation` filters, every sibling
inspected at `main` with `?blobs=true`:

```text
repos scanned         494
.gguf siblings       5387    stored in Git rather than LFS:  0
Git-stored siblings  3848    of these, between 1 and 10 MiB: 346
largest Git-stored   9.740 MiB   MiniMaxAI/MiniMax-M2.7/model.safetensors.index.json
```

The distribution runs right up to the boundary — 346 files in the last decade
of it, the largest 9.740 MiB — and then stops completely. Together with the
quoted server rejection, that is strong corroboration of current enforcement.
The sample alone would not prove enforcement: an advisory threshold need not
produce spillover in any finite sample.

### Second measurement: the population least likely to comply voluntarily

The objection to the sweep above is correct and worth stating in full: a clean
sample is equally consistent with an advisory threshold that every publisher
happens to respect. Sample *size* cannot separate the two. Sample *composition*
can, so the second sweep deliberately targeted repositories where voluntary
compliance is least plausible — newest-created, least-downloaded and
most-recently-touched, across both models and datasets:

```text
repos scanned         480 (of 1056 queued; the rest hit API rate limiting)
Git-stored siblings 47304
largest Git-stored  9.9896 MiB   ~11 KiB below the 10 MiB line
above 10 MiB            0

size distribution   <1MiB 41991 | 1-5MiB 4968 | 5-9MiB 336 | 9-10MiB 9 | >10MiB 0
```

Two things make this harder to explain by compliance than by refusal. The
distribution is **truncated, not tapering**: files stack up to 9.9896 MiB and
then stop dead, which is what a byte-valued gate produces and not what a
population of independently careless publishers produces. And the file nearest
the line is named `…/gen_test_checkpoint4.pt.txt` — a PyTorch checkpoint with
`.txt` appended, which is what someone does to get *around* extension-based LFS
tracking. A publisher actively evading the tracking convention still landed
under the byte limit.

### What no sweep can show

Both measurements observe accepted writes. The API cannot show a rejected push,
so no amount of sweeping distinguishes "the server refuses" from "every client
declines to try"; only the reported rejection message speaks to the mechanism
itself. Neither sweep audits historical objects, private repositories, or
ingestion routes other than the ones sampled. The evidence supports **current
enforcement on observed routes**, at high confidence, and does not reach a
service contract.

## Why it matters

This converts the Q3 residue from a prevalence observation into an enforced
service invariant for newly accepted writes under the current policy. The
earlier claim on record was "no non-LFS GGUF was observed in 543 siblings",
which was correctly challenged as not proving impossibility. The boundary
argument is stronger and differently shaped:

- A newly accepted GGUF above 10 MiB is expected to expose `lfs.sha256` because
  of enforced storage routing rather than by luck of sampling.
- The case where `lfs.sha256` is absent is confined, by server enforcement, to
  artifacts of at most 10 MiB — below the size of any usable quantization,
  though a test fixture or a trivially small projector could land there.
- In that confined case the Git blob OID is the content identifier, published
  both as `blobId` and as `X-Linked-ETag`. It is genuinely the only reproducible
  identifier there, so it is not correct to say `blobId` is never the only
  evidence — only that it is never the only evidence *for an LFS-backed file*.

## Consequences

- Q3's ordinary path does not depend on sample size. For newly accepted
  artifacts above 10 MiB, a full-content SHA-256 is structurally available
  under the current service policy, so no weaker evidence is needed there.
- A design should still represent "no supported content identifier": the API
  shape permits an absent `lfs` block, the enforcement is not a normative
  forever-contract, and robust readers do not turn a current provider policy
  into an impossible state.
- The SHA-1 collision-resistance question stands or falls on its own merits. It
  is confined to ≤10 MiB Git-stored files, but confinement by size is a scope
  limit, not a cryptographic argument — collision resistance is a property of
  the algorithm and the threat model, not of the input length.

## Sources

- Xet upload protocol, SHA-256 required for Git-based Hub repositories:
  <https://github.com/huggingface/hub-docs/blob/main/docs/xet/upload-protocol.md>
- Hub repository guide, 10MB threshold and generated `.gitattributes`:
  <https://huggingface.co/docs/hub/en/repositories-getting-started>
- The pre-receive rejection is attested by its widely reproduced error message
  ("Your push was rejected because it contains files larger than 10 MiB")
  rather than by a specification sentence; it is corroborated here by the
  measured ceiling above. Marked as **enforced in observation**, not quoted
  from a normative document.
- Measurement: live Hub API sweep, 2026-08-10, script and counts above.
