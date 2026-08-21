## Why

The listing reports two states, `installed` and `unreconciled`. The decision
[`model-artifact-states`](../../../docs/decisions/model-artifact-states.md)
settled seven and retired both: `installed` conflated presence, local adoption
and remote provenance, and `unreconciled` is named after a command that is being
split. `establish-sidecar-authority` has since landed the evidence those seven
states are read from, so the vocabulary is derivable today and the code still
speaks the retired one.

The diagram beside that decision,
[`model-artifact-state-diagram.puml`](../../../docs/decisions/model-artifact-state-diagram.puml),
is normative and describes the system Coquena must reach, not the one it is. It
derives `local` from `import` and `synced` from `reconcile` — two commands that
do not exist, because today there is one `reconcile` with `--accept` and
`--online` modes. Shipping the states without the commands would leave the
listing explaining itself in words its own CLI contradicts. The naming half was
settled the same day, in `align-model-command-surface`; this change carries both.

## What Changes

**BREAKING throughout. No compatibility path, no transitional errors, no
deprecation aliases.** Coquena has no release, so a retired name is deleted
rather than redirected.

**States**

- `STATE` becomes the seven derived values `untracked`, `altered`, `incomplete`,
  `local`, `detached`, `synced`, `upgradable`, computed by one function that
  implements the diagram's cascade in the diagram's order.
- The strings `"installed"` and `"unreconciled"` are removed from every use —
  sort key, filter, ordering rule, usability gate, preset companion selection,
  reference resolution and reconciliation outcomes — not only from what is
  rendered.
- `LibraryEntry.State` stops carrying two vocabularies. A download job's status
  moves to its own field; an in-flight job is not an artifact state, and the
  cascade begins at an artifact on disk.
- The `UnusableReason` enumeration is deleted. `usable` is derived wholly from
  the state, which is what the decision already claims and the code does not do.
  `Advice` survives: it never bore on eligibility.

**Classification failure becomes an error**

- Coquena SHALL NOT write a sidecar it cannot classify. `deriveFile` returns an
  error instead of degrading to `TypeUnknown`, and the two sidecar writers, the
  derivation repair in `verify`, and the remote inspection in repository
  reconciliation all fail and exit rather than persisting an unknown type.
- An artifact that cannot be classified therefore has no sidecar and reads as
  `untracked`. `unclassifiable` stops being a usability reason because a managed
  artifact without a type stops being representable.
- This closes the Gemma 4 witness path in a narrow way: a failed remote
  inspection no longer degrades a member's type and silently shrinks the
  reconciliation target set. It fails. Representing the failure as an incomplete
  bundle belongs to `address-quantization-bundles`.

**Commands**

- `reconcile --accept` and `reconcile --online` are replaced by two commands with
  one responsibility each: **`import`, local only** and **`reconcile`, online
  only**. There are no mode flags, because the mode is the command.
- `check-updates` is renamed `update`. Its behaviour is untouched.
- `--with-companions` is removed. A bundle is not optional.
- The `resolve` and `revision` subcommands are removed from the operator surface.

## Capabilities

### New Capabilities

None. This change re-words and re-partitions behaviour that two existing
capabilities already own.

### Modified Capabilities

- `gguf-model-library`: the state vocabulary replaces `installed`/`unreconciled`;
  usability is derived wholly from the state and its reason enumeration is
  removed; classification failure refuses to write a sidecar instead of recording
  an unknown type.
- `gguf-model-interfaces`: `reconcile` splits into `import` and `reconcile`;
  `check-updates` becomes `update`; `--with-companions`, `resolve` and `revision`
  are removed; the listing reports the seven states in both human and JSON output.

## Impact

**Code.** `internal/model/library.go` (`deriveFile`, `Publish`,
`PublishInPlace`), `internal/model/manager.go` (enumeration, job/artifact state
split), `internal/model/usability.go` (rewritten), `internal/model/offline.go`,
`internal/model/reconcile.go`, `internal/model/reconcile_repository.go`,
`internal/model/verify.go`, `internal/presets/companion.go`, and
`cmd/coquena/model.go` (dispatcher, usage string, sort priority, filter,
rendering, JSON shape).

**Operator-visible.** The `STATE` column and the `usable`/`reason` fields of
`model list --json` change vocabulary. Four command spellings change or
disappear. `docs/model-commands.md` is rewritten.

**Not affected.** The sidecar schema. `STATE` is derived and never persisted, so
this change migrates no stored data.

## Non-goals

- **Deriving `incomplete`.** It requires knowing an artifact's shards, and
  Coquena has no notion of a shard. The cascade includes its gate; the predicate
  is unreachable until `address-quantization-bundles` supplies one.
- **Update and upgrade semantics.** `update` is a rename here. What it observes
  and persists is `detect-model-updates`; applying what it finds is
  `support-model-upgrade`.
- **The bundle as the addressable unit**, which is `address-quantization-bundles`.
- **Preventing a preset from binding an untyped artifact.** `Principal()` accepts
  any entry with a non-nil classification and never consults usability. Refusing
  to write an unclassifiable sidecar removes the artifacts that would exercise
  that hole, but the hole itself is separate work.
- **Redefining the evidence axes**, settled by `establish-sidecar-authority`.
