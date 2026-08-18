# Record what `verify` found, so the listing does not have to re-hash

## Status

- **State:** closed
- **Opened:** 2026-08-11
- **Closed:** 2026-08-15
- **Owner:** project-owner
- **Priority:** high
- **Story step:** *"keep it"*
- **Candidate change:** `establish-sidecar-authority`

## Problem

`verify` computes whether an artifact's bytes still match the digest recorded
for them, prints the answer, and discards it. Nothing is written down.

That makes the `altered` state underivable in practice. `hashFile` is called in
exactly three places — download (`manager.go:479`), `verify` (`verify.go:63`)
and `reconcile` (`reconcile.go:67`, `:104`) — and **the listing never hashes**.
For `model list` to report that an artifact was altered, it would have to
re-hash every model in the library on every invocation.

## Why it matters

The state vocabulary settled in `model-artifact-states` includes `altered`, and
the attestation ruling states that `STATE` is derived and never persisted. Both
cannot hold unless the *observation* is persisted and only the state name is
derived. `verify` is the writer of that observation, the way `chkdsk` records
that it found errors rather than requiring every later reader to rescan the
disk.

Without it, either the listing lies by omission about damaged artifacts, or
listing a library becomes an operation measured in gigabytes read.

## Required outcome

Future work should:

- persist the outcome of the last `verify` and when it ran, on its own axis;
- derive `altered` from that record rather than from a fresh hash;
- make the staleness of the record visible, so an operator can tell a clean
  result from an old one;
- keep `verify` the only writer of that field, per the authority partition.

## Acceptance questions

- Does an artifact never verified read as unknown, or as clean until shown
  otherwise?
- Does any other operation reset the record — for example a successful
  `reconcile`, which hashes the file anyway and therefore knows the answer?
- Should the listing warn when the record predates the file's modification
  time?

## Scope boundary

The verify axis only. Do not settle the other sidecar fields here.

## Relevant implementation areas

- `internal/model/verify.go`
- `internal/model/library.go`
- `cmd/coquena/model.go`

## Outcome

Closed by `establish-sidecar-authority`. `verify` persists its outcome, the time
it ran, and **the digest it compared against**; the listing derives `altered`
from that record and never hashes.

Answers to this entry's acceptance questions:

- **An artifact never verified reads as unverified, not as clean** — and stays
  usable. Reading it as clean would assert a state on no evidence; reading it as
  unusable would make every existing library unusable the moment this landed.
  Only a recorded mismatch produces `altered`.
- **No other operation resets the record.** Reconciliation hashes the file and
  therefore knows the answer, and still does not write it. The record names the
  digest it checked, so replacing an artifact's evidence retires the reading *by
  identity* — which is what makes the single-writer rule affordable, since
  nobody has to remember to clear anything.
- **The listing does warn when the record predates the file**, comparing
  modification time against the recorded check. mtime only ever weakens the
  claim: a stale record still reports the outcome it recorded, and is never
  upgraded to matched or downgraded to altered on that evidence alone.

Rejected: recording the file's size and modification time at verification
instead of the digest. It answers a weaker question, and a file rewritten in
place at the same length with a restored mtime would defeat it.
