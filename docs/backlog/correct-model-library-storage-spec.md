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

## The prohibition is narrower than its own intent

Naming SQLite is weaker than the invariant it was meant to serve. As written,
the clause forbids one product. A proposal using BoltDB, LMDB, DuckDB or any
other embedded store satisfies the letter of the spec while defeating the
purpose entirely.

The project owner restated the position on 2026-08-10, extending it and adding
a second reason:

> "hay que ajustar la prohibición de SQLITE extendiendo a cualquier BBDD
> (colocar una BBDD es sobreingeniería)."

So the prohibition has two independent justifications, and both generalise past
SQLite: hand-editability excludes any opaque store, and a database is
disproportionate to the problem. The corrected clause must be derived from the
invariant rather than enumerate a technology.

## Consequence

An index file within the model tree is currently blocked by wording that was
never meant to block it. The RFC's addressing discussion repeatedly reached
points where such a file would have been a candidate, and it could not be
evaluated on its merits.

**Decided 2026-08-10.** This is no longer a hypothetical option. The project
owner has approved a per-repository index file, and proposed `index.json` under
`<models>/<user>/<repo>/` as the shape:

> "sí se puede crear un archivo, de hecho, eso lo apruebo y no me parece malo.
> hasta propuse utilizar una suerte de `index.json` dentro de un
> `<user>/<repo>` para simplificar cosas como la gestión de modelos companion o
> modelos sparsed (aquellos que son chunks de GB que forman un único modelo). o
> sea, para cuando un sidecar no podría ser suficiente, y este tipo de archivos
> resuelve este caso, hasta incluso puede llevar el histórico (como nombre
> previo […])."

That changes this entry's standing. It is not enabling an option for later
evaluation; it is removing wording that **approved work would violate**. Its
priority as a prerequisite is now hard: `introduce-repository-index-file`
cannot be specified while the spec forbids its central artifact.

## Required outcome

Future work should:

- rewrite the clause to express co-location plus plain-text hand-editability,
  rather than enumerating one forbidden file shape;
- keep the raw-GGUF-map prohibition, which is a separate and still-correct rule
  about sidecar content;
- **extend the SQLite prohibition to any database or opaque binary store**,
  stated as a consequence of hand-editability and of proportionality rather
  than as a ban on one named product, so that no other embedded engine
  satisfies the letter of the spec while defeating its intent;
- state that per-directory files inside the model tree are permitted, citing
  the publisher-avatar precedent;
- make no decision about the *contents or schema* of the approved index file —
  that is owned by `introduce-repository-index-file`.

Wording along these lines satisfies the above:

> The sidecar SHALL NOT contain the raw GGUF metadata map. Model metadata SHALL
> be co-located with the models beneath the effective configured models
> directory, never in Coquena's home directory, so that it survives
> reinstalling the application. It SHALL be stored in plain text an operator
> can inspect and correct with a text editor alone; consequently no database or
> other opaque binary store SHALL be used to hold it. Per-directory plain-text
> files inside the model tree are permitted.

## Acceptance questions

- Does hand-editability constrain format beyond "plain text" — for example,
  does it require a format that survives a partial hand edit without becoming
  unreadable?
- Should co-location be stated as an invariant of the library as a whole, or
  per artifact?
- Does any other spec clause encode the same over-generalisation?
- Does the prohibition bind only metadata, or the whole model tree? The
  publisher avatar is a binary image and must remain permitted, so the clause
  cannot be written as "no binary files".

## Scope boundary

Correct the wording so it expresses the intended invariants. Do not design the
index file here — its schema, contents and lifecycle belong to
`introduce-repository-index-file`. Do not relax the raw-metadata prohibition.

## Relevant implementation areas

- `openspec/specs/gguf-model-library/spec.md`
- `internal/model/library.go`
- `internal/model/avatar.go`
