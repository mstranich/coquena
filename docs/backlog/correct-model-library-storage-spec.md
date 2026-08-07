# Correct the model library storage spec wording

## Status

- **State:** open
- **Opened:** 2026-08-05
- **Owner:** project-owner
- **Priority:** high
- **Related RFC:** `gguf-model-artifact-identity`
- **Candidate change:** `correct-model-library-storage-spec`

## Provenance

This predates the RFC. It resurfaced during the Engram review conducted inside
it. It is filed at high priority not because it is urgent but because a
mis-worded spec blocks designs silently: a future proposal gets refused for
violating a rule nobody intended to write, and the refusal looks correct.

## Problem

`openspec/specs/gguf-model-library/spec.md:15-16` states:

> The sidecar SHALL NOT contain the raw GGUF metadata map and SHALL NOT use
> SQLite or a repository-wide metadata file.

The project owner has determined this is stricter than the intent it was meant
to capture. SQLite was rejected for a specific reason, quoted from the owner:

> "I only rejected the use of SQLite because I did not want to use other tools
> to hand-correct some inconsistency."

The motivating scenario was also stated: Coquena installed on `C:`, models kept
on `D:` beside personal files, `C:` reformatted for a reinstall — and the model
information lost along with the installation.

The real invariants are therefore:

1. **Co-location.** Model metadata lives with the models, never in Coquena's
   home directory, so it survives reinstalling the application.
2. **Plain text, hand-editable.** No external tool required to correct an
   inconsistency. This is what excluded SQLite.

Neither invariant forbids a per-directory file. The clause that does forbid one
is an over-generalisation of the second invariant.

A precedent already exists and is accepted by spec:
`<models>/<publisher>/publisher-avatar.<format>` is a per-directory file inside
the model tree.

## Consequence

An alias or index file within the model tree is currently blocked by wording
that was never meant to block it. That option should be *enabled and undecided*
rather than prohibited. The RFC's addressing discussion repeatedly reached
points where such a file would have been a candidate, and it could not be
evaluated on its merits.

## Required outcome

Future work should:

- rewrite the clause to express co-location plus plain-text hand-editability,
  rather than enumerating one forbidden file shape;
- keep the raw-GGUF-map prohibition, which is a separate and still-correct rule
  about sidecar content;
- keep the SQLite prohibition, restated as a consequence of hand-editability
  rather than as an arbitrary technology ban;
- state that per-directory files inside the model tree are permitted, citing
  the publisher-avatar precedent;
- make no decision about whether an alias or index file should exist — only
  stop the spec from pre-emptively refusing one.

## Acceptance questions

- Does hand-editability constrain format beyond "plain text" — for example,
  does it require a format that survives a partial hand edit without becoming
  unreadable?
- Should co-location be stated as an invariant of the library as a whole, or
  per artifact?
- Does any other spec clause encode the same over-generalisation?

## Scope boundary

Correct the wording so it expresses the intended invariants. Do not introduce
an alias or index file as part of this work, and do not relax the raw-metadata
prohibition.

## Relevant implementation areas

- `openspec/specs/gguf-model-library/spec.md`
- `internal/model/library.go`
- `internal/model/avatar.go`
