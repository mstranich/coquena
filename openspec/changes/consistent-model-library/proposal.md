## Why

Coquena exists so that running `llama-server` stops being a craft of hand-kept
scripts and becomes something close to a production environment. That depends
on one thing the library cannot do today: answer, for every artifact it holds,
what the artifact is and whether it can take part in a usable invocation.

A real library exposes the gap immediately. Nine GGUF files written by another
application report no type at all, because classification is read from a
sidecar that a foreign file does not have. The catalog therefore cannot say
which of them are principal models, which are vision projectors, or which
projector serves which model — the exact facts a preset needs before it can
bind anything.

Reconciliation, which is the operation that would close that gap, cannot be
driven far enough to close it. It reconciles one artifact per invocation, so a
repository holding a model and its projector is reconciled halfway by
construction, leaving the library in precisely the inconsistent state the
product exists to prevent. Its bulk mode reconciles the entire models
directory instead of a repository, which was never the intent and conflates
cases needing an operator decision with cases that do not. And it matches a
single sibling by filename rather than exploring the repository the way
`discover` and `download --with-companions` already do, so it cannot report
that a principal's companion exists remotely but is missing locally.

The listing compounds this by rendering a tree that asserts a relationship the
domain does not have. A projector is compatible with every quantization of its
principal, because the criterion is embedding length and that does not change
between quants. Nesting it beneath one of them is arbitrary, and actively
misleading once a library holds two quantizations of the same model.

## What Changes

- Classify every artifact from its own header when the library is enumerated,
  so an unreconciled file reports what it is before anything is written about
  it, and the catalog can distinguish a principal from a projector without
  requiring reconciliation first.
- Drive reconciliation by repository rather than by artifact: resolve the
  repository, enumerate its remote candidates with their classification, and
  match them against what is on disk by filename and metadata, reconciling the
  principal and its companions in one action.
- Report a companion that exists in the remote repository but is absent
  locally, so an operator learns that a model is incomplete rather than
  discovering it when an invocation fails.
- Replace the tree listing with a flat listing that reports each artifact's
  declared type and whether it is usable, since the principal/companion
  relation is many-to-many and cannot be expressed by nesting.
- Report usability explicitly: an artifact is usable when it is reconciled and
  classified, a projector is usable only as a companion of a compatible
  principal, and an artifact whose type cannot be determined is reported as
  unusable rather than silently listed.
- Redefine `--all` as an online triage over the library: reconcile every
  repository that can be resolved without an operator decision, and report the
  remainder with the reason and the command that resolves each one.
- Warn before a triage pass that will hash a large library and require
  confirmation, with an explicit flag to accept without prompting.
- Restrict `--accept` to a single artifact. Accepting an artifact is a claim
  about that artifact's identity, which cannot be made in bulk.
- Remove `--json` from `reconcile`, which prompts and therefore cannot emit one
  clean document.
- Expose companion assignment through the CLI, so the principal/companion
  relation the listing no longer draws is resolved where the operator actually
  chooses it.
- **BREAKING**: `coquena model list` no longer nests companions.
- **BREAKING**: `coquena model reconcile --all` reconciles resolvable
  repositories online instead of every artifact in the models directory, and no
  longer accepts `--accept` or `--json`.

## Non-Goals

- Verifying or repairing artifact content. `coquena model verify` keeps its
  current behaviour untouched, including the persisted invalid state its
  original design called for and its implementation never wrote. Integrity is a
  property of one file; this change is about the consistency of the library.
- Re-downloading, overwriting, or updating an artifact. That belongs to a
  future `update`/`upgrade` command.
- Filtering or searching the listing.
- Any HTTP API or Angular surface. The ports stay as they are; only the CLI and
  the shared service change.
- Supporting an artifact whose local filename differs from its remote name.
- Reconciling against a revision other than the repository's current one.

## Capabilities

### Modified Capabilities
- `gguf-model-library`: classify an artifact whenever the library is
  enumerated rather than only when a sidecar exists; define artifact usability.
- `gguf-model-interfaces`: drive reconciliation by repository; redefine `--all`
  as online triage; restrict `--accept` to one artifact; drop `--json` from
  reconciliation; replace the tree listing with a flat listing reporting type
  and usability.
- `model-preset-persistence`: expose companion assignment and admissible
  companion options through the CLI.

## Impact

- Affects `internal/model` (library enumeration and classification,
  repository-driven reconciliation, usability) and `cmd/coquena` (`reconcile`,
  `list`, and new preset companion subcommands).
- Affects `internal/presets` only at its existing companion API, which is
  implemented and tested but currently unreachable.
- Removes the tree-building logic and the library-wide bulk reconciliation
  introduced by `classify-models-and-companions`, which manual testing found to
  be the wrong design.
- Requires a metadata read that skips array payloads, since classifying on
  every listing must not decode vocabulary arrays.
- Leaves the sidecar format unchanged.
