# Align the model command surface with the story

## Status

- **State:** open
- **Opened:** 2026-08-05
- **Owner:** project-owner
- **Priority:** normal
- **Story step:** the whole story, as a check on what exists
- **Candidate change:** `align-model-command-surface`

## Problem

The commands were built one change at a time, each reasonable on its own, with
no story to check them against. Mapping the current surface onto
`model-acquisition` shows four kinds of drift.

**Verbs nobody chose.** `check-updates` is an invented verb. The project owner
uses `apt` and `winget` daily, where the pair is `update` and `upgrade`. A
bespoke verb charges every user the cost of learning it and buys nothing.

**Half a pair.** Restated by the owner on 2026-08-10: today only the observing
half exists, and that is why the surface reads oddly — `check-updates` learns
about changes and nothing acts on them. `upgrade` is not merely a nicer name
for a future operation; it is the missing verb that gives the observing one a
purpose. Its scope grew accordingly: besides replacing bytes, it now owns
applying an observed repository rename. See `support-model-upgrade`.

**A flag that contradicts the unit.** `--with-companions` makes the bundle
optional. The story says the download "must be a complete bundle", so there is
nothing to opt into.

**Internal helpers that leaked.** `resolve` and `revision` serve no step of the
story. Both look like internals — of `discover` and `verify` respectively — that
ended up exposed as subcommands.

**A name that describes the mechanism, not the intent.** `reconcile` is what the
operation does internally; `import` is what the operator is doing. The project
owner has proposed `import`, keeping the requirement that intent be explicit
rather than inferred: adopting local bytes and synchronising with a remote are
different acts and the command must not choose between them silently.

## Required outcome

Future work should:

- rename `check-updates` to `update`, pairing with `upgrade`;
- remove `--with-companions`;
- remove or internalise `resolve` and `revision`;
- split `reconcile` into two verbs of one responsibility each, per the owner
  decision of 2026-08-11: **`import` is local only** and **`reconcile` is online
  only**, replacing today's `--accept` and `--online` modes. This supersedes the
  earlier plan to rename `reconcile` to `import` wholesale. Explicit intent is
  preserved by construction rather than by a flag: the operator names the act,
  and neither command can silently choose the other;
- keep `<ref>` mandatory everywhere. No `--all` flag is introduced. If
  library-wide action is ever warranted, `<ref>` becomes optional across every
  operation, which retires the question permanently.

## Acceptance questions

- ~~What are the flag names under `import`?~~ Answered by the split: there are
  no mode flags, because the mode is the command.
- ~~What is `import` with no flag?~~ Answered likewise. Neither command falls
  back to the other; a `reconcile` that establishes no match leaves the artifact
  `detached` rather than silently importing it as local.
- Do the removed subcommands warrant a transitional error naming the
  replacement, given there is no release yet and therefore no users to migrate?
- Does `verify` keep its name? It serves no story step explicitly, but it is the
  integrity guarantee the other operations rest on.

## Scope boundary

Naming and surface. Behaviour changes belong to the entries that own them:
bundles to `address-quantization-bundles`, update semantics to
`detect-model-updates`, discovery output to
`report-download-size-in-discover`.

## Relevant implementation areas

- `cmd/coquena/model.go`
- `docs/model-commands.md`
