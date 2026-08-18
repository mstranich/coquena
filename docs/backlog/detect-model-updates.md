# Detect that an installed quantization has updates available

## Status

- **State:** open
- **Opened:** 2026-08-05
- **Owner:** project-owner
- **Priority:** high
- **Story step:** *"check for updates to a quantization or its companions"*
- **Candidate change:** `detect-model-updates`

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

**Restated 2026-08-10**, with a second analogy that fixes the boundary more
precisely than `apt` alone:

```text
update   consults the provider            git fetch    apt update
upgrade  downloads and applies changes    git pull     apt upgrade
```

`update` is a query that leaves a record. `check-updates` is to be renamed to
`update` and improved to meet the contract below — the rename alone does not
discharge this entry.

## What "no impact on disk" means here

Stated by the owner on 2026-08-10: `update` must not impact disk, *except* that
it may write `index.json` or its equivalent.

That distinction is the whole contract, and it is not a technicality. The model
tree — GGUF bytes, their sidecars, directory layout — is untouched by `update`
under every circumstance. The repository index is a record of observations, and
writing an observation into it is what makes `update` more than a transient
query: without it the finding dies with the process, which is the second defect
listed above.

`git fetch` behaves the same way. It writes to the repository, but it does not
touch the working tree.

**This supersedes "the sidecar" as the storage location for a rename.** A
sidecar describes one file; a rename is a property of the repository, so it
belongs in the index per `introduce-repository-index-file`. Per-artifact drift
may still live in the sidecar; repository-level observations may not.

No `--all` flag. `<ref>` stays mandatory. If library-wide action is ever
warranted, `<ref>` becomes optional across every operation and no flag is
introduced.

## Required outcome

Future work should:

- report update state for a quantization as a whole, including its companions;
- persist what was observed, in a field written only by this operation, so
  `model list` can report a known-stale artifact from local records alone with
  no network access — per-artifact drift in the sidecar, repository-level
  observations such as a rename in the repository index;
- leave the model tree untouched: no bytes moved, renamed or rewritten, the
  index being the only file this operation may write;
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
  report? `apt update` refreshes an index; it does not rename your packages —
  and under the 2026-08-10 division it does not have to, since `upgrade` owns
  the relocation. What remains open is whether `update` reports a rename in the
  same channel as "behind by a revision" or as a distinct condition.
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
