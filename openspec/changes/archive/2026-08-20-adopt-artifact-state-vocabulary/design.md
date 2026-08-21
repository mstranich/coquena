## Context

See `proposal.md` — Why. The evidence this change reads already exists: the
sidecar carries `Verification`, `Observation`, `Attestation` and `Ownership` as
independent axes, and `Record.Standing()` already collapses the last two into
`verified`/`partial`/`unattested`. Nothing new needs to be persisted.

Three facts about the current code shape the approach:

- `LibraryEntry.State` is a plain `string` filled from two unrelated
  vocabularies — `string(job.Status)` for an in-flight download, and
  `"installed"`/`"unreconciled"` for a file on disk. Every consumer therefore
  begins by discriminating which vocabulary it is holding.
- `Usability` is consumed only by the listing renderer. Nothing in
  `internal/presets/` reads it, so it annotates a column and gates nothing.
- Classification failure is swallowed in four places, which is how a transient
  network error ends up recorded as a durable property of a local file.

The model CLI is a hand-written dispatcher over `os.Args` in
`cmd/coquena/model.go` with a hardcoded usage string. There is no command
framework to fight, so splitting a verb is an ordinary edit.

## Goals / Non-Goals

**Goals:**

- One function owns the state derivation, and its order is the diagram's order.
- The retired words are absent from the codebase, not merely from the output.
- An artifact Coquena could not classify never reaches a sidecar.
- Each of `import` and `reconcile` has one responsibility and no mode parameter.

**Non-Goals:**

- Introducing a state machine type, a transition table, or a registry of state
  handlers. The derivation is a cascade of predicates over one record; anything
  larger is scaffolding for transitions that do not exist, because states are
  read, never stored or moved between.
- Preserving any removed spelling as an alias, shim, or transitional error.
- Changing what evidence is persisted.

## Decisions

### The cascade is one ordered function, not a set of predicates

`stateOf(record, present bool) ArtifactState` evaluates the diagram's gates in
the diagram's sequence and returns at the first that matches. The order is
load-bearing, not stylistic: an altered artifact may also carry an attestation,
and an untracked one has no verification outcome to consult at all. A
rule-collection keyed by condition would let a later edit reorder the checks
without any test noticing.

*Alternative considered:* deriving each state independently and asserting exactly
one holds. Rejected — it turns a total function into a runtime invariant, and the
invariant is false by construction, since `altered` and `synced` genuinely
co-occur in the evidence.

### `incomplete` keeps its gate even though nothing reaches it

The predicate is `false` today because Coquena has no notion of a shard. The gate
stays in the cascade, in position, with a comment naming
`address-quantization-bundles` as what supplies the predicate.

*Alternative considered:* omitting the branch until it can fire. Rejected — the
gate's position between `altered` and the provenance states is the part that is
easy to get wrong later, and reinstating it means re-deriving an ordering that is
settled now.

### `ArtifactState` is a defined type, and the job status leaves `State`

`LibraryEntry` gains a `JobStatus` field for the transfer vocabulary, and `State`
becomes `ArtifactState`. An entry that represents an in-flight job carries no
artifact state at all, because it has no artifact on disk yet: the cascade starts
at "is there a sidecar", and neither answer is meaningful for a file that does
not exist.

This is what lets the listing drop `entry.State != "installed" && entry.State !=
"unreconciled"`, a filter that only ever existed to separate the two vocabularies
back out.

*Alternative considered:* a single enum spanning both. Rejected — it re-creates
the conflation under a new name, and the seven states are defined by the decision
as readings of a sidecar, which a pending transfer does not have.

### Usability collapses to a set membership test

`usable = state ∈ {local, detached, synced, upgradable}`. The `UnusableReason`
enumeration and the `Reason`/`Detail` fields are deleted. `Advice` stays: it
never bore on eligibility, and the companion-without-principal note is still
worth telling an operator.

The renderer loses its reason column. An operator reading `untracked` or
`altered` already has the reason.

### Classification failure propagates instead of degrading

`deriveFile` returns `(Derivation, error)`. Its callers divide cleanly:

| Caller | Writes a sidecar | New behaviour |
|---|---|---|
| `Publish` (download) | yes | fail, publish nothing |
| `PublishInPlace` (import, reconcile) | yes | fail, write nothing |
| `verify` derivation repair | yes | fail, leave the sidecar unchanged |
| `classifyUnreconciled` (enumeration) | no | unchanged, best-effort |

`classifyUnreconciled` is deliberately untouched. It runs over files that have no
sidecar during the directory walk, records nothing, and reporting an undetermined
type for an `untracked` entry is accurate. Making it fail would make one foreign
file hide the rest of the library, which is the behaviour it exists to prevent.

The remote path in `reconcile_repository.go` is the one that changes character: a
failed `InspectRemote` currently yields `Classification{Type: TypeUnknown}`,
which then fails to match as a principal, which silently shrinks the target set.
It now fails for that artifact.

*Alternative considered:* treating an unclassifiable artifact as a new unusable
state. Rejected by the project owner — if Coquena cannot classify, it should
error and exit without writing a sidecar. The artifact then has no sidecar and is
`untracked`, which the vocabulary already covers, and the operator's recourse is
to import it deliberately or retry with a working network.

### `upgradable` compares two axes that already exist

`Attestation.ResolvedCommit` names the revision the bytes are bound to;
`Observation.ResolvedCommit` names the revision last seen at the repository. They
differ exactly when the repository moved on. No new field, no network call at
listing time.

What refreshes the observation is `update`, whose persistence is
`detect-model-updates`. Until then an observation is refreshed by `reconcile`,
which is sufficient for the state to be derivable and reachable.

### Two commands, two entry points, no shared mode

`import` and `reconcile` get separate handlers rather than one handler taking a
mode. Sharing the body is what allowed a flag to choose between two acts; keeping
them apart makes the fallback impossible to write by accident. The genuinely
common parts — reference parsing, target resolution, sidecar writing — are
already separate functions and stay shared.

### `--json` follows the endpoint each command would be

The REST API was deferred for speed, and the CLI is standing in for it. Each
command is the endpoint it would have been, and `--json` is where a
representation of a resource is returned: a reading command gets it, an acting
command does not.

| Reads | Acts |
|---|---|
| `discover`, `list`, `update`, the download-job queries | `download`, `import`, `reconcile`, `verify` |

This replaces the reason `reconcile` was refusing the flag before — that it
prompts, and so cannot emit exactly one document. True, but incidental: it
explains one command rather than the surface, and it would have admitted `--json`
on `import` and `download`, which prompt nothing.

`verify` is on the acting side. It exists to repair the coherence between an
artifact's bytes and the evidence recorded about them, and the library capability
already makes it the only writer of the verification axis. What it establishes is
read back through `list`.

`download` loses nothing by giving up the flag. It already prints every created
job identity on standard output, one per line, and the jobs are durable and
addressable through `download list` afterwards.

`update` reads today and keeps `--json`. `detect-model-updates` will make it
persist its observation, and whoever writes that change has to decide whether it
crosses to the acting side.

### JSON output changes shape within schema version 1

`model list --json` drops the usability reason, changes its state values, and
gains a job-status field. The document's schema version stays 1: there is no
released consumer to distinguish versions for, and bumping it would advertise a
migration that nobody has to perform.

## Risks / Trade-offs

**`incomplete` never appears, and a reader concludes it is dead code and deletes
it** → The spec requires the gate, and the code comment names the change that
supplies its predicate. A deletion has to argue with both.

**Reconciliation gets stricter: one candidate whose remote inspection fails now
fails that artifact, where before the run appeared to succeed** → This is the
point. The Gemma 4 witness is precisely a run that reported success while
omitting a companion. A loud failure the operator can retry beats a quiet one
they discover when a preset will not bind. Reconciliation still continues past a
failure for the repository's other artifacts.

**A preset can still bind an artifact with no recognised type** → `Principal()`
checks only for a nil classification and never consults usability. Refusing to
write an unclassifiable sidecar removes the artifacts that would exercise the
hole, so this change narrows it without closing it. It is recorded as a non-goal
in the proposal and needs its own backlog entry.

**Test churn is large** — 39 occurrences of the retired strings across seven
non-test files and roughly as many test files → Unavoidable, and the point of
doing it in one change rather than leaving two vocabularies alive across several.

**The interfaces capability described a `verify` that makes no local change,
which the library capability already contradicted** → `establish-sidecar-authority`
made `verify` the only writer of the verification axis and did not update the
command's own requirement. This change replaces it rather than landing a delta
beside a requirement it contradicts. The correction is confined to what the
sidecar authority model already settled: verification records its outcome, and
there is no baseline-absence case because a valid sidecar always carries an
`integrity` digest.

## Migration Plan

None. `STATE` is derived and never persisted, so no stored data changes shape.
Sidecars written before this change remain valid; they are read through the same
schema-4 reader `establish-sidecar-authority` established.

No rollback path is prepared. Coquena has no release, and the change is a
refactor of derived output plus a command rename.
