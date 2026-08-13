# A publisher can permanently destroy the history an attestation referred to

**Kind:** finding · **State:** current · **Verified:** 2026-08-11

## The fact

A Hugging Face repository is a Git repository, but its history is not
append-only from a consumer's point of view. The Hub documents three distinct
operations by which a publisher permanently removes content that a consumer may
already hold locally:

- **Super-squash.** *"The super-squash operation compresses your entire Git
  history into a single commit… This is a destructive operation that cannot be
  undone, commit history will be permanently lost and **LFS file history will be
  removed**."* It is offered as the recommended way to reclaim storage from old
  LFS versions.
- **Deleting individual LFS files**, from the repository's Settings → Storage →
  "List LFS files".
- **Deleting pull-request refs**, which removes the commits a PR branch held.

All three are ordinary maintenance actions, not abuse. The storage-limits page
presents super-squash as good practice for a repository whose history has grown.

## Why it matters

A locally held artifact can become permanently unmatchable against its own
source repository, with no fault on either side and no corruption of the local
bytes:

1. Coquena downloads an artifact at commit `A` and records the evidence.
2. The publisher pushes a new quantization and later super-squashes.
3. Commit `A` no longer exists and its LFS object is gone.
4. The local bytes are intact, but no revision of the repository will ever again
   publish a content hash equal to theirs.

This is the mechanism behind an otherwise puzzling case: an artifact that
matches no remote revision is not necessarily corrupt, and not necessarily an
older version that can be found by searching history — the version it belongs to
may have been deleted.

It also means **"cannot be re-attested" is not evidence of anything being
wrong**. A consumer that re-derives attestation on demand, rather than retaining
what it once established, will silently downgrade healthy artifacts as
publishers do routine housekeeping.

## Consequences

- Retaining a past attestation rather than recomputing it is not merely an
  efficiency choice; it preserves a fact that can become unverifiable through
  no defect. This corroborates the `model-artifact-attestation` ruling that a
  later observation never erases an older valid attestation.
- Searching a repository's history for a revision whose hash matches local
  bytes is a *best-effort* diagnostic, never a decision procedure. A negative
  result is inconclusive, because the matching revision may have been destroyed.
- "Local bytes match no available remote revision" therefore has at least three
  causes that must not be collapsed into one word: the bytes are from an older
  revision that still exists; from a revision that was destroyed; or genuinely
  not from this repository at all.
- A drift or update check must not treat a vanished source revision as a reason
  to alter or distrust local bytes.

## Sources

- Hub storage limits — super-squash, LFS file deletion and PR-ref deletion,
  with the quoted destructive-operation warning:
  <https://huggingface.co/docs/hub/en/storage-limits>
- `HfApi.super_squash_history` reference:
  <https://huggingface.co/docs/huggingface_hub/main/en/package_reference/hf_api#huggingface_hub.HfApi.super_squash_history>
