## Why

`preset bind model` and `coquena model list` only ever see a `.gguf` that came
through Coquena's own downloader: `Manager.Library()` silently drops any
artifact whose adjacent `.json`/`.revision` sidecars are missing or do not
match Coquena's own schema. A model directory populated by another tool (for
example LM Studio, which lays out `<publisher>/<repo>/<file>.gguf` in the same
shape Coquena itself uses) is therefore invisible to Coquena even though the
bytes on disk are perfectly usable. This stopped being a hypothetical the
moment LM Studio failed to recognize its own previously-downloaded models after
a reinstall, forcing a multi-gigabyte re-download — the exact failure this
change exists to make unnecessary for Coquena's own users.

Three narrower defects in the existing `user/repo:QUANT` grammar compound the
same problem. The short reference form has no way to pin a revision, so it is
strictly weaker than the full resolve URL. Its `:QUANT` selector matches by
case-insensitive substring, so a persisted preset binding can silently become
ambiguous the day a publisher uploads a similarly-named file. And nothing
Coquena stores lets it tell whether the bytes on disk still match what Hugging
Face currently serves at that path — a publisher can replace a file's content
under the same filename and revision alias (as Google did for a Gemma 4
release) without anything in the existing sidecar detecting it, because
`Artifact.SHA256` is declared in the schema but never computed or written by
any code path.

## What Changes

- Give the short `user/repo:QUANT` reference form a revision axis, so a
  revision other than `main` is expressible without the full resolve URL.
- Add an exact-match selector mode alongside the existing substring match, so a
  persisted reference can be pinned against future ambiguity.
- Stop silently discarding installed `.gguf` files whose sidecars are missing
  or invalid: report them from `coquena model list` in a distinct
  `unreconciled` state, derived best-effort from their path, and refuse to bind
  an unreconciled model to a preset.
- Add `coquena model reconcile <path>`, with an `--online` mode that matches
  the file against Hugging Face and writes a full Coquena sidecar, and an
  `--accept` mode that adopts the file offline under a new `local` provider
  with no attested revision or URL.
- Compute and persist `Artifact.SHA256` for every model Coquena downloads or
  reconciles online, closing the dead field left by the current schema.
- Add `coquena model verify <ref>`, which re-hashes the local file and compares
  it against the persisted `SHA256`, marking the model unusable on mismatch
  with the option to re-download or reconcile again.
- Add `coquena model check-updates <ref>`, which consults the Hugging Face API
  using only the information already in the sidecar — no local hashing, no
  network access from `model list` itself — to report whether the currently
  installed content has fallen behind what Hugging Face now serves at the same
  path.
- **BREAKING**: an unresolvable or unreconciled model can no longer be bound to
  a preset; `preset bind model` now requires either an installed, reconciled
  artifact or `--allow-missing`.

## Capabilities

### Modified Capabilities
- `gguf-model-resolution`: add a revision axis and an exact-match selector mode
  to the `user/repo:QUANT` grammar.
- `gguf-model-library`: stop discarding artifacts with missing or invalid
  sidecars; add the `local` provider and the `unreconciled` state; compute and
  persist `Artifact.SHA256`.
- `gguf-model-interfaces`: add `coquena model reconcile`, `coquena model
  verify`, and `coquena model check-updates`; report `unreconciled` entries
  from `coquena model list`.

## Impact

- Affects `internal/model` (reference grammar, library scan, sidecar schema,
  Hugging Face resolver) and `cmd/coquena` (new `model` subcommands).
- Affects `internal/presets` (`BindModel` refuses an unreconciled reference).
- Adds no new persisted document; extends the existing per-artifact JSON
  sidecar and introduces no SQLite or repository-wide metadata file.
- Does not execute `llama-server`, does not make `model list` perform network
  access or local hashing on its own, and does not add automatic background
  update checking.
