# Detect that an installed quantization has updates available

## Status

- **State:** blocked
- **Opened:** 2026-08-05
- **Owner:** project-owner
- **Priority:** high
- **Story step:** *"check for updates to a quantization or its companions"*
- **Blocked by:** `establish-sidecar-authority`
- **Candidate change:** `establish-sidecar-authority`

## Problem

The story requires an operation that answers "is what I have still current?".
`model check-updates <ref>` is the closest thing that exists, and it falls short
in three ways.

**It answers for a file, not a quantization.** The story's unit is the
quantization and its companions. A quantization that resolves to several files
needs one answer, not one per file.

**It persists nothing.** It computes an outcome and returns it. The observation
does not survive the process, so the next command has no way to know that drift
was ever seen, and `model list` cannot report it without going to the network.

**It cannot see a whole class of change.** Per the finding
`hugging-face-redirects-renamed-repositories`, a repository can be renamed while
serving HTTP 307 from the old path. A rename does not rewrite git history, so
the recorded revision still matches the current one. Comparing revisions
therefore reports "up to date" for a repository that has moved.

## Naming

The project owner has ruled that the verb pair is `update` and `upgrade`,
matching `apt` and `winget`, which the operator already uses daily.
`check-updates` was an invented verb and charges every user the cost of learning
it. This entry covers `update`; applying what it finds is `support-model-upgrade`.

No `--all` flag. `<ref>` stays mandatory. If library-wide action is ever
warranted, `<ref>` becomes optional across every operation and no flag is
introduced.

## Required outcome

Future work should:

- report update state for a quantization as a whole, including its companions;
- persist what was observed, in a field written only by this operation, so
  `model list` can report a known-stale artifact from the sidecar alone with no
  network access;
- define "an update exists" as more than "the revision advanced" — at minimum
  covering a renamed or moved repository;
- leave model bytes, the integrity baseline, provenance attestation, operator
  ownership and the derived block untouched;
- state the interaction rule explicitly: refreshing derived evidence must not
  disturb update state, and recording update state must not touch derived
  evidence;
- distinguish "never checked" from "checked and current" from "checked and
  behind".

## Acceptance questions

- Is a renamed repository an update, or a separate condition needing its own
  report? `apt update` refreshes an index; it does not rename your packages.
- Does a locally modified file count as behind, or is that strictly the
  integrity axis?
- Which operation clears the recorded state once the operator has acted?
- What is reported when the remote exposes no comparable content hash? Defer
  until `request-remote-content-hash` shows how large that residue really is.
- A private fine-tune has no remote at all. What does `update` say about it, and
  does it refuse rather than reporting a misleading result?

## Scope boundary

Detection only. Replacing bytes is `support-model-upgrade`. Do not introduce a
bulk sweep. Do not let this field share a writer with derived evidence,
integrity, attestation or operator ownership.

## Relevant implementation areas

- `internal/model/check_updates.go`
- `internal/model/types.go`
- `internal/model/record_json.go`
- `cmd/coquena/model.go`
