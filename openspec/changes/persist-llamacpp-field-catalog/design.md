## Context

`apps/web/src/app/catalog-store.ts` holds the field catalog and presets as
Angular signals, and the current `llamacpp-configuration-catalog-ui`
specification requires that slice to stay in browser memory. That constraint was
ergonomic rather than architectural: it kept the MVP from writing and deleting a
file on every authoring iteration. Coquena already persists installation state
as an atomically published JSON document in `internal/installation`, exposes
local settings through `/api/config/*`, and manages immutable llama.cpp runtime
installations identified by release tag, target, backend, and exact asset name.

The eventual consumer of this catalog is documented upstream. `llama-server`
router mode accepts `--models-preset <path>` pointing at an INI file whose
section names are model identifiers, whose `[*]` section supplies defaults, and
whose keys are llama.cpp argument names without leading dashes. Upstream accepts
the long argument, the short argument, and the environment variable name as
equivalent keys, and states that parameter names have changed between builds.
This Change persists only the field vocabulary; presets and INI rendering are
separate Changes.

## Goals / Non-Goals

**Goals:**

- Make the field catalog survive reloads, restarts, and upgrades.
- Let a field declare its kind and which llama.cpp builds admit it.
- Make a catalog reproducible and shareable between installations.
- Choose field identity and availability keys now that will not require
  migration when custom runtime builds are registered.

**Non-Goals:**

- Persisting presets, preset inheritance, or preset values.
- Rendering, reading, or validating a `--models-preset` INI file.
- Registering non-official runtime builds or evaluating build-scoped fields.
- Deriving fields by parsing `--help`, reading llama.cpp source, executing a
  runtime, or inspecting a binary.
- Embedding a curated catalog in the executable.
- A general configuration-scripting facility beyond the seed script.

## Decisions

### Persist one catalog, seed it with a replayable script

There is one catalog document, `config/llamacpp-fields.json`, written beneath
the application home with its own `schemaVersion` starting at `1` and published
through the existing temporary-file-and-rename sequence. An installation with no
document resolves an empty catalog.

A curated catalog embedded in the executable was considered and dropped. It
would have introduced an origin axis on every field, read-only refusals on every
mutation path, and a baseline-version reconciliation between shipped and
authored content — a large amount of structure whose only purpose was to deliver
a starting set of fields. A script of `field define --overwrite` commands
delivers the same starting set with none of it, and stays inspectable and
editable by the operator.

The document is deliberately not merged into `config/coquena.json`. That
document is the installation contract at schema version 3 and is read during
initialization; coupling a frequently edited vocabulary to it would force an
installation schema bump on every catalog format change.

Deriving the catalog from `llama-server --help` output or from llama.cpp source
was rejected outright. Both are brittle against the same cross-build parameter
drift the availability range exists to record, and the cost is far out of
proportion to typing a field definition once.

### Make definition an upsert

`field define --overwrite` and `PUT /api/fields/<name>` create the field when it
is absent and replace it when it is present.

Upsert is what makes a catalog portable. A script of definitions can be replayed
against a fresh installation, re-run after editing one value, or shared with
another operator, and it converges to the same catalog every time. Without it,
sharing a catalog would require the recipient to know which fields they already
have.

Definition without `--overwrite` still refuses an existing name, so an
accidental redefinition is caught. On an interactive terminal Coquena asks for
confirmation; when the terminal is not interactive it fails with a diagnostic
rather than prompting, so a replayed script never blocks on a question nobody
can answer.

### Identify a field by its canonical name

The canonical argument name is the field's identity: the long INI key, without
leading dashes, unique across the catalog. There is no separate generated
identifier.

A generated identifier was considered and rejected. Its only justification was
protecting preset entries from a rename, but a field's name is not a label over
a stable thing — it is the key literally emitted into the INI. Renaming
`ctx-size` to `context-size` produces a different argument, so a preset entry
that silently followed the rename would now emit a key the runtime may not
admit. Treating the rename as what it is — removing one field and defining
another — is both simpler and more honest, and it keeps the persisted document
readable, diffable, and hand-editable.

Short arguments and environment variable names are recorded as aliases of the
same field rather than as separate fields, because upstream accepts all three
forms as the same key. Coquena always treats the long name as canonical, which
keeps a future rendered INI stable and diffable.

### Require the kind and state it first

Every field declares exactly one immutable kind. The kind is not decoration: it
is what lets Coquena validate a preset value before that value reaches the INI,
where a wrong type is only discovered by the runtime at startup.

The CLI therefore states the kind before the name, the way a declaration states
a type before an identifier, so `field define integer context-size` reads as the
declaration it is.

### Qualify availability by provider from the start

Availability is `{ provider, introducedIn, removedIn }`. The only provider in
this Change is the official llama.cpp distribution, and `introducedIn` and
`removedIn` are official `b<digits>` build tags, which are monotonic and
therefore cheap to compare as ranges.

The provider dimension is introduced now even though only one provider exists,
because a later Change will register custom runtime builds, and re-keying both
the catalog and the presets that reference it after the fact would be a
migration of exactly the data this Change exists to protect. Only the range axis
is implemented and exercised; no build-scoped evaluation is built ahead of the
requirement that needs it.

Availability resolves to `admitted`, `not-admitted`, or `unknown`, reusing the
three-valued vocabulary already established by runtime host compatibility rather
than inventing a second one. It is always advisory and never blocks authoring.

### Address fields as resources under `/api/fields`

The established convention maps a CLI noun to an API collection: `coquena model`
to `/api/models`, `coquena runtime` to `/api/runtimes/llama.cpp`, and
`coquena config` to `/api/config/*`. `coquena field` therefore serves
`/api/fields`, with the collection listing and defining, and `/api/fields/<name>`
reading, upserting, and deleting one field.

A whole-document `PUT` replacing the catalog in one call was rejected: it has no
CLI counterpart, so the two interfaces would stop being analogous, and it would
make a single-field edit submit state the caller did not author.

Per-resource verbs do not weaken validation. Every mutation is validated against
the whole catalog before publication — uniqueness, alias collisions, and parent
references — and the document is still published atomically as one file.

## Risks / Trade-offs

- [A fresh installation has an empty catalog] → Accepted and visible. The seed
  script populates it in one command, and an empty catalog is a clearer starting
  state than a shipped one whose provenance the operator cannot inspect.
- [A seeded availability range is wrong and mislabels a valid argument] →
  Availability never blocks authoring, and a later Change validates presets
  against a runtime before execution.
- [Upsert silently replaces a field an operator tuned by hand] → `--overwrite`
  is explicit, and definition without it refuses or asks.
- [Concurrent single-field edits interleave] → The catalog is a single-operator
  local document; each mutation validates against the current catalog and
  publishes only after validation succeeds.
- [The catalog file is hand-edited into an invalid state] → Reads fail with an
  actionable diagnostic identifying the document, and Coquena does not silently
  discard or rewrite operator-authored content.
- [Persisting fields before presets leaves an unused capability] → Accepted; the
  catalog is the smaller and independently verifiable half, and presets are
  meaningless without a durable vocabulary to reference.

## Migration Plan

1. Install the updated executable. No `coquena init` change is required.
2. An installation with no catalog document resolves an empty catalog.
3. Running the seed script publishes `config/llamacpp-fields.json` at schema
   version 1 and is safe to re-run.
4. Field definitions previously held in browser memory are not migrated; they
   were never durable and are re-authored through the CLI or the Fields
   workspace.
5. Roll back by removing the catalog document; no other Coquena state is
   affected.
