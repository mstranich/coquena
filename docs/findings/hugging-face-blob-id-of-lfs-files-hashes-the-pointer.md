# Hugging Face `blobId` hashes the LFS pointer, not the model bytes

**Kind:** finding · **State:** current · **Verified:** 2026-08-10

## The fact

For a sibling stored in Git LFS, `blobId` is the Git object ID of the **135-byte
LFS pointer file**, not of the artifact's content. Git tracks the pointer; the
bytes live outside Git. `blobId` is therefore a pure function of the two values
already published beside it:

```text
pointer = "version https://git-lfs.github.com/spec/v1\n"
        + "oid sha256:" + lfs.sha256 + "\n"
        + "size " + lfs.size + "\n"

blobId  = sha1("blob " + len(pointer) + NUL + pointer)
```

Reproduced with no network access and no file, from the values this repository
had **already recorded** for its own witness in
[`hugging-face-publishes-content-hash-under-blobs`](hugging-face-publishes-content-hash-under-blobs.md):

```text
input   lfs.sha256 f568ac5de71c…eeec, size 6975879008   (nothing else)
derived pointer length 135                              (recorded pointerSize 135)
derived blobId 01e97ac6aa548d894f07a1b77725603c1d6fae40
recorded blobId 01e97ac6aa548d894f07a1b77725603c1d6fae40   → equal
```

Confirmed live against every LFS sibling of
`bartowski/google_gemma-3-4b-it-GGUF` at revision `71506238…`: **28 of 28**
reproduced exactly, each with `pointerSize` 135.

The complementary case holds too. For siblings stored directly in Git —
`README.md` (14310 bytes) and `.gitattributes` (3448 bytes) in the same
repository — the file was downloaded and `blobId` reproduced from its complete
bytes. Both matched. So `blobId` *is* a full-content identifier, but only for
files Git stores itself.

## Coverage: the non-LFS case does not occur for GGUF

The 40 most-downloaded `filter=gguf` model repositories were resolved with
`?blobs=true` and every `.gguf` sibling inspected:

```text
repos scanned 40    .gguf siblings 543    siblings without lfs 0
```

Hugging Face documents that files above 10MB are tracked by large-file storage
rather than Git, and uploaders write a per-file `filter=lfs` line into
`.gitattributes` (27 such lines in the witness repository, one per `.gguf`).
A GGUF small enough to be stored in Git directly is possible, but did not occur
in this sample.

## Why it matters

This bounds, and largely empties, the case for treating `blobId` as a second
attestation domain:

- When `lfs.sha256` is present, `blobId` is **derivable from it** and adds no
  independent evidence. It cannot corroborate the digest, because it is the
  digest, re-encoded.
- When `lfs.sha256` is absent, `blobId` does identify the complete bytes — but
  that branch requires a Git-stored GGUF, unobserved in 543 siblings and
  confined by the 10MB rule to artifacts far smaller than any model of interest.
- The two conditions are **mutually exclusive**, so `blobId` is never the
  strongest evidence available and never the only evidence available.

It also disposes of the SHA-1 collision-resistance question as a practical
concern: where `blobId` would be used, it commits to at most ~10MB of Git-stored
bytes; where it would be redundant, its SHA-1 covers a pointer that already
commits to a SHA-256.

## Consequences

- `blobId` does not justify schema complexity on its own. An algorithm/domain
  discriminator remains defensible, but for the Xet hash (see
  [`hugging-face-etag-carries-the-content-identifier`](hugging-face-etag-carries-the-content-identifier.md)),
  not for `blobId`.
- A client that parsed `blobId` for LFS artifacts and compared it to a locally
  derived pointer hash would be performing the `lfs.sha256` comparison with
  extra steps, while appearing to add an independent check.
- This scopes, and does not contradict,
  [`hugging-face-blob-id-identifies-git-tracked-content`](hugging-face-blob-id-identifies-git-tracked-content.md):
  that finding's claim is true precisely for the Git-stored case it names, and
  does not extend to model artifacts.

## Sources

- Git LFS pointer file specification:
  <https://github.com/git-lfs/git-lfs/blob/main/docs/spec.md>
- Git object storage and the blob-hash construction:
  <https://git-scm.com/book/en/v2/Git-Internals-Git-Objects#_object_storage>
- Hugging Face repository guide, 10MB large-file threshold and the generated
  `.gitattributes`:
  <https://huggingface.co/docs/hub/en/repositories-getting-started>
