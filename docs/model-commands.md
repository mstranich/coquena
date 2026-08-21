# `coquena model` command contracts

This document defines the operator-facing boundaries of the `coquena model`
namespace: what each command may read, what it may write, and what it refuses.

It does not define the artifact states the listing reports. Those are settled in
[`model-artifact-states`](decisions/model-artifact-states.md) and derived from
the diagram beside it; restating them here would be a second place to keep the
same answer correct.

## Reading and acting

The REST API was deferred, and the CLI stands in for it. Each command is the
endpoint it would be, and `--json` follows that correspondence: a command that
**reads** state returns a representation of it and accepts `--json`; a command
that **acts** on state reports an outcome and does not.

Passing `--json` to a command that acts is a usage error, never a silently
ignored option, and the command does not act before refusing.

| Command | Reads or acts | Network | Reads model bytes | Writes sidecar | Writes model bytes |
|---|---|---:|---:|---:|---:|
| `discover <repository-or-url[:selector]>` | reads | yes | no | no | no |
| `list` | reads | no | no | no | no |
| `update <reference>` | reads | yes | no | no | no |
| `download list\|status <job-id>` | reads | no | no | no | no |
| `download <repository-or-url[:selector]>` | acts | yes | yes | yes | yes |
| `download pause\|resume\|cancel <job-id>` | acts | as required | as required | job state only | through an active download |
| `import <reference>` | acts | no | header only | yes | no |
| `reconcile <reference>` | acts | yes | as required | yes | no |
| `verify <reference> [--repair-derived]` | acts | no | full artifact | yes | no |

`upgrade <reference>` does not exist. It is tracked in
[`support-model-upgrade`](backlog/support-model-upgrade.md).

## References and cardinality

The CLI accepts one optional selector slot with two alternatives:

```text
user/repository[@revision]:QUANT
user/repository[@revision]:filename.gguf
```

`:QUANT` is a logical quantization class with cardinality `0..N`.
`:filename.gguf` is the canonical exact-artifact selector with cardinality
`0..1`. They are alternatives in the same colon slot; Coquena does not accept a
slash filename form, nested selectors, or an invented calibration-variant
suffix.

Commands that require one artifact refuse an ambiguous logical selector and name
every matching canonical filename reference. An explicit `@revision` is enforced
against the recorded sidecar provenance; omission leaves the recorded revision
unconstrained and does not imply `main`.

For example, when two managed files share Q4_K_M, a singleton operation on
`org/repository:Q4_K_M` refuses and offers references such as:

```text
org/repository@abc123:model-Q4_K_M.gguf
org/repository@def456:model-Q4_K_M-imatrix.gguf
```

Preset bindings retain the authored selector and separately record the canonical
artifact and resolved revision chosen at bind time. Installing a sibling in the
same quantization class makes the authored selector ambiguous; it does not make
the already-bound canonical artifact stale or rebind it.

## Classification is a precondition, not a fallback

No command writes a sidecar for an artifact it could not classify. When a file
cannot be opened, cannot be parsed as GGUF, declares a type Coquena does not
recognise, or could not be classified because a required remote inspection
failed, the operation fails and leaves no sidecar behind.

Such an artifact therefore has none, and the listing reports it as `untracked`:
present on disk, outside Coquena's management, and adoptable once the operator
resolves whatever made it unclassifiable.

Enumeration is the one place that still tolerates an undetermined type, because
it records nothing. A foreign file in the models directory is listed rather than
hidden, and does not fail the walk.

## Remote exploration

### `discover`

```console
coquena model discover org/repository
coquena model discover org/repository:Q4_K_M
coquena model discover org/repository --model-card
```

Groups remote GGUF candidates into quantizations, lists every constituent file,
and reports the total published size of each quantization. Split GGUF shards are
one row group rather than unrelated candidates. Shared companions are reported
separately and are not duplicated in quantization totals. A dash means the
complete total is unknown because at least one constituent size is unavailable.

The default comparison does not request the repository model card. Pass
`--model-card` to fetch and print it after the quantization summary.

Discovery queries Hugging Face and mutates nothing.

## Downloading and job management

### `download`

```console
coquena model download org/repository:Q4_K_M
```

Downloads the selected quantization as a complete bundle: the principal and
every companion identified for it, one durable job per artifact. There is no
opt-in flag, because the operator asks for a quantization and a quantization is
not usable in pieces.

Every created job identity is printed on its own line. The jobs are durable and
remain addressable through the query subcommands afterwards, which is why
`download` needs no machine-readable option of its own.

Publication is refused when the completed bytes cannot be classified; the
partial file is left in place and no sidecar claims it.

The nested job commands operate on model download jobs:

```console
coquena model download list [--json]
coquena model download status <job-id> [--json]
coquena model download pause <job-id>
coquena model download resume <job-id>
coquena model download cancel <job-id>
```

## Adopting and reconciling

`import` and `reconcile` are two verbs with one responsibility each. Adopting
local bytes and establishing a repository match are different acts, so the
operator names which one they mean by choosing the command. Neither has a mode
flag, and neither falls back to the other.

### `import`

```console
coquena model import org/repository:Q4_K_M
```

Adopts an artifact already on disk from its path-derived identity alone. It
issues no network request and leaves the artifact `local`.

It stays one artifact per invocation: it asserts an identity with nothing to
corroborate it against, so it is never applied to a set the operator did not name
one by one. There is no `--all`.

### `reconcile`

```console
coquena model reconcile org/repository:Q4_K_M
```

Reconciles the artifacts of one repository against that repository. A principal
and its companions are parts of one logical unit, so a reference naming a
repository reconciles all of them; a reference matching more than one principal
is refused as ambiguous, since choosing among quantizations is the operator's
decision.

An artifact whose locally computed digest agrees with the content hash the
repository publishes gains a full attestation. One whose digest disagrees is
refused. One the repository could not be consulted about, or for which no match
could be established, is left `detached` — usable, visible, and never silently
adopted as `local`.

A candidate whose remote inspection fails is reported and refused rather than
reclassified as unknown, and it stays in the reconciliation's target set so its
failure is visible instead of appearing as a silent omission.

Reconciliation prompts, so it emits no machine-readable document. It has no
`--all` mode.

## Verification and repair

### `verify`

```console
coquena model verify org/repository:Q4_K_M
```

Re-hashes the complete local artifact, compares it with the `integrity` digest
recorded for it, and **records the outcome and the time it ran**. It makes no
network request.

Verification writes by design: it repairs the coherence between an artifact's
bytes and the evidence recorded about them, and the record it leaves is what
lets a later `list` report `altered` without hashing anything. `verify` is the
only writer of that axis.

A valid sidecar always carries an `integrity` digest, so there is no
baseline-absence outcome: the comparison either matches or it does not.

### `verify --repair-derived`

```console
coquena model verify org/repository:Q4_K_M --repair-derived
```

Recalculates only evidence derived from the local bytes — GGUF classification,
parameter count, declared recipe, physical tensor composition, and the
derivation version.

The operation:

- proceeds only after a digest match, and refuses after a mismatch;
- preserves source, attestation, observation, ownership and the recorded
  digests;
- never queries Hugging Face and never modifies model bytes;
- fails without changing the derived evidence when the artifact can no longer be
  classified.

A failed repair does not retract the verification that ran before it. The hash
comparison happened, and discarding its outcome would lose an observation
Coquena actually made.

There is no `verify --all`.

## Checking for updates

### `update`

```console
coquena model update org/repository:Q4_K_M
coquena model update org/repository:Q4_K_M --json
```

Queries Hugging Face using only the artifact's recorded repository, requested
revision and filename, without reading or hashing the local file. It reports
whether the repository's current content for that path differs from what is
recorded locally, degrading to a revision-only comparison when the repository
publishes no content hash. It never runs as a side effect of a listing.

The verb pairs with `upgrade`, matching the tools the operator already uses
daily. What `update` should persist, and how a rename is detected, are
[`detect-model-updates`](backlog/detect-model-updates.md).

## Listing

### `list`

```console
coquena model list
coquena model list --json
```

Lists every artifact in the configured models directory with its state, declared
type, and whether it is usable. It performs no network request, no hashing, no
GGUF rescan, and no repair, and reports an empty list rather than an error when
no model is present.

Usability is derived wholly from the state, so no separate reason is reported:
the state is the reason. A companion with no compatible principal alongside it is
usable, and the absent principal is reported as advice rather than as a refusal.

A download still in flight reports its job status in its own field and carries no
artifact state. They are two vocabularies, and a transfer that has not finished
has produced no artifact to derive one from.

The `DERIVATION` column reports whether the evidence calculated from each GGUF
was produced by Coquena's current derivation algorithm:

- `current` — the sidecar's derivation version matches this build;
- `stale` — the sidecar carries an older derivation version.

`stale` does **not** mean the model is corrupt or that a remote update exists. It
means those calculated fields may not reflect the current derivation rules.
`list` determines it by comparing small version values in the sidecar; it does
not open, scan, or hash the artifact. To refresh one explicitly, run
`verify <reference> --repair-derived`.

## Removed commands

Coquena has no release, so a removed spelling is deleted rather than redirected.
Nothing forwards these, and the usage text names no replacement — it is the only
record of what the surface is.

| Removed | Why |
|---|---|
| `inspect` | An internal projection exposed as a subcommand. `verify` is the operator-facing comparison; download still uses the projection internally rather than parsing a header twice. |
| `resolve` | An internal projection of `discover`. |
| `revision` | An internal projection of `verify`. `list` already reports each artifact's resolved revision. |
| `check-updates` | An invented verb. Renamed `update`, which pairs with `upgrade`. |
| `reconcile --accept` / `--online` | A flag chose between two different acts. Split into `import` and `reconcile`. |
| `download --with-companions` | Made the bundle optional, contradicting the unit the operator asks for. |

## GGUF validation fixtures

The reproducible test suite uses small synthetic GGUF documents that exercise
metadata traversal, 64-bit tensor dimensions, structural-scope filtering, and the
overlapping numeric value `15` in both enum domains. A fixture that is to be
published must declare `general.type`, because publication now refuses an
artifact it cannot classify.

The operator's Ornith Q4_K_M artifact is not stored in this repository because it
is a multi-gigabyte model. When that artifact is locally available, `model
verify` provides the optional end-to-end acceptance check; its absence does not
make the synthetic parser and contract tests non-reproducible.
