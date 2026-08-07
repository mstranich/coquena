## Why

Online reconciliation currently reports that Hugging Face supplied no content
hash for locally adopted artifacts, even though Hugging Face publishes the
SHA-256 when blob metadata is requested. This client defect leaves matching
artifacts only partially reconciled and prevents the existing reconciliation
contract from being fulfilled.

## What Changes

- Request Hugging Face blob metadata when resolving repository siblings whose
  content identity is needed.
- Parse the published content digest from `lfs.sha256` instead of the
  nonexistent `lfs.oid` field.
- Preserve the existing refusal behavior when a local SHA-256 disagrees with
  the remote digest.
- Distinguish a genuinely absent remote hash from a client request or decoding
  failure instead of attributing every empty digest to Hugging Face.
- Re-run the known locally cached model witnesses and record which artifacts,
  if any, remain partially reconciled after the correction.
- Non-goals: defining attestation strength or sidecar authority, accepting
  weaker evidence, recording canonical repository redirects, changing model
  upgrade semantics, or adding bulk reconciliation.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

None. The durable `gguf-model-interfaces` requirement already says online
reconciliation compares the local SHA-256 with the content hash reported by
Hugging Face and persists a full sidecar only on agreement. This change brings
the client into conformance with that existing requirement, so the change opts
out of delta specs rather than inventing a new contract.

## Impact

- Affected implementation: `internal/model/huggingface.go` and the remote
  candidate data consumed by reconciliation; diagnostics may affect
  `internal/model/reconcile.go` and `internal/model/reconcile_repository.go`.
- Affected tests: Hugging Face response fixtures and online reconciliation
  cases for matching, mismatching, absent, and malformed remote hashes.
- External system: Hugging Face model revision API with `?blobs=true`.
- No dependency, storage-schema, command-surface, or breaking API change is
  intended.
