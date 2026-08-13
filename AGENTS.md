# AGENTS.md

Instructions for any coding agent working in this repository.

Project context, the development baseline, and the rules for proposals, specs,
design and tasks live in [`openspec/config.yaml`](openspec/config.yaml). Read it
for what Coquena is and how changes are shaped. This file covers something that
document does not: how the written artifacts relate to each other, and the
scripts that keep them honest.

## Start here

```bash
task docs:status
```

It prints, from the registries alone, which RFCs are open, which backlog entries
are ready versus blocked and by what, and which OpenSpec changes are named but
have never been created. It opens no artifact document and makes no network
request, so it is cheap enough to run before every task.

Run it before reading anything else. It usually replaces reading a dozen files.

## Where written work lives

Two systems. OpenSpec owns implementation; `docs/` owns the reasoning that
justifies it. Putting something in the wrong place is how the same problem gets
solved twice.

| Location | Answers | Lifecycle |
|---|---|---|
| `openspec/specs/` | What does the system do? Normative. | Durable |
| `openspec/changes/` | What are we changing right now? | Archived when applied |
| `docs/` | Why, and on what evidence? | See the kinds below |
| `docs/` (loose files) | Reference material that indexes nothing | Not registered |

Under `docs/`, every artifact has a **kind**, and each kind lives in its own
directory:

| Kind | Directory | Is | Closes? |
|---|---|---|---|
| `story` | `stories/` | A user need, in the operator's words | No — it changes, it does not finish |
| `finding` | `findings/` | A verified fact about the world | No — a fact stays true |
| `usecase` | `usecases/` | A story plus the findings that constrain it | No — it evolves |
| `decision` | `decisions/` | What was settled, and what it rules out | No — it stays in force until superseded |
| `backlog` | `backlog/` | A known gap, not yet scheduled | Yes, when a change lands |
| `rfc` | `rfcs/` | An open architectural question | Yes, by the project owner |

The flow is a graph, not a pipeline. A story motivates findings; a use case
assembles both; backlog entries and RFCs derive from them and produce changes;
and a change can produce new findings, bugs and derived RFCs that feed back in.

**A finding is a fact about the world** — Hugging Face, GGUF, llama.cpp, the
ecosystem — not about Coquena's current code. Code facts are found by reading
code. World facts cost network calls, documentation archaeology or experiments,
and that is what makes them worth writing down. The line that keeps findings
from swallowing the backlog: *a finding survives the change; a defect is closed
by one.*

## docs/registry.yaml is the source of truth for relations

One registry indexes every artifact and every link between them. Nothing else
stores those links, so nothing else can contradict them — provided they are
validated.

Rules:

- **Do not invent fields or relation types.** Extending the schema is a decision
  for the project owner, not something to improvise mid-task. The relation types
  are `motivated-by`, `produces`, `blocked-by`, `supersedes` and `relates-to`.
- **A relation is written once**, on the artifact it belongs to, and never
  mirrored. A one-way link cannot exist if there is only one place to write it.
- **Prefix an OpenSpec change with `change:`.** An artifact and the change it
  asks for routinely share a name; without the prefix the target silently
  resolves to the artifact itself.
- **A closed artifact's `produces` targets must exist.** An open artifact may
  still be describing a plan, which is reported as a note.
- **Every `.md` and `.puml` in a kind directory must be registered.** Loose files
  elsewhere under `docs/` are not indexed and are not orphans. A diagram is an
  artifact like any other: PlantUML is registered and validated because it is
  text that diffs and reviews, and because a document pointing at a renamed
  diagram should fail the build rather than rot quietly.

The YAML reader accepts a deliberately small subset: nested mappings, lists of
scalars, folded block scalars, one-line flow mappings for relations, and empty
inline collections. It fails with a file and line on anything else. Keep the
registry inside that subset so it stays editable by hand.

## Archiving

Follow OpenSpec where it applies: a kind that closes is archived by **moving**
the document into an `archive/` directory beside it, and location encodes
lifecycle. The validator requires that `state: closed` and a path under
`archive/` agree, so neither can drift from the other.

Two departures, both deliberate:

- **The registry entry stays.** OpenSpec has no registry because its changes are
  self-contained; ours cross-reference, and a link into an archived artifact
  must keep resolving.
- **Kinds that do not close are never moved.** A finding that turns out to be
  wrong becomes `refuted` and stays exactly where it was. OpenSpec archives
  finished work, which is fine to tuck away — but a refuted finding's entire
  value is being found before someone proposes it again.

## Before you finish

```bash
task docs:validate
```

Non-zero exit means a reference is dangling, a status disagrees with its own
fields, a file is unregistered, or a link points only one way. It runs as the
first step of `task verify`, so a broken reference fails the workspace
verification rather than waiting to be noticed.

Notes (as opposed to errors) are informational and do not fail the run. A
`candidateChanges` entry naming a change that does not exist yet is normal: it
records intent.

## Record what was ruled out, not only what was decided

The most expensive failure in this repository's history was not a wrong
decision. It was a wrong conclusion that got recorded, inherited, and then
re-derived by four separate passes over the same problem.

When closing an RFC or a backlog entry, write down the options that were
rejected and why. A reader who can see that an approach was already considered
and refuted will not propose it again. An outcome that lists only acceptances
invites the rework it was meant to prevent.

## Verify claims against primary sources

This repository has been bitten by plausible, confidently stated, wrong claims
about upstream behaviour. Two habits are expected:

- Check upstream assertions against the actual source or API rather than
  recalling them, and cite what you checked.
- Mark anything you could not verify as unverified rather than letting it settle
  by repetition.

## Scripts

Development scripts live in `scripts/` and run on Node with no dependencies.
Match the existing style: ESM, `node:`-prefixed imports, `import.meta.dirname`,
kebab-case filenames, no build step and no TypeScript.

| Script | Task | Purpose |
|---|---|---|
| `docs-status.mjs` | `task docs:status` | Print artifact state from the registries |
| `docs-validate.mjs` | `task docs:validate` | Validate registries and their references |
| `docs-registry.mjs` | — | Shared reader; not run directly |
