# Adopt the settled artifact state vocabulary

## Status

- **State:** closed
- **Opened:** 2026-08-11
- **Closed:** 2026-08-20
- **Owner:** project-owner
- **Priority:** high
- **Story step:** the whole story, as what the operator reads
- **Candidate change:** `adopt-artifact-state-vocabulary`

## Outcome

The seven states are derived and rendered, `installed` and `unreconciled` are
gone from the codebase rather than from the output alone, and `usable` is derived
wholly from the state. The change absorbed `align-model-command-surface`, because
the diagram derives its states from commands that did not exist yet.

Six of the seven states derive. `incomplete` does not: deciding that a principal
is missing pieces requires knowing which files constitute a quantization, and
Coquena has no notion of a shard. Its gate keeps its place in the cascade with a
named predicate, `principalIsIncomplete`, that `address-quantization-bundles`
replaces.

### What was ruled out

- **Deriving each state independently and asserting exactly one holds.**
  Rejected: the invariant is false by construction. An artifact whose bytes were
  found altered can carry a perfectly valid attestation, so `altered` and
  `synced` genuinely co-occur in the evidence. The cascade's *order* is what
  decides the answer, and it is normative.
- **Omitting the `incomplete` branch until something can reach it.** Rejected:
  its position — after altered bytes, before anything about the repository — is
  the part a later change would most easily get wrong. Reinstating it later
  means re-deriving an ordering that is settled now.
- **Making `unclassifiable` an eighth unusable state.** Rejected by the project
  owner in favour of a stronger rule: if Coquena cannot classify an artifact it
  errors and writes no sidecar. The artifact then has no sidecar and reads as
  `untracked`, which the vocabulary already covers, so the case stops being
  representable rather than gaining a name.
- **Mapping `unclassifiable` into `incomplete`.** Considered and dropped with
  the above. It was also a category error: a valid GGUF declaring an unrecognised
  type is not missing any piece, and `incomplete` is about composition.
- **A transitional period accepting the retired names as filter input, and a
  stable machine code beside the state name in JSON.** Both rejected: the change
  is atomic and there is no released consumer to distinguish versions for.
- **Keeping one field for both the artifact state and a download job's status.**
  Rejected. They are two vocabularies, and sharing `State` is why every consumer
  began by discriminating which of the two it was holding.

### What it opened

- `Principal()` accepts any entry with a non-nil classification and never
  consults usability, so a preset can still bind an artifact with no recognised
  type. Refusing to write an unclassifiable sidecar removes the artifacts that
  would exercise that hole; it does not close it.
- `update` reads today and keeps `--json`. `detect-model-updates` will make it
  persist its observation, and that change has to decide whether it crosses to
  the acting side of the surface.

## Problem

The listing reports two states, `installed` and `unreconciled`, and derives them
from whether a valid sidecar exists. `model-artifact-states` settles seven, and
retires both of the current words: `installed` conflated presence, local
adoption and remote provenance, and `unreconciled` is named after a command that
is being split into `import` and `reconcile`.

`installed` is not only rendered; it is load-bearing. It is a sort key
(`model.go:374`), a filter (`model.go:418`), the ordering rule (`model.go:450`)
and the gate in `usabilityOf` (`usability.go:103`) and `installedPrincipals`
(`usability.go:76`). Renaming it is not a display change.

## Required outcome

Future work should:

- derive and render the seven states settled in `model-artifact-states`:
  `untracked`, `altered`, `incomplete`, `local`, `detached`, `synced`,
  `upgradable`;
- replace every internal use of the string `installed` — sorting, filtering,
  usability gating — rather than renaming only the rendered value;
- keep `usable` derived wholly from the state, and keep the `DERIVATION` column
  as it is: `synced` was chosen precisely so the two columns stop colliding;
- ensure structured output and human output agree on the vocabulary.

## Acceptance questions

- Does the JSON output carry the state name, a stable machine code, or both?
- Is there a transitional period where the old names are still accepted as
  input to `--state`-style filters, or is the change atomic?
- Where does the *cause* of `detached` surface, given the state deliberately
  does not name it?

## Scope boundary

State derivation and presentation. The sidecar fields the derivation reads are
settled in `model-artifact-states` and specified by
`record-verify-outcome-in-sidecar` and `distinguish-unattested-artifacts`; do
not redefine them here. Command renames belong to
`align-model-command-surface`.

## Relevant implementation areas

- `internal/model/library.go`
- `internal/model/usability.go`
- `cmd/coquena/model.go`
