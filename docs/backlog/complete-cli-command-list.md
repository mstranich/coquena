# Show the complete Coquena command list

## Status

- **State:** open
- **Opened:** 2026-08-10
- **Owner:** project-owner
- **Priority:** low
- **Story step:** cross-cutting operator discoverability
- **Candidate change:** `complete-cli-command-list`

## Problem

When Coquena rejects a top-level invocation, the usage text presents itself as
the list of available commands, but that list is incomplete. The dispatcher
accepts command families the top-level `commandUsage` string does not name:
`preset` and `config` are both absent, and nothing prevents the next family from
being absent too.

An operator cannot treat the program's own help as an authoritative answer to
"what can Coquena do?" and must instead consult source code or separate docs.

**Updated 2026-08-20.** Two things changed.

The scheduling condition this entry set for itself is satisfied.
`align-model-command-surface` closed with `adopt-artifact-state-vocabulary`, so
the vocabulary a complete listing would document is now the intended one rather
than a transitional one. The reason this entry was filed at low priority — that
fixing it before the surface changed would create churn — no longer holds.

The defect also got worse, in a way that is worth stating exactly because it is
the concrete case for the general complaint.

`cmd/coquena/runtime.go:23` reads:

```text
	   coquena model resolve|download|revision <reference> [--json]
```

`resolve` and `revision` were withdrawn by `adopt-artifact-state-vocabulary`, so
the program's own help now names two commands that do not exist and claims
`--json` applies to a family where it is refused by three of the verbs. The line
also indents with a tab where every other line uses spaces, so it renders
misaligned.

This was deliberately **not** fixed inside that change: the change's scope was
the state vocabulary and the model command surface, and correcting the top-level
listing belongs here. It is recorded rather than repaired so that the first
concrete thing this entry does is close a defect that already exists, instead of
starting from a general complaint.

The general complaint is unchanged and is the reason a one-line patch is not the
answer: the top level enumerates some families by hand and omits others, and
nothing fails when the next one is forgotten.

## Required outcome

Future work should:

- make the top-level command listing complete for every command family the
  dispatcher accepts;
- make each family expose its supported subcommands and material flags without
  requiring an invalid invocation to discover them;
- keep the presented command surface consistent with `docs/model-commands.md`
  and executable parsing tests;
- prevent a newly added command from silently remaining absent from the list;
- ~~schedule this after command-surface changes such as
  `align-model-command-surface`~~ — satisfied on 2026-08-20.

## Acceptance questions

- Should Coquena adopt conventional `help` and `--help` entry points, or is a
  complete usage document on invalid input sufficient for the first pass?
- Should command metadata be centralized and rendered, or should tests compare
  hand-written usage text against the dispatcher?
- How much detail belongs at the top level versus family-specific help?

## Scope boundary

Command discoverability only. Do not rename commands, alter their semantics,
or add shell completion here. Those changes remain owned by their existing
backlog entries.

## Relevant implementation areas

- `cmd/coquena/runtime.go`
- `cmd/coquena/main.go`
- `cmd/coquena/model.go`
- `cmd/coquena/preset.go`
- `cmd/coquena/field.go`
- `cmd/coquena/config.go`
- `docs/model-commands.md`

