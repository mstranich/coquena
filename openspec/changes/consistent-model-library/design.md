## Context

The previous change made classification possible; this one makes the library
consistent enough to serve an invocation. The distinction matters because the
work that remains is not about reading GGUF headers — that already works — but
about when classification happens, what unit reconciliation operates on, and
what the catalog is allowed to claim about an artifact.

Everything below rests on facts already verified against real artifacts:
`general.type` declares whether a file is a `model` or an `mmproj`; a projector
matches a principal when `clip.vision.projection_dim` equals the principal's
`<arch>.embedding_length`; and both are readable through bounded random access
without downloading anything.

### Decided by the operator, versus proposed here

The previous change shipped commands whose details were filled in during
implementation rather than agreed beforehand, and the gaps only surfaced when
the library had to be trusted. This section exists so the same thing cannot
happen quietly again.

**Decided by the operator.** The tree is discarded, because a projector is
compatible with every quantization of its principal. Reconciliation is driven
by repository and mirrors discovery, comparing disk against Hugging Face by
name and metadata. `--all` is online, rejects automatically what it cannot
match, and warns before a long pass. `--accept` is per artifact only. `reconcile`
loses `--json`, because it is an impact rather than a query. Companion CLI
surface enters this change, to be either exposed or removed. Integrity work is
out of scope.

**Proposed here, open to veto.** That a projector alone is reported unusable
rather than merely typed. That a principal without its projector stays usable,
since it still serves text-only inference. That triage treats "one principal,
matched by name and hash" as the only automatically resolvable case. That
usability is derived on read rather than persisted. Names of new flags and
subcommands.

## Goals / Non-Goals

**Goals.** After one reconciliation pass, the catalog can state for every
artifact what it is, whether it is usable, and which companions are admissible
for it — with no remaining artifact whose type is unknown for a reason Coquena
could have determined on its own.

**Non-Goals.** Integrity verification and repair, updating, listing filters,
HTTP and Angular surfaces, renamed local artifacts, and non-current revisions.

## Decisions

### Classification moves to enumeration

`Manager.Library` classifies from the sidecar when one exists and from the
artifact's own header when it does not. Today an unreconciled entry carries no
classification at all, which is why a real library shows nine artifacts with an
empty type column and no way to tell a projector from a model.

This does not weaken the rule that reconciliation is mandatory. Reading a
header is an observation; it grants no standing. An unclassified artifact stays
unreconciled, stays unresolvable for binding, and stays unusable. What changes
is only that the operator can see what the artifact is before deciding.

The cost has to be paid honestly. `ReadMetadata` materializes every array into
`[]any`, so classifying a principal today decodes a quarter of a million tokens
and as many merges. Enumeration therefore needs a metadata read that skips
array payloads: fixed-width element types can be skipped by computing the span,
string arrays require walking the length prefixes but allocate nothing. The
tokenizer digest still needs the arrays, but only at reconciliation time, never
at listing time.

### Reconciliation operates on a repository

A repository is the unit Hugging Face publishes and the unit an operator
thinks in. Reconciling one artifact at a time guarantees the half-reconciled
state the product exists to prevent: a principal installed, its projector still
foreign, and no preset able to bind the pair.

So reconciliation resolves the repository, enumerates its remote candidates
with their classification exactly as `discover` does, and matches them against
the artifacts on disk. Matching is by filename, which Hugging Face guarantees
unique within a repository, and corroborated by metadata. A local artifact
whose name matches no remote sibling is reported, not adopted.

This also yields something the artifact-driven form could not: a companion
present remotely and absent locally is visible, so an operator learns a model
is incomplete before an invocation fails rather than after.

### Ambiguity by type resolves itself; ambiguity by quantization does not

A reference naming a repository that holds one principal and one projector is
not ambiguous — those are parts of one logical unit and reconciliation takes
both. A reference naming a repository that holds two quantizations of the same
model is ambiguous and must stay so, because for `llama-server` those are
genuinely different choices and only the operator can make one.

This is why the current resolver fails on a perfectly ordinary repository: it
inherited the exactly-one-match rule from binding, where the rule is correct,
and applied it to reconciliation, where it is not.

### Usability is derived, not stored

An artifact is usable when it is reconciled and its type is known. A projector
is never independently usable: it is usable only as a companion of a compatible
principal, so a projector whose principal is absent is reported unusable with
that reason. A principal missing its projector stays usable, because it still
serves text-only inference — the projector adds a capability rather than
enabling one.

Usability is computed when the library is read rather than written to the
sidecar, because it depends on which other artifacts are present, and a stored
answer would go stale the moment one of them is added or removed.

### The listing reports facts, not relationships

The tree asserted a one-to-one relation that does not exist. The listing
becomes flat and reports, per artifact, its reference, its declared type, and
its usability. The principal/companion relation is many-to-many and belongs
where the operator resolves it — in the preset — which is why the companion
CLI surface enters this change rather than staying unreachable.

### Triage automates only the unambiguous

`--all` reconciles what needs no decision and reports the rest. Automatically
resolvable means: the repository resolves remotely, every local artifact
matches a remote sibling by name, its content hash agrees, and no ambiguity by
quantization remains. Anything else — a local-only artifact, a hash
disagreement, two principals matching one reference — is reported with its
reason and the exact command that resolves it. A report that lists problems
without naming their remedy reproduces the hand-kept-script workflow in a new
place.

The pass hashes whole files, which on a real library is tens of gigabytes, so
it states the artifact count and total size and asks before starting, with a
flag to accept without prompting.

### Interactivity, not mutation, is what forbids `--json`

`reconcile` prompts, and a command that prompts cannot emit exactly one clean
JSON document. The rule is about interactivity: a mutation that does not ask
may still report machine-readable output, which is why the HTTP analogue of a
mutation returns a body.

### Composition belongs to the port

A CLI command that performs several actions decomposes into several
independently invocable service operations; the CLI composes them in a loop and
a browser would compose them in an RxJS chain. The service exposes the atoms.

The boundary is coherence: what must happen together stays one operation.
Writing an artifact's sidecar is atomic because a partially written unit is an
inconsistent catalog. Reconciling several artifacts of a repository is
composition, because each artifact's reconciliation is independently complete.

## Risks / Trade-offs

**Enumeration now does I/O proportional to library size.** Every listing opens
each artifact and walks its metadata section. This is bounded and local, and
far cheaper than the sidecar parsing it replaced, but it is not free and it is
the reason the array-skipping read is a prerequisite rather than an
optimization.

**Triage will refuse artifacts that are legitimately fine.** An artifact
downloaded from an earlier revision of a repository whose content was later
replaced will fail the hash comparison, and its only remedy in this change is
`--accept`, which records it as `local` and forgives no further update
checking. Supporting `@revision` in reconciliation would fix it and is
deliberately out of scope; the diagnostic must therefore say plainly that the
artifact may be from an earlier revision rather than implying corruption.

**Removing the tree removes the only place the relation was visible.** If the
companion CLI surface is removed rather than exposed, the library will hold
computable companion relations that no command reports. That is acceptable only
as a deliberate choice, not as an oversight.

## Migration Plan

No migration. Sidecars written by the previous change stay valid, and the
listing and reconciliation changes are behavioural. Artifacts already installed
keep their classification; artifacts still unreconciled gain a visible type
without any file being written.

## Open Questions

- Whether the companion CLI surface is exposed or removed is decided when that
  section is reached, not before.
- The spelling of the triage acceptance flag and of the companion subcommands.
