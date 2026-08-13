# Decide which repository name a renamed repository is recorded under

## Status

- **State:** open
- **Opened:** 2026-08-11
- **Owner:** project-owner
- **Priority:** normal
- **Story step:** *"keep it"*
- **Candidate change:** `establish-sidecar-authority`

## Problem

Repositories get renamed and the Hub answers with a redirect, per
`hugging-face-redirects-renamed-repositories`. Go's HTTP client follows it, so
Coquena reaches the data and nothing appears wrong — but the sidecar records the
name that was *requested*, which is no longer canonical, and no field
distinguishes the two.

This was raised as F3 in the `model-artifact-attestation` RFC, which stated the
question belongs to the RFC rather than to the client fix, and marked it
unresolved. Across nine review passes **no reviewer answered it**, and the
closing ruling does not settle it either.

## Why it matters

A repository match binds bytes to a revision *of a repository*. If the
repository's own identity can drift silently, the match inherits that ambiguity.

It also has a visible consequence now that the state vocabulary is settled:
`upgradable` is defined to cover repository relocation as well as revision
advancement, so an artifact will be reported as relocatable without the record
being clear about which name it is moving from.

There is a second-order risk already documented elsewhere: renaming a
repository's directory on disk silently breaks every preset pointing at it, so
whichever name is chosen has consequences beyond the sidecar.

## Required outcome

Future work should:

- decide whether the sidecar records the requested name, the canonical name, or
  both, and state why;
- make a relocation visible to the operator rather than absorbed by a
  redirect-following client;
- keep the decision consistent with how a repository-level index would record
  the same fact, per `introduce-repository-index-file`.

## Acceptance questions

- If both names are recorded, which one does a canonical reference render, and
  which one resolves?
- Is a rename an `upgradable` condition the operator applies, or metadata
  repaired in place?
- What happens to a preset that names the old repository?

## Scope boundary

Repository identity in the record. Do not change redirect handling in the HTTP
client, and do not resolve the on-disk directory question, which belongs to
`correct-model-library-storage-spec`.

## Relevant implementation areas

- `internal/model/types.go`
- `internal/model/huggingface.go`
- `internal/model/reconcile.go`
