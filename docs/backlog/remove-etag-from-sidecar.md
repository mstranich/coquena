# Stop storing an ETag whose meaning changes between hops

## Status

- **State:** open
- **Opened:** 2026-08-11
- **Owner:** project-owner
- **Priority:** normal
- **Story step:** *"get the model onto the machine"*
- **Candidate change:** `establish-sidecar-authority`

## Problem

`Artifact.ETag` (`types.go:47`, `:102`) holds whatever the final HTTP response
carried. Coquena issues a HEAD with a redirect-following client
(`huggingface.go:137-144`) and reads the ETag off the last hop, which for a
Hugging Face artifact is the Xet CAS hash — a content identifier in a domain
Coquena cannot reproduce, stored under a field name that suggests an ordinary
HTTP cache validator.

Per `hugging-face-etag-carries-the-content-identifier`, the *first* response
carries `X-Linked-ETag`, which is exactly the sibling's `lfs.sha256`. Coquena
discards the comparable value and keeps the incomparable one.

For a sibling stored directly in Git the same field would instead hold a Git
blob OID. Three domains can reach one field, and none of them is recorded.

## Why it matters

The attestation ruling removes `ETag` from the sidecar authority model: it is
an HTTP container whose value changes domain across hops, not an attestation
algorithm. Until the field is dealt with, the sidecar carries a
content-addressing value that looks like evidence, is not labelled with its
domain, and would be wrong to compare against anything.

## Required outcome

Future work should:

- remove `Artifact.ETag` from the sidecar, or retain it explicitly as opaque
  transport metadata that no derivation reads;
- never let it flow into a digest role;
- if a value from the transport is worth keeping, read `X-Linked-ETag` from the
  first response and record it with its role and algorithm like any other
  digest.

## Acceptance questions

- Is any current behaviour reading `ETag` — conditional requests, change
  detection — that would regress if it were dropped?
- Does removal need a sidecar schema version bump and a migration, or can the
  field simply stop being written and be ignored on read?

## Scope boundary

The `ETag` field only. The digest roles themselves are settled in
`model-artifact-states`.

## Relevant implementation areas

- `internal/model/types.go`
- `internal/model/huggingface.go`
