# Hugging Face ETags carry content identifiers, in two different domains

**Kind:** finding · **State:** current · **Verified:** 2026-08-10

## The fact

The `resolve` endpoint publishes a content identifier in an ETag header — but
*which* identifier depends on where you read it, because the response is a
redirect and the two hops disagree.

Verified against `bartowski/google_gemma-3-4b-it-GGUF` at revision `71506238…`:

```text
GET /resolve/main/google_gemma-3-4b-it-Q4_0.gguf
  302  X-Linked-ETag: "824eac1a…720e"   = the sibling's lfs.sha256 exactly
  200  etag:          "654837…4272"     = X-Xet-Hash, a different value

GET /resolve/main/README.md              (stored in Git, not LFS)
  307  X-Linked-ETag: "a6cf6784…ebe1"   = the sibling's blobId (Git blob OID)
  200  ETag:          "a6cf6784…ebe1"   = the same value
```

So `X-Linked-ETag` on the **first** response is the repository-level content
identifier: `lfs.sha256` for LFS-backed siblings, the Git blob OID for
Git-stored ones. It is the same value the metadata API publishes, delivered
through a second, independent channel.

The identifier on the **final** response is not. For an LFS artifact it is the
Xet CAS hash — a third content-addressing domain, equal to the `X-Xet-Hash`
header, and equal to neither the SHA-256 nor the blob OID. It only coincides
with `X-Linked-ETag` for Git-stored files, which are not redirected to Xet.

## Why it matters

Coquena reads the wrong one, silently. `internal/model/huggingface.go:137-144`
issues a HEAD and stores `response.Header.Get("ETag")`. Go's HTTP client
follows redirects, so `response` is the final hop: for every GGUF artifact,
`Artifact.ETag` holds a **Xet CAS hash**, while `X-Linked-ETag` — the
directly comparable SHA-256 — is discarded on the hop before.

That explains a value the attestation RFC's reviewers repeatedly set aside as
an "unexplained ETag" and grouped with filename and size as mere corroboration.
It is not descriptive metadata. It is a content identifier that was read from
the wrong response and therefore could not be interpreted.

The Xet hash is not comparable to a locally computed SHA-256: it is a
chunk-tree root over the content, not a digest of it. Stored under a bare
`ETag` field with no algorithm recorded, it is indistinguishable from the Git
blob OID a non-LFS sibling yields in the same field.

**Corrected 2026-08-10.** This section previously said the Xet value was "not
reproducible from local bytes without implementing Xet chunking". That is true
of Coquena as it stands but was phrased so it could be read as a property of
the evidence, which it is not: the Xet file ID is fully reproducible from
complete local bytes, and has since been reproduced end-to-end against the
published conformance vectors. See
[`xet-file-id-is-a-reproducible-full-content-hash`](xet-file-id-is-a-reproducible-full-content-hash.md).
What remains true is the narrower claim that matters here — Coquena cannot
compare the value today, and a field that may hold it needs to record which
domain it came from.

## Consequences

- `X-Linked-ETag` is a genuine second source of the same content hash. It does
  not create a case for weaker evidence — it strengthens the case that the
  strong evidence is available — but it does mean transport headers deserve a
  second look before an artifact is declared unattestable.
- Remote content identifiers need an explicit algorithm/domain discriminator.
  This is the finding that establishes that need: three domains
  (`sha256`, `git-blob-sha1`, Xet) can reach the same field, and one of them is
  not locally reproducible.
- `Artifact.ETag` as currently populated should not be treated as evidence of
  anything until it records which domain the value came from. Whether the field
  is corrected, discriminated, or dropped belongs to the sidecar schema
  decision.

## Sources

- Live HTTP responses from `huggingface.co/…/resolve/…`, headers quoted above.
- Cross-checked against `?blobs=true` metadata for the same revision; see
  [`hugging-face-publishes-content-hash-under-blobs`](hugging-face-publishes-content-hash-under-blobs.md)
  and
  [`hugging-face-blob-id-of-lfs-files-hashes-the-pointer`](hugging-face-blob-id-of-lfs-files-hashes-the-pointer.md).
- Hugging Face Xet storage documentation:
  <https://huggingface.co/docs/hub/en/xet/index>
