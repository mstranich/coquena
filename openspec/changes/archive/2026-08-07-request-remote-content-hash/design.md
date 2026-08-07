## Context

See `proposal.md` for motivation. `Resolver.resolveRepository` currently calls
the Hugging Face model revision endpoint without blob detail and decodes
`siblings[].lfs.oid` into `Candidate.LFSOID`. The verified upstream response
instead exposes the file-content SHA-256 as `siblings[].lfs.sha256` only when
`?blobs=true` is requested. Reconciliation and update checking already consume
that transient candidate field, while the durable specification already
requires digest comparison.

The operator witness is
`lmstudio-community/gemma-4-26B-A4B-it-QAT-GGUF:Q4_0` in the project's test
`home/`, selected by setting `COQUENA_HOME`. It was downloaded before its
publisher updated the remote weights, so it can exercise both content-hash
retrieval and real remote drift. The finding also covers six additional
repositories: every sampled repository returned `lfs.sha256`, and none required
weaker evidence. During application, the named Gemma 4 26B witness was confirmed
to resolve through an `xet-bridge-us` URL while still publishing a matching
`lfs.sha256`; Xet transport is therefore covered by the witness without making
a universal claim about every Xet repository.

## Goals / Non-Goals

**Goals:**

- Make every repository resolution that supplies candidates request blob
  metadata and expose a correctly decoded SHA-256 to existing consumers.
- Keep content identity transient: use the digest for comparison without adding
  a new persisted sidecar field.
- Fail explicitly on invalid published digest data rather than treating a
  client or decoding defect as genuinely absent evidence.
- Demonstrate matching, mismatching, absent, and malformed-hash behavior with
  local HTTP fixtures, then repeat the documented external witnesses manually.

**Non-Goals:**

- Defining partial/verified/unattested persistence semantics.
- Introducing weaker evidence or deciding Xet policy before observing it.
- Recording `blobId`, `lfs.size`, or requested-versus-canonical repository
  identity.
- Changing sidecar schema, reference grammar, or command surface.
- Implementing `model update` or `model upgrade`; the witness may expose their
  need, but those operations remain owned by later changes.

## Decisions

### Request blob metadata in the shared repository resolver

`resolveRepository` will add `blobs=true` to the revision endpoint through the
request URL's query values. All existing consumers then receive the same remote
candidate identity without a second request or a reconciliation-specific
resolver path.

The alternative was to request blob data conditionally only from reconciliation
and update checking. That would either duplicate repository resolution or add a
mode whose candidates have subtly different guarantees. The shared request is
the smaller reversible change. Its response-size cost will be measured in tests
and the known witness set rather than pre-optimised.

### Name the transient value by its meaning

The response model will decode `lfs.sha256`. The transient candidate field and
internal consumers will be renamed from the Git-LFS-specific `LFSOID` vocabulary
to `ContentSHA256`, because the value is a file-content digest directly
comparable with `hashFile`; it is not the response's `blobId` and Hugging Face
does not publish it under an `oid` property.

Keeping the old name was rejected because it preserves the false upstream model
that caused the defect. Persisting a second digest was also rejected: the
current sidecar baseline and attestation semantics are owned by the open RFC.

### Treat absence and invalid metadata differently

A missing `lfs` block or empty `lfs.sha256` remains "no published content hash"
and follows the existing partial path. A non-empty value that is not a valid
SHA-256 hex digest is an invalid upstream response and will fail resolution
with an explicit diagnostic; it will not silently degrade to partial.

This preserves compatibility for genuinely non-LFS siblings while preventing
malformed or incorrectly decoded evidence from authorising publication. Using
`blobId` or `lfs.size` as fallback evidence was rejected because neither is
directly comparable with the local file SHA-256.

### Keep mismatch and publication behavior unchanged

`reconcileAgainst` will continue hashing local bytes, refuse a digest mismatch,
and publish only on agreement or genuine absence. This change corrects the
candidate evidence feeding that path; it does not redesign the sidecar or the
temporary `Partial` result. `check-updates` will consume the same corrected
digest, so same-revision content replacement becomes observable as already
specified.

## Risks / Trade-offs

- **Larger Hugging Face responses for repositories with many siblings** → Use
  one enriched metadata request rather than a follow-up per artifact; retain
  existing bounded per-candidate inspection and measure representative fixture
  size before considering a separate optimisation.
- **Some storage backend may omit `lfs.sha256`** → Preserve the explicit partial
  path and record the post-change residue as evidence for RFC Q3; do not guess
  from `blobId`, ETag, filename, or size.
- **Strict validation could reject an unexpected digest representation** → Test
  the documented 64-hex representation and report the offending artifact
  clearly so future evidence can justify broadening the parser.
- **Shared resolver change affects discovery, download and update checking** →
  Cover request query construction and candidate decoding at the resolver
  boundary, then retain focused reconciliation and update-check regression
  tests.

## Migration Plan

1. Update the transient Hugging Face response and candidate models.
2. Update resolver fixtures and assert `blobs=true` on incoming requests.
3. Update reconciliation and update-check consumers and regression tests.
4. Run repository verification and manually repeat the documented witness set.

No persisted data migration is required. Rollback consists of reverting the
client correction; existing sidecars remain readable because their schema does
not change.
