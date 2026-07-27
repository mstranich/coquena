## Context

`internal/model.Manager.Library()` walks `ModelsDir` for `.gguf` files and
calls `ReadRecord` on each one, which requires an exact-schema `.json` sidecar
(`schemaVersion == 1`, `Source.Validate()` passing with `provider ==
"huggingface"`) plus a `.revision` sidecar whose contents equal
`resolvedRevision + "\n"`. Any `.gguf` that fails either check is dropped from
the walk callback with a bare `return nil` — not reported, not counted, not
distinguished from "does not exist."

`parseRef` (`internal/model/huggingface.go`) accepts either a full Hugging
Face resolve URL, which can pin any revision, or the short `user/repo:QUANT`
form, which always resolves `requested = "main"` and matches `QUANT` against
candidate filenames with `strings.Contains`. `ResolveInstalled`
(`internal/model/offline.go`) reuses the same `parseRef` against the local
library, so both defects apply equally offline.

`Artifact.SHA256` exists in `internal/model/types.go` but no code path writes
it: `Manager.download` only records `ETag`/`Last-Modified` from the HTTP
response, and `hfInfo.Siblings` in `huggingface.go` only requests `rfilename`
and `size` from the Hugging Face API, never the LFS `oid` that would give an
authoritative content hash for the resolved revision.

`internal/presets.BindModel` already refuses to bind a reference `Resolve
Installed` cannot match, unless `--allow-missing` is passed — so "not
reconciled" naturally already means "not bindable" once `Library()` reports an
unreconciled entry with a state other than `installed`; no new refusal path is
needed in the presets package itself.

## Goals / Non-Goals

**Goals:**

- Make every `.gguf` beneath the configured models directory visible through
  `coquena model list`, whether or not it was downloaded by Coquena.
- Give an operator an explicit, deliberate way to adopt a foreign or
  sidecar-less model, with or without contacting Hugging Face.
- Make the short reference grammar capable of pinning a revision and of an
  exact, non-drifting selector.
- Detect two independent failure modes on demand: local disk corruption
  (hash mismatch against what Coquena itself recorded) and remote content
  drift (Hugging Face now serves different bytes at the same path).
- Keep every existing installed model, preset binding, and rendered INI
  working unchanged.

**Non-Goals:**

- Automatic, unprompted hashing or network access from `coquena model list`.
  Both `verify` and `check-updates` are always operator-invoked.
- A generic plugin system for recognizing other tools' sidecar formats.
  Reconciliation always produces Coquena's own sidecar shape; it does not learn
  LM Studio's or any other tool's metadata format.
- Repairing corruption automatically. `verify` reports; the operator decides
  whether to re-download or re-reconcile.
- Executing `llama-server` or any change to how presets render an INI.

## Decisions

### Revision axis in the short form: `user/repo@revision:QUANT`

The short form gains an optional `@revision` segment between the repository
and the quant selector, parsed before the existing `:` split. Omitting it keeps
today's behavior (`requested = "main"`) exactly as is, so every existing
preset binding and every existing test string continues to parse identically.
`@` was chosen over overloading `:` or adding a flag because the reference is a
single string that gets persisted verbatim in `ModelBinding.Requested.Raw` and
rendered back into `preset show`; a flag would require threading a second
argument through every call site that accepts a reference, including the
persisted binding itself.

### Exact-match selector: triggered by a full filename, not a new symbol

`ResolveInstalled` and `Resolve` currently require a selector to resolve to
exactly one candidate under `strings.Contains`, which is what makes the
grammar fragile. Rather than invent new punctuation, a selector that ends in
`.gguf` (case-insensitive) is matched by exact equality against the candidate
filename instead of substring containment. This is unambiguous — no current
substring selector is a complete filename with extension — and it is exactly
what an operator would naturally type when disambiguating: the full name
Coquena already reports in `preset show`'s `-> <filename>` line.

### Unreconciled entries: listed, never bindable

`Library()` stops discarding a `.gguf` whose `ReadRecord` fails. Instead it
derives a best-effort `Source` from the path alone — `Repository` from the two
path segments above the file (mirroring the `<publisher>/<repo>/<file>.gguf`
shape both Coquena and LM Studio use), `Filename` from the base name, no
`Provider`, no `ResolvedRevision`, no `URL` — and reports it with `State:
"unreconciled"`. `ResolveInstalled` already filters on `entry.State !=
"installed"`, so an unreconciled entry is automatically invisible to `preset
bind model` without `--allow-missing` and requires no new refusal logic; it
becomes visible, and explicitly not usable, purely by being listed with a
non-`installed` state.

### `coquena model reconcile <path>`: exactly one of `--online` or `--accept`

- `--online` treats the derived `Repository` as a candidate, queries Hugging
  Face's revision info the same way `Resolve` does, locates the sibling whose
  filename matches, hashes the local file, and compares that hash against the
  LFS `oid` Hugging Face reports for that sibling when one is available. On
  agreement it writes the same `.json`/`.revision` sidecar pair a normal
  download produces, with `Artifact.SHA256` populated from the just-computed
  local hash. On disagreement it refuses and reports the mismatch rather than
  writing a sidecar that asserts a false identity.
- `--accept` performs no network access. It writes a reduced sidecar with a new
  `provider: "local"`, the path-derived `Repository`/`Filename`, no
  `ResolvedRevision`/`URL`, and `Artifact.SHA256` computed from the local file
  so a later `verify` has a baseline. `Source.Validate()` is relaxed to accept
  `provider == "local"` without the Hugging-Face-only fields it otherwise
  requires; every other validation (path safety, no traversal) applies
  unchanged.

A `local`-provider entry's `State` becomes `installed` — it is now bindable —
but `check-updates` refuses on it: there is no Hugging Face identity to check
against until an operator later reconciles it `--online`.

### SHA256 becomes a real field, computed at the point content is trusted

`Artifact.SHA256` is populated in exactly two places: at the end of a normal
Coquena-managed download (streaming hash alongside the existing write, no
extra pass over the file) and at the end of a successful `reconcile` (either
mode, since both read the whole file once to establish or confirm identity).
It is never computed implicitly by `model list`, which only stats and walks
paths.

### `coquena model verify <ref>`: local integrity against the recorded hash

Re-hashes the file at the resolved path and compares against the sidecar's
`Artifact.SHA256`. Three outcomes: match (reports verified), mismatch (marks
the model unusable, surfaces the option to re-download the same source or run
`reconcile` again), and no baseline recorded (a model installed before this
change, or reconciled by a build predating it) — reported distinctly from a
mismatch so an operator is not told a legitimate file is corrupt merely
because it predates hashing.

### `coquena model check-updates <ref>`: remote drift, no local I/O on the model file

Reads only the sidecar's `Repository`, `RequestedRevision`, and `Filename`,
queries Hugging Face's current revision info the same way `Resolve` does, and
compares the current commit SHA and, when available, the sibling's LFS `oid`
against what is recorded locally. It never opens or hashes the local `.gguf`.
Refuses with a clear diagnostic on a `local`-provider entry, since there is
nothing recorded to compare against Hugging Face. This is the direct answer to
the Gemma-4-shaped failure: a publisher can replace a file under the same
`main` alias, and this command is how an operator finds out, on demand.

### The content hash joins `.json`; `.revision` stays a separate file

`Artifact.SHA256` is a new field on the existing `.json` sidecar, not a new
sidecar file. `.revision` is not touched, and is not deprecated in favor of a
`.json` field, even though `Source.ResolvedRevision` already lives in `.json`
and looks redundant with it. The redundancy is deliberate and predates this
change: `Publish` renames the `.gguf`, `.json`, and `.revision` files
atomically but *separately*, one after another. A crash between the `.json`
rename and the `.revision` rename leaves a `.json` that is well-formed and
internally consistent but stale — nothing inside a single file can detect that
it disagrees with a sibling write that did not complete. `.revision` exists
specifically to be that independent witness; `ReadRecord` treats disagreement
between the two as `ErrInconsistentRevision` rather than trusting either one.
Folding it into `.json` would remove the only mechanism that catches a torn
three-file publish.

The content hash has no such prior witness to preserve — `Artifact.SHA256` was
declared in the schema and never written by any code path, so adding it to
`.json` loses nothing that existed before. It also answers a different
question than `.revision` does: `.revision` cross-checks the sidecar's own
consistency; `Artifact.SHA256` checks the model *file's* bytes against what
the sidecar (however it was produced — download or reconcile) recorded when
it was trusted.

## Risks / Trade-offs

- Deriving `Repository` from path segments for unreconciled entries assumes the
  `<publisher>/<repo>/<file>.gguf` shape. A model dropped directly under the
  models directory root, or nested more deeply, derives a poor or empty
  repository guess; it is still listed as `unreconciled` with whatever partial
  information is available, and `reconcile` is where the operator corrects it,
  not `list`.
- Hashing a multi-gigabyte file is not free. `verify` and `reconcile --online`
  are the only operations that pay this cost, both explicitly operator-invoked,
  matching the existing project preference for expensive checks to be
  on-demand rather than ambient.
- Hugging Face does not always expose an LFS `oid` for every sibling (small,
  non-LFS-tracked files use Git blob hashing instead). Both `reconcile
  --online` and `check-updates` degrade to commit-SHA-only comparison when no
  LFS `oid` is available, and report that the comparison is partial rather than
  silently treating it as a full content match.

## Migration

No existing sidecar, preset binding, or rendered INI changes shape. Models
already installed with valid Coquena sidecars keep `State: "installed"` and an
empty `Artifact.SHA256` until the next download or `reconcile`; `verify`
reports "no baseline" for them rather than a false mismatch. No data
migration step is required.
