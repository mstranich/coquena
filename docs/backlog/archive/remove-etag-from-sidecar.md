# Stop storing an ETag whose meaning changes between hops

## Status

- **State:** closed
- **Opened:** 2026-08-11
- **Closed:** 2026-08-15
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

## Outcome

Closed by `establish-sidecar-authority`. `Artifact.ETag` is gone from the
record. The field moved to `Candidate.ETag` as `json:"-"` transport state, and
`Job.ETag` is untouched, because `If-Range` resume needs precisely the opaque
last-hop value the sidecar must not keep.

Answers to this entry's acceptance questions:

- **Nothing read `ETag` from a sidecar.** Its only consumer took it off the
  freshly resolved candidate, before any sidecar existed, so dropping it from
  the persisted shape regressed nothing.
- **No migration was needed** — and none was written. The schema bump to 4
  removed the preceding-version reader entirely, so the field simply stops
  existing rather than being translated away.

Rejected: reading `X-Linked-ETag` from the first response to recover the
comparable `lfs.sha256`. Reviewers proposed correcting the ETag rather than
isolating it, and the ruling chose removal; Coquena already gets `lfs.sha256`
from the repository API, so a second path to the same value would be work
justified by a header rather than by a need. The prohibition is worded against
*opaque* tags rather than against headers as a class, so a published identifier
carried in a header with an unambiguous domain stays admissible.
