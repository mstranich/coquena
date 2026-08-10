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
accepts command families and forms that the top-level `commandUsage` string
does not name accurately: for example `preset` and `config` are absent, while
the model summary collapses or omits supported operations such as `discover`,
`list`, `reconcile`, `verify`, `check-updates`, and their relevant flags.

An operator cannot treat the program's own help as an authoritative answer to
"what can Coquena do?" and must instead consult source code or separate docs.
Because the command surface is still expected to change, fixing this before
those changes land would create churn without unblocking model acquisition.

## Required outcome

Future work should:

- make the top-level command listing complete for every command family the
  dispatcher accepts;
- make each family expose its supported subcommands and material flags without
  requiring an invalid invocation to discover them;
- keep the presented command surface consistent with `docs/model-commands.md`
  and executable parsing tests;
- prevent a newly added command from silently remaining absent from the list;
- schedule this after command-surface changes such as
  `align-model-command-surface`, so the result documents the intended final
  vocabulary rather than today's transitional one.

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

