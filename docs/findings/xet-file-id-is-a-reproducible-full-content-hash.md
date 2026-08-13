# The Xet file ID is a reproducible full-content hash

**Kind:** finding · **State:** current · **Verified:** 2026-08-10

## The fact

Hugging Face's published Xet protocol defines the value returned as
`X-Xet-Hash` as the Xet file ID, and defines how an independent implementation
computes that ID from the complete file bytes:

1. split the file with the specified content-defined chunking algorithm;
2. compute a keyed BLAKE3 hash for every chunk;
3. aggregate the ordered chunk hashes and lengths into the specified Merkle
   tree; and
4. hash the root once more with the all-zero BLAKE3 key to obtain the file
   hash.

The specification includes a reference implementation and public conformance
vectors. Its stated interoperability goal is that independent implementations
produce the same hashes.

`X-Xet-Hash` is therefore not opaque descriptive metadata and is not inherently
unverifiable from local bytes. Coquena does not currently implement the Xet
chunker/hash, so it cannot compare the value **today**; that is an
implementation-capability statement, not a property of the evidence.

## Independent reproduction, 2026-08-10

The claim above was recorded from the specification. It has since been executed
rather than read, in about 50 lines against the published reference files, with
no Xet code involved:

```text
raw bytes → gearhash CDC boundaries        first 2 chunk lengths  131072, 106099  ✓
          → keyed BLAKE3 chunk hashes      b10aa1dc…5ce4, 26255591…c907           ✓
          → Merkle aggregation (796 → 150 → 31 → 6 → 2 → 1)
          → xorb hash                      eea25d6e…7632                          ✓
          → all-zero-key file hash         118a5332…af97                          ✓
```

Every value matches `xet-team/xet-spec-reference-files` exactly. Two details
are load-bearing and are *not* stated on the hashing page — an implementor
needs the linked reference implementation to get them right:

- The Merkle tree is not a flat aggregation of all chunks. Nodes are grouped by
  a content-defined cut (`next_merge_cut`): groups of 2 to 9, cut after the
  first node whose hash is ≡ 0 mod 4. Aggregating all 796 chunks in one buffer
  produces a confidently wrong hash.
- That modulus reads the **last** of the four `u64` words, and the hash's
  string form is a per-8-byte endianness inversion of its raw bytes. The raw
  bytes, not the string form, are what the final all-zero-key hash consumes.

So the protocol is reproducible, and now demonstrably so — but "specified" and
"specified precisely enough to implement from the prose alone" are not the same
claim, and only the first survives.

## Why it matters

The finding
[`hugging-face-etag-carries-the-content-identifier`](hugging-face-etag-carries-the-content-identifier.md)
correctly establishes that the final-hop `ETag` may be an Xet-domain value and
must not be compared as though it were a raw SHA-256 or Git blob OID. Its claim
that the Xet value is not locally reproducible without implementing Xet
chunking is true of the current client but can be misread as saying it is not a
full-content identifier. The protocol establishes the opposite.

This distinction affects schema reasoning. Multiple identifiers can justify an
algorithm/domain discriminator **if Coquena chooses to accept and reproduce
them**. It does not require the attestation schema to accept every transport
identifier: Coquena may instead make raw SHA-256 its sole accepted attestation
algorithm and retain or discard Xet ETags as transport metadata.

## Consequences

- An Xet file ID can provide complete-byte identity after deterministic local
  recomputation; it is not merely corroborating evidence.
- The current Go client must not promote its final-hop `Artifact.ETag` into
  attestation, because it neither records the domain nor computes the matching
  local Xet file hash.
- For Git-backed Hub repositories, the Xet upload protocol separately requires
  the raw file SHA-256 used by the Git LFS pointer. Since that value is already
  available as `lfs.sha256`, implementing Xet hashing solely for attestation
  would add cost without covering an observed GGUF residue.

## Sources

- Xet protocol goals and reference implementations:
  <https://github.com/huggingface/hub-docs/blob/main/docs/xet/index.md>
- Xet chunk, Merkle and file-hash construction, plus conformance vectors:
  <https://github.com/huggingface/hub-docs/blob/main/docs/xet/hashing.md>
- Xet upload protocol requirement to compute SHA-256 for Git-backed Hub
  repositories:
  <https://github.com/huggingface/hub-docs/blob/main/docs/xet/upload-protocol.md>
- `X-Xet-Hash` as the Xet file ID:
  <https://github.com/huggingface/hub-docs/blob/main/docs/xet/file-id.md>

