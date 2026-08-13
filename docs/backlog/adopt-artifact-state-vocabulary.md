# Adopt the settled artifact state vocabulary

## Status

- **State:** open
- **Opened:** 2026-08-11
- **Owner:** project-owner
- **Priority:** high
- **Story step:** the whole story, as what the operator reads
- **Candidate change:** `establish-sidecar-authority`

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
