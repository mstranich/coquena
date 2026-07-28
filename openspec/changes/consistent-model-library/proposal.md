## Why

The logical name `user/repo:QUANT` is what Coquena manages, and everything
depends on it holding together across one loop: discover a repository, filter
its candidates to the quantization you want, download that quantization into
the library, and end up with a library coherent enough to bind a preset
against. Coquena does not close that loop today.

The break is at the end of it. A library holding artifacts Coquena did not
download reports no type for any of them, because classification is read from
a sidecar a foreign file does not have. The catalog cannot say which artifacts
are principal models, which are vision projectors, or which of them can take
part in a `llama-server` invocation at all — the exact facts a preset needs
before it can bind anything.

Reconciliation, which closes that gap, reconciles one artifact per invocation.
A repository holding a model and its projector is therefore reconciled halfway
by construction, which is the inconsistent state the product exists to prevent.

The listing compounds it by rendering a tree that asserts a relation the domain
does not have: a projector is compatible with every quantization of its
principal, because the criterion is embedding length and that does not change
between quants.

## What Changes

- Classify every artifact when the library is enumerated, reading the
  artifact's own header when no sidecar records a classification, so the
  catalog can tell a principal from a projector without requiring
  reconciliation first.
- Report usability explicitly: an artifact is usable when it is reconciled and
  classified, a projector is usable only as a companion of a compatible
  installed principal, and an artifact whose type cannot be determined is
  reported unusable rather than silently listed.
- Reconcile a repository in one action: resolve it, enumerate its remote
  candidates with their classification, and match them against what is on disk
  by filename corroborated by metadata, so a principal and its companions are
  never left reconciled halfway.
- Report a companion present in the remote repository and absent locally, so an
  incomplete model is visible before an invocation fails.
- Replace the tree listing with a flat listing reporting each artifact's
  declared type and usability.
- Turn off library-wide reconciliation. Reconciliation is per artifact and per
  repository; `--all` stops being accepted.
- Remove `--json` from `reconcile`, which prompts and cannot emit one clean
  document.
- Expose companion assignment through the CLI, so the relation the listing no
  longer draws is resolved where the operator chooses it — in the preset.
- **BREAKING**: `coquena model list` no longer nests companions.
- **BREAKING**: `coquena model reconcile --all` is no longer accepted.

## Non-Goals

- Library-wide reconciliation in any form. The switch is turned off in this
  change and the behaviour is reconsidered in a future one; the code left
  behind is knowingly dead.
- Verifying or repairing artifact content. `coquena model verify` is untouched.
  Integrity is a property of one file; this change is about the library.
- Re-downloading, overwriting, or updating an artifact.
- Filtering or searching the listing.
- Any HTTP API or Angular surface.
- Supporting a local filename that differs from its remote name, or
  reconciling against a revision other than the repository's current one.

## Capabilities

### Modified Capabilities
- `gguf-model-library`: classify an artifact whenever the library is
  enumerated rather than only when a sidecar exists; define artifact usability.
- `gguf-model-interfaces`: reconcile a repository in one action; stop accepting
  library-wide reconciliation; drop `--json` from reconciliation; replace the
  tree listing with a flat listing reporting type and usability.
- `model-preset-persistence`: expose companion assignment and admissible
  companion options through the CLI.

## Impact

- Affects `internal/model` (library enumeration and classification,
  repository-driven reconciliation, usability) and `cmd/coquena` (`reconcile`,
  `list`, and preset companion subcommands).
- Affects `internal/presets` only at its existing companion API, implemented
  and tested but currently unreachable.
- Removes the tree-building logic introduced by
  `classify-models-and-companions`, and disables its library-wide bulk
  reconciliation without deleting it.
- Requires a metadata read that skips array payloads, since classifying on
  every listing must not decode vocabulary arrays.
- Leaves the sidecar format unchanged.
