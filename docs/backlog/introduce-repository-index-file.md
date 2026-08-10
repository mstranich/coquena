# Introduce a per-repository index file

## Status

- **State:** blocked
- **Opened:** 2026-08-10
- **Owner:** project-owner
- **Priority:** high
- **Related RFC:** `model-artifact-attestation`
- **Blocked by:** `correct-model-library-storage-spec`
- **Story step:** *"I ask Coquena for the quantization … it must be a complete bundle"*
- **Candidate change:** `introduce-repository-index-file`

## Provenance

Decided by the project owner on 2026-08-10, in response to
`correct-model-library-storage-spec`. Recorded here rather than in the RFC
because RFC process rule 2 fixes that document's question list at opening:
anything that is not an answer to one of its six questions goes to the backlog
with a pointer back. This is such a thing — an input to Q1 and Q2, not an
answer to them.

Quoted, so the decision is not paraphrased away:

> "sí se puede crear un archivo, de hecho, eso lo apruebo y no me parece malo.
> hasta propuse utilizar una suerte de `index.json` dentro de un `<user>/<repo>`
> para simplificar cosas como la gestión de modelos companion o modelos sparsed
> (aquellos que son chunks de GB que forman un único modelo). o sea, para cuando
> un sidecar no podría ser suficiente, y este tipo de archivos resuelve este
> caso, hasta incluso puede llevar el histórico (como nombre previo, como ha
> ocurrido […])."

## Problem

The per-artifact sidecar can only describe one file. Three facts are not
properties of a file, and today have nowhere to live.

**Bundle composition.** Per `quantizations-can-be-shard-directories`, a
quantization can be a directory of shards; `unsloth/DeepSeek-R1-GGUF` publishes
no `.gguf` at its root at all. Which shards constitute one quantization, and in
what order, is a fact about the set.

**Companion sharing.** A projector serves several quantizations of one
repository. The relation is repository-scoped and cannot be read off any single
file.

**Repository identity over time.** Per
`hugging-face-redirects-renamed-repositories`, a repository can be renamed and
the API answers HTTP 307 to the new path. Two artifacts in the operator's own
library are affected. The directory on disk still reads
`deepreinforce-ai/Ornith-1.0-9B-GGUF`, every sidecar underneath repeats that
stale name, and no field distinguishes the name that was requested from the
name that answered. A previous name is not recoverable from the bytes: if it is
not recorded, it is lost.

The sharing boundary for all three is the repository — already established by
`address-quantization-bundles`, which states that two repositories publishing
the same model each carry their own companion copy and do not interact. That is
exactly where the owner placed the file.

## Not a database

`correct-model-library-storage-spec` carries the standing prohibition, restated
by the owner on 2026-08-10 and extended from SQLite to any database or opaque
binary store, on two grounds: metadata must be correctable with a text editor
alone, and a database is disproportionate to the problem. A plain-text JSON
file per repository satisfies both. This entry does not reopen that.

## The derivable/non-derivable line

`address-quantization-bundles` rejects a persisted reference count for
companions, and the reason generalises to this file:

> This needs **no persisted reference count**. […] a derived answer cannot
> drift the way a stored counter can — a file removed with the operating
> system's file manager would silently corrupt a counter.

An index file does not repeal that argument; it inherits it. The operator can
still delete a file with Explorer, and the index cannot know. The proposed rule:

**The index records facts the files cannot reconstruct. It does not duplicate
facts enumeration already answers.**

| Belongs in the index | Stays derived by enumeration |
|---|---|
| Previous repository names and the resolved canonical name | Which quantizations are currently installed |
| Which files compose a sharded quantization, and their order | Whether a companion is still required by anything |
| Which companion serves which quantizations, as a relation | Whether a bundle is complete right now |

Anything recorded in the index must be treated as a claim about the past, not
an assertion about the present filesystem, and reconciled against the tree
rather than trusted over it.

## Required outcome

Future work should:

- define a plain-text `index.json` at `<models>/<user>/<repo>/`, hand-editable
  and co-located with the models it describes;
- record shard composition and companion relations, so a quantization resolves
  to a set without re-deriving it from filenames;
- record repository rename history, including the name originally requested and
  the canonical name that answered;
- treat a missing index as a recoverable state, not a corrupt library: it must
  be reconstructible from the tree for everything except the history it alone
  carries;
- never let the index override what the filesystem says about presence;
- state a schema version, consistent with how the sidecar already versions
  itself.

## Acceptance questions

- ~~On a rename, is the on-disk directory renamed to the canonical name, or
  kept and annotated by the index?~~ **Answered 2026-08-10 by the project
  owner: the directory is renamed.** The work divides across the `update` /
  `upgrade` pair — `update` observes the 307 and records both names here,
  altering nothing on disk; `upgrade` performs the move. Applying it under
  `import` (today `reconcile`) was considered and rejected, because adopting
  local bytes and relocating a repository are different acts. Relocation is
  owned by `support-model-upgrade`; this entry owns only the record it reads.
  Consequence for the schema: the directory name is **location, not identity**,
  and can lag behind the canonical name between an `update` and an `upgrade`,
  so the index must remain readable while the two disagree.
- What happens when the index and the tree disagree — does a command repair,
  report, or refuse?
- Does the index supersede any field currently in the sidecar, or only add?
  Duplicating repository identity in both is how they drift apart.
- Is the index written on download only, or also by `verify` and `reconcile`?
  The authority partition in `model-artifact-attestation` gives each sidecar
  field exactly one writer; the index needs the same discipline.
- Does a repository with a single unsharded file and no companion need an index
  at all, or is it optional in that case?

## Scope boundary

The file, its schema and its lifecycle. Do not fold in attestation semantics —
whether the sidecar or the index carries attested identity is Q1 of
`model-artifact-attestation` and is not settled here. Do not implement deletion
(`remove-installed-models`), the bundle unit itself
(`address-quantization-bundles`), or the directory relocation that consumes the
recorded rename (`support-model-upgrade`). All three will read this file.

## Relevant implementation areas

- `openspec/specs/gguf-model-library/spec.md`
- `internal/model/library.go`
- `internal/model/manager.go`
- `internal/model/huggingface.go`
