# Hugging Face `blobId` identifies the complete content of Git-tracked files

**Kind:** finding · **State:** current · **Verified:** 2026-08-10

## The fact

For a repository sibling stored directly in Git rather than Git LFS,
Hugging Face exposes `blobId` as the file's Git object ID when file metadata is
requested. Hugging Face's own API documentation calls it the file's Git OID;
its cache documentation says Git-tracked files are addressed by a 40-hex
SHA-1, while LFS files are addressed by a 64-hex SHA-256.

A Git blob OID is reproducible from the complete local file. Git hashes:

```text
"blob " + decimal-byte-length + NUL + file-bytes
```

with SHA-1 in the 40-hex object format. It is therefore not equal to
`sha1(file-bytes)`, and it cannot be compared to `Artifact.SHA256`, but it is a
content identifier for the entire file rather than filename, size, revision or
descriptive metadata.

## Why it matters

The current client parses only `lfs.sha256` and discards `blobId`. Consequently
it reports a Git-tracked sibling as having no comparable content evidence even
though the API supplied an independently reproducible content identifier.

This refutes the claim that `blobId` can only corroborate identity and cannot
establish complete-byte identity. It can establish it if Coquena records the
algorithm/domain and computes the Git blob OID locally. It does **not** show
that SHA-1 and SHA-256 offer equal collision resistance, so accepting Git blob
SHA-1 remains an explicit security-policy decision rather than an automatic
substitution.

## Consequences

- Remote attestation evidence needs an algorithm/domain discriminator; a bare
  digest field is insufficient.
- `lfs.sha256` remains the directly comparable and stronger common case.
- When `lfs` is absent and `blobId` is present, Coquena can distinguish
  "unsupported/declined Git-blob evidence" from "no content identifier".
- Size, ETag, commit, filename and GGUF metadata remain corroborating evidence,
  not substitutes for a reproducible full-content identifier.

## Sources

- Hugging Face `RepoSibling` API documentation:
  <https://huggingface.co/docs/huggingface_hub/en/package_reference/hf_api#huggingface_hub.hf_api.RepoSibling>
- Hugging Face cache layout and object identifiers:
  <https://huggingface.co/docs/hub/en/local-cache#blobs-content-addressed-file-storage>
- Git object storage and the exact blob-hash construction:
  <https://git-scm.com/book/en/v2/Git-Internals-Git-Objects#_object_storage>

