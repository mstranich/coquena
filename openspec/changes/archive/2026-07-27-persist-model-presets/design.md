## Context

`internal/fields` persists the field catalog as `config/llamacpp-fields.json`
with its own `schemaVersion`, published through the established temporary-file
and rename sequence, and serves it through a per-entity REST collection at
`/api/fields` mirroring the `coquena field` command family. Presets are the next
consumer of that vocabulary and the last piece missing before an INI can be
rendered.

Upstream `docs/preset.md` documents the target artifact. A `--models-preset` INI
has a `[*]` section supplying defaults that named sections override, its section
names are the model identifiers exposed at `/v1/models` and accepted in the
`model` field of OpenAI-compatible requests, a local model is referenced with
`model = <path>` and a remote one with `hf = user/repo:QUANT`, and its keys are
llama.cpp argument names without leading dashes. Upstream also warns that flag
and parameter names have changed across builds.

Two facts about the existing model code shape this Change. `Manager.Library()`
enumerates installed artifacts and `Manager.InstalledMetadata` matches them by
exact `(repository, filename)`, so that pair is already the durable installed
identity. And `parseRef` resolves `user/repo:QUANT` by case-insensitive
*substring* match over candidate filenames, requiring exactly one match — a
convenience for an interactive download, but not an identifier: a later upstream
publication can make the same string match two artifacts and fail.

## Goals / Non-Goals

**Goals:**

- Make presets survive reloads, restarts, and a moved models directory.
- Express both associations a preset needs: to a model, and to the builds that
  can run it.
- Render a byte-for-byte deterministic INI from persisted presets.
- Keep authoring possible offline and without a runtime installed.
- Choose preset identity now so that renaming and later build registration do not
  require migration.

**Non-Goals:**

- Executing `llama-server`, passing it the rendered INI, or supervising it.
- Registering non-official runtime builds.
- Repairing the two known defects of the short model reference grammar.
- Reading, importing, or reconciling an INI written outside Coquena.
- Preset sharing, export, or Hugging Face `preset.ini` discovery.

## Decisions

### The section name is the identity, so it is validated, not decorated

A preset's name is its INI section name, which upstream makes the model
identifier served at `/v1/models` and quoted in the `model` field of every
request. That name therefore travels over HTTP, lands in client configuration
files, and appears in logs and URLs. It is validated as lowercase segments
separated by `-` or `.`, and a separate optional `title` carries display text.

Whether an INI parser tolerates `[Qwen3 Fast]` is not the deciding question; the
name is a network identifier before it is a label. This mirrors the field
catalog, where the canonical INI key is identity and aliases and display are
separate concerns.

The dot is admitted alongside the hyphen because upstream's own
`docs/preset.md` names a real model `Qwen3.5-4B`: a dotted version number is
not a hypothetical, and a preset name that could not represent it would force
every operator to mangle a version they didn't choose. Casing stays
lowercase-only regardless of upstream's example, because Coquena mints this
identifier itself and a predictable case avoids two presets differing only by
case colliding in a case-insensitive client or filesystem. `title` carries the
casing an operator wants to see.

`global` and `render` are reserved and refused as preset names. `global` names
the `[*]` section wherever a preset name is accepted, which avoids quoting `*` in
a shell. `render` is reserved because the WebUI routes `/api/presets/render` and
path routing would otherwise read it as a preset name.

### `define` creates, and every part has one idempotent mutator

`coquena field define --overwrite` upserts because a field is atomic: its entire
state is the flags of one command. A preset is composite — it also changes
through `set` and `unset` — so a whole-entity overwrite has no defined meaning
with respect to values already authored. Either answer surprises someone, and the
destructive answer surprises expensively.

So `preset define` refuses an existing name, and correcting a mistake means
re-running the mutator for the part that is wrong: `bind model`, `bind runtime`,
`set`, `unset`, `set --title`. There is no `--overwrite` anywhere in the family
and, correspondingly, no `PUT /api/presets/<name>`. This is a deliberate
divergence from the field catalog, justified by atomic versus composite.

Renaming needs its own operation because `remove` and `define` would discard
every authored value, and the name is a served identifier that will be corrected
in practice.

### `bind` references another entity; `set` stores a literal

A preset's parts split on one rule. `bind` targets a reference to something else
in the system that must resolve and can drift — a model in the library, a runtime
capability — so it carries validation and diagnostics. `set` stores a value as
given. That is why `--title` is `set` and the model is `bind`, and it decides
where any future part belongs without another debate.

### The model binding stores intent and resolution, both

`preset bind model` accepts the `user/repo:QUANT` grammar the CLI already
accepts, and stores two layers:

- `requested` — what the operator meant: provider, repository, quant, and the
  raw string. Durable, never rewritten.
- `resolved` — what it physically matched: the exact filename, resolved
  revision, and a timestamp. An advisory attestation, re-derivable.

Storing only `requested` would mean re-running a substring match at render time,
so an upstream publication could break a preset that never changed. Storing only
`resolved` would lose the operator's intent and could not render `hf =` for an
artifact that is not installed. Keeping both is redundant on purpose, and the
redundancy pays immediately: because `requested` preserves `user/repo:QUANT`
exactly, a preset bound with `--allow-missing` renders correctly with no
resolution at all — `model = <path>` when `resolved` is installed, `hf =
user/repo:QUANT` from `requested` when it is not.

Resolution runs against the installed library and performs no network request. A
binding must not require Hugging Face to be reachable, so this Change adds
offline resolution over `Library()` and the `coquena model list` command that
makes installed artifacts discoverable — `Library()` exists in Go today but was
never exposed as a command.

Drift is reported, not repaired silently: if `resolved.filename` no longer
exists, reads emit a diagnostic and re-running `bind model` re-resolves from the
intact `requested`.

### The runtime binding is a constraint, and most of it is derived

Pinning a preset to a runtime identity was rejected. A runtime is identified by
its primary asset name, which embeds the release tag, so every runtime upgrade
would orphan every preset. Instead a preset declares a capability constraint —
provider, backend, and a release range — with `--id` available as an explicit
hard pin, and no binding at all meaning the preset is portable.

The important part is that the constraint is largely *computable*. Every field
already carries `availability{provider, introducedIn, removedIn}`, so the range
of builds admitting a preset is the intersection of the ranges of the fields it
uses. Reads therefore report two things: the `computed` requirement, derived from
the catalog and attributed to the fields that produced each bound, and the
`declared` requirement, which carries the intent no release range can express —
that `n-gpu-layers 99` is pointless without a CUDA backend.

Both stay advisory, consistent with availability and runtime compatibility
elsewhere: they never block authoring, storing, or rendering.

### Inheritance has two layers, and a field default is not one of them

Inheritance is `global` and a preset's own layer. llama.cpp's compiled-in
behavior is a third layer Coquena neither owns nor replicates, so the field
catalog's `default` attribute plays no part here: it is not inherited, it does
not make an authored value special, and it never causes a key to be omitted from
a rendered INI.

The tempting optimization — omit a value that equals its field's default — is
wrong, and wrong in the one way this project already defends against. Upstream
defaults change between builds; that drift is exactly why fields carry an
availability range. A key omitted today because it matched a default would
silently change the behavior of an unchanged preset on a later build. Emitting
`temp = 0.8` when `0.8` is today's default pins the value against a build that
may disagree.

Having no role in presets, and none in validation either, the `default`
attribute earns nothing but the cost of maintaining a build-specific value
Coquena cannot verify. Removing it from the field catalog is a separate Change;
this one simply specifies no behavior that depends on it.

### Read effective, write own

`GET /api/presets/<name>` returns values already merged with `global` and labeled
with their source, so the browser never re-derives inheritance. Every mutation
endpoint touches only that preset's own layer, which is the only writable one.
One sentence covers the whole read/write asymmetry.

A consequence: there is no `GET` on `/api/presets/<name>/values/<field>`. The
resource there *is* the override, so a read would have to report absence for a
value the operator can plainly see inherited on screen. Effective values are
reported only on the preset representation, and `DELETE` on that path removes the
override — which is exactly what `unset` means.

Because `global` is a reserved name rather than a special route, the same paths
serve it; only operations that cannot apply to it are refused. The collection
response still separates it as `{global, presets[]}` with a `kind` discriminator,
so the browser does not filter a heterogeneous list.

### Rename is `MOVE`

Three shapes were considered. `PUT /api/presets/<name>/name` treats the name as a
property, which is uniform with `title` but writes a resource in order to
relocate it. A `POST .../rename` is honest that renaming is an operation but is
otherwise unprincipled.

`MOVE /api/presets/<name>` with a `Destination` header was adopted. A preset
document is closer to a file than to a record, and in a filesystem a name is not
an attribute of the object — it lives in the enclosing namespace, which is why
POSIX renames with `rename(2)` over two paths rather than by writing to the
inode. WebDAV already made that translation to HTTP, and the method is
standardized in RFC 4918. The router in `internal/webui` dispatches on
`r.Method` directly, so a non-CRUD method costs nothing structurally, and
browsers accept it through `fetch`.

### Durable presets make field removal a cross-document operation

`field remove` deletes a field and its descendant subtree. That was safe while
presets lived in browser memory, because nothing durable could point at a field
that no longer existed. This Change creates exactly that possibility, so it also
has to close it.

Removal is refused when the field or any of its descendants holds a value in a
persisted preset, naming the presets so the operator knows what to look at, and
`--force` cascades: the fields leave the catalog and their values leave every
preset, including `global`. Cascading was chosen over tolerating orphans because
an orphaned value cannot be rendered without inventing a rule for it, and over
silent cascading because dropping authored configuration is exactly the class of
action this project confirms rather than assumes.

Both documents are published, so a failure must leave neither one referencing
content the other dropped.

### The INI is derived, one per runtime, and never a store

A rendering is an output rather than authored state: it cannot represent a
durable preset name separate from a section, a range constraint, a runtime
binding, or the difference between an inherited and an overridden value once
flattened. When it is written at all it lives beneath `data/`, never `config/`.

Rendering does not write by default. Both interfaces report the INI and leave the
filesystem alone: the CLI prints it, and the HTTP endpoint returns it as text,
which keeps a `GET` free of side effects and keeps the two interfaces analogous
rather than merely parallel. Writing is opt-in through `--write`, with an
optional path and `data/servers/<runtime-id>.ini` as the default target. The
inversion matters for the common case, which is inspecting what a preset would
produce — that should not leave a file behind.

It is rendered per runtime, because that is precisely where the runtime binding
materializes: a runtime's INI contains `[*]` plus one section for each preset
whose constraint admits that runtime, and presets that are not admitted are
omitted with a diagnostic naming the reason. Rendering is deterministic — stable
key order, canonical long field names only, never a short argument or an
environment variable alias — so it can be verified against a golden file.

Rendering is a pure function of the persisted document, the field catalog, and
the named runtime's identity. It performs no network request and does not require
the runtime to be installed to produce output for it.

## Risks / Trade-offs

- The rendered INI is not verified against a real `llama-server`, and upstream
  warns key names drift between builds. Accepted: golden-file determinism is
  testable now, execution is a later Change, and the availability range is the
  mechanism that will carry the drift when it appears.
- `MOVE` is unfamiliar and some intermediaries treat non-CRUD methods poorly.
  Accepted for a local single-operator control plane, where no third-party proxy
  sits in the path.
- The substring model selector remains fragile at bind time even though it can no
  longer break at render time. Accepted and recorded: an exact selector and a
  revision axis for the short grammar are separate Changes.
- Preset names are constrained more tightly than upstream requires. Accepted:
  the constraint protects a served identifier, and `title` absorbs the
  expressiveness lost.

## Migration Plan

Presets have never been persisted, so no stored document is being converted. The
browser's in-memory presets are replaced by API-backed state; unsaved browser
state at upgrade time is not carried over, which matches the existing behavior
where a reload already discarded it.

## Open Questions

None.
