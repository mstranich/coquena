# Request the remote content hash Hugging Face actually publishes

## Status

- **State:** open
- **Opened:** 2026-08-05
- **Owner:** project-owner
- **Priority:** high
- **Related RFC:** `model-artifact-attestation`
- **Candidate change:** `request-remote-content-hash`

## Problem

Online reconciliation of a locally cached artifact reports:

```text
partial: Hugging Face reported no content hash to compare against
```

The statement is literally true and causally misattributed. Hugging Face
publishes the content hash. Coquena does not ask for it, and would not read it
if it arrived.

Two independent defects are stacked:

**1. The query parameter is missing.** `internal/model/huggingface.go:92`
requests `GET /api/models/{repo}/revision/{rev}` with no parameters. That
endpoint returns a minimal `siblings` list. Verified against the operator's own
repository, the response contains **zero** occurrences of `"lfs"`. Blob detail
requires `?blobs=true`.

**2. The field name is wrong.** With `?blobs=true` the block appears, but the
hash is published under `lfs.sha256`. The response contains **zero**
occurrences of `"oid"`. The struct at `huggingface.go:30-32` parses `LFS.OID`,
so even with the parameter added it would still read an empty string.

## Witness

`lmstudio-community/gemma-4-12B-it-QAT-GGUF`, revision
`aaec3dd9d1012557147a627142759994d1fd8d37`, with `?blobs=true`:

```json
"rfilename": "gemma-4-12B-it-QAT-Q4_0.gguf",
"blobId": "01e97ac6aa548d894f07a1b77725603c1d6fae40",
"size": 6975879008,
"lfs": {
  "sha256": "f568ac5de71c8fcac5d5794494388ad94db9e18b4368ca897e21b30d2448eeec",
  "size": 6975879008,
  "pointerSize": 135
}
```

The reported `size` matches exactly the `sizeBytes=6975879008` Coquena logged
from its own local inspection of the same file. `lfs.sha256` is the SHA-256 of
the file contents and is directly comparable to the value `hashFile` computes
for `Artifact.SHA256`.

## Why downloaded artifacts were unaffected

Artifacts Coquena downloaded are attested by construction: the bytes were
fetched from a specific revision URL, so provenance does not depend on a
sibling hash. Only artifacts adopted from an existing local cache — the model
directory shared with another tool — need the remote hash to establish after
the fact that these bytes belong to that revision. That is why the operator's
downloaded models reconciled cleanly while the cached ones reported `partial`.

## Adjacent finding: repository renames

While verifying the above, two repositories in the operator's library returned
**HTTP 307** rather than data:

```text
deepreinforce-ai/Ornith-1.0-9B-GGUF   -> ornith-ai/Ornith-1.0-9B-GGUF
deepreinforce-ai/Ornith-1.0-35B-GGUF  -> ornith-ai/Ornith-1.0-35B-GGUF
```

Go's HTTP client follows redirects by default, so Coquena reaches the data.
But the sidecar records the requested name, which is no longer the canonical
one, and nothing reports the divergence. This is an identity question rather
than a hash question and is recorded against
`model-artifact-attestation` Q1/Q2. It may deserve its own backlog entry.

## Coverage check

With `?blobs=true` and redirects followed, every repository in the operator's
library publishes `lfs.sha256`:

```text
ornith-ai/Ornith-1.0-9B-GGUF                    7
ornith-ai/Ornith-1.0-35B-GGUF                   7
lmstudio-community/Qwen3.6-35B-A3B-GGUF         4
lmstudio-community/gemma-4-12B-it-QAT-GGUF      2
lmstudio-community/gemma-4-26B-A4B-it-QAT-GGUF  2
prism-ml/Bonsai-27B-gguf                        6
prism-ml/Ternary-Bonsai-27B-gguf                8
```

No repository in this sample required weaker evidence.

## Required outcome

Future work should:

- request blob metadata so the published hash is returned;
- parse `lfs.sha256`, and decide whether `lfs.size` and `blobId` are worth
  recording as corroboration;
- stop attributing a client omission to the remote service — the diagnostic
  must distinguish "the service published no hash" from "no hash was
  requested";
- re-evaluate which artifacts still reconcile as partial once the hash is
  available, since that set is the real input to
  `model-artifact-attestation` Q3.

## Acceptance questions

- Does `?blobs=true` change response size enough to matter for repositories
  with many siblings, and should the parameter be conditional?
- Do repositories migrated to Xet storage still publish `lfs.sha256`? Every
  repository sampled here did, but none was confirmed to be Xet-backed, so
  this remains unverified.
- Should `blobId` be recorded? It identifies the git blob rather than the
  content and is not comparable to a local file hash.
- Is the local hash still computed when a remote hash is unavailable, or is
  that work deferred?

## Scope boundary

This is a client correction. It does not define what attestation means, how
strength is recorded, or what `upgrade` requires — those belong to
`model-artifact-attestation` and `establish-sidecar-authority`. It should land
independently, and preferably first, because it changes which artifacts can
reach an attested state and therefore narrows what that RFC must decide.

## Relevant implementation areas

- `internal/model/huggingface.go`
- `internal/model/reconcile.go`
- `internal/model/reconcile_repository.go`
- `internal/model/check_updates.go`
