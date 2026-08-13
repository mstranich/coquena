# Verify a download against the digest the provider already gave us

## Status

- **State:** open
- **Opened:** 2026-08-11
- **Owner:** project-owner
- **Priority:** high
- **Story step:** *"get the model onto the machine"*
- **Candidate change:** `verify-download-against-remote-digest`

## Problem

Coquena resolves an artifact, receives its content digest from the provider,
and then throws it away before downloading.

`resolveRepository` parses `lfs.sha256` into `Candidate.ContentSHA256`
(`huggingface.go:129`). `Download` builds the job from that same candidate but
copies only `SizeBytes`, `ETag` and `LastModified` (`manager.go:108`); the
digest is not carried. On completion the worker hashes the finished file and
writes the result straight into the sidecar (`manager.go:479-484`) with nothing
to compare it against.

The only integrity check on a download is **length**: `manager.go:469` fails the
job when the byte count disagrees with what was expected.

## Why it matters

A transfer that is silently corrupted while still delivering the expected number
of bytes is recorded as good, and is then treated as *attested by construction*
because the bytes came from a revision URL. The one artifact class Coquena is
most confident about is the one it never checks, even though the evidence needed
to check it was already in hand and free.

This is the inverse of the defect `distinguish-unattested-artifacts` describes.
There, an unverified artifact is presented as verified. Here, an unverified
artifact genuinely *could* have been verified and was not.

## Required outcome

Future work should:

- carry the resolved content digest from the candidate into the download job;
- compare the completed file against it before publishing the sidecar, and fail
  the job when they disagree;
- record which digest role the comparison satisfied, so the resulting artifact
  is `synced` on evidence rather than on the assumption that a revision URL
  cannot mislead;
- keep working when the provider supplies no acceptable digest: the download
  still completes, and the artifact simply does not gain a repository match.

## Acceptance questions

- Should a digest mismatch discard the partial file, or retain it for
  inspection?
- Does a download that cannot be checked produce `local`, or `detached`?
- Should the comparison be streamed during transfer rather than run as a second
  pass over a file that may be tens of gigabytes?

## Scope boundary

The download path only. Do not redefine the sidecar schema here; consume the
roles settled in `model-artifact-states`.

## Relevant implementation areas

- `internal/model/manager.go`
- `internal/model/huggingface.go`
