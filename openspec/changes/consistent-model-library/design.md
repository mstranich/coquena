## Context

The loop this change has to close is: discover a repository, filter to the
quantization you want, download it, and have a library coherent enough to bind
a preset against. Classification and companion criteria already work and are
verified against real artifacts — `general.type` declares whether a file is a
`model` or an `mmproj`, and a projector matches a principal when
`clip.vision.projection_dim` equals the principal's `<arch>.embedding_length`.
What is missing is when classification happens, what unit reconciliation
operates on, and what the catalog is allowed to claim about an artifact.

### Decided by the operator, versus proposed here

The previous change shipped commands whose details were filled in during
implementation rather than agreed beforehand, and the gaps only surfaced when
the library had to be trusted. This section exists so that cannot happen
quietly again.

**Decided by the operator.** The tree is discarded. Reconciliation is per
artifact and per repository, never library-wide; `--all` is turned off in this
change and its code is knowingly left dead. Reconciliation mirrors discovery,
comparing disk against Hugging Face by name and metadata. `reconcile` loses
`--json`. Companion CLI surface enters this change, to be exposed or removed.
Integrity work is out of scope.

**Proposed here, open to veto.** That a projector alone is reported unusable
rather than merely typed. That a principal without its projector stays usable,
since it still serves text-only inference. That usability is derived on read
rather than persisted. Names of new subcommands.

## Goals / Non-Goals

**Goals.** After downloading or reconciling a repository, the catalog can state
for every artifact what it is, whether it is usable, and which companions are
admissible — with no artifact left untyped for a reason Coquena could have
determined on its own.

**Non-Goals.** Library-wide reconciliation, integrity verification and repair,
updating, listing filters, HTTP and Angular surfaces, renamed local artifacts,
and non-current revisions.

## Decisions

### Classification moves to enumeration

`Manager.Library` classifies from the sidecar when one exists and from the
artifact's own header when it does not. Today an unreconciled entry carries no
classification, which is why a real library shows artifacts with an empty type
column and no way to tell a projector from a model.

This does not weaken mandatory reconciliation. Reading a header is an
observation and grants no standing: an unreconciled artifact stays
unreconciled, stays excluded from `ResolveInstalled`, and stays unusable. Only
the operator's visibility changes.

The cost is real. `ReadMetadata` materializes every array into `[]any`, so
classifying a principal decodes a quarter of a million tokens and as many
merges. Enumeration therefore needs a metadata read that skips array payloads:
fixed-width element types by computing the span, string arrays by walking
length prefixes without allocating. The tokenizer digest still needs the array
contents, but only at reconciliation time.

### Reconciliation operates on a repository, never on the library

A repository is the unit Hugging Face publishes and the unit an operator thinks
in. Reconciling one artifact at a time guarantees the half-reconciled state the
product exists to prevent: a principal installed, its projector still foreign,
and no preset able to bind the pair.

So reconciliation resolves the repository, enumerates its remote candidates
with their classification exactly as `discover` does, and matches them against
what is on disk — by filename, which Hugging Face guarantees unique within a
repository, corroborated by metadata. A local artifact matching no remote
sibling is reported, not adopted. A remote candidate absent locally is reported
too, so an incomplete model is visible before an invocation fails.

Library-wide reconciliation is deliberately not part of this. It conflates
cases needing an operator decision with cases that do not, and every attempt to
specify it has expanded rather than closed. The switch is turned off and the
question is reopened in a future change.

### Ambiguity by type resolves itself; ambiguity by quantization does not

A reference naming a repository holding one principal and one projector is not
ambiguous — those are parts of one logical unit and reconciliation takes both.
A reference matching two quantizations of the same model is ambiguous and must
stay so, because for `llama-server` those are genuinely different choices.

This is why the current resolver fails on an ordinary repository: it inherited
the exactly-one-match rule from binding, where it is correct, and applied it to
reconciliation, where it is not.

### Usability is derived, not stored

An artifact is usable when it is reconciled and its type is known. A projector
is never independently usable: it is usable only as a companion of a compatible
principal, so an orphaned projector is reported unusable with that reason. A
principal missing its projector stays usable, because the projector adds a
capability rather than enabling inference.

Usability is computed on read rather than written to the sidecar, because it
depends on which other artifacts are present and a stored answer would go stale
the moment one is added or removed.

### The listing reports facts, not relationships

The tree asserted a one-to-one relation that does not exist. The listing
becomes flat and reports, per artifact, its reference, its declared type, and
its usability. The principal/companion relation is many-to-many and belongs
where the operator resolves it — in the preset — which is why the companion CLI
surface enters this change rather than staying unreachable.

### Interactivity, not mutation, is what forbids `--json`

`reconcile` prompts, and a command that prompts cannot emit exactly one clean
JSON document. The rule is about interactivity: a mutation that does not ask
may still report machine-readable output, which is why the HTTP analogue of a
mutation returns a body. The flag is extracted before subcommand dispatch, so
it must be rejected explicitly rather than silently ignored.

### Composition belongs to the port

A CLI command performing several actions decomposes into several independently
invocable service operations; the CLI composes them in a loop and a browser
would compose them in an RxJS chain. The service exposes the atoms.

The boundary is coherence: what must happen together stays one operation.
Writing an artifact's sidecar is atomic, because a partially written unit is an
inconsistent catalog. Reconciling several artifacts of a repository is
composition, because each artifact's reconciliation is independently complete.

## Risks / Trade-offs

**Enumeration now does I/O proportional to library size.** Every listing opens
each artifact and walks its metadata section. Bounded and local, far cheaper
than the sidecar parsing it replaced, but not free — which is why the
array-skipping read is a prerequisite rather than an optimization.

**Reconciliation refuses artifacts that are legitimately fine.** An artifact
from an earlier revision of a repository whose content was later replaced fails
the hash comparison, and its only remedy here is `--accept`, which records it
as `local` and forfeits update checking. Supporting `@revision` would fix it
and is out of scope, so the diagnostic must say the artifact may be from an
earlier revision rather than implying corruption.

**Dead code is left behind on purpose, twice.** Turning `--all` off, and
turning the model card output off, without deleting either implementation
leaves code no command reaches. Both are deliberate trades against spending
this change's budget on behaviour being reconsidered anyway — the model card is
expected back behind an explicit option, since the problem is that it is
printed unconditionally and buries the candidate list, not that it is
worthless. The tasks record both so a later reader does not mistake unreachable
code for an abandoned implementation.

## Migration Plan

No migration. Sidecars stay valid, and the listing and reconciliation changes
are behavioural. Installed artifacts keep their classification; unreconciled
artifacts gain a visible type without any file being written.

## Open Questions

- Whether the companion CLI surface is exposed or removed is decided when that
  section is reached.
