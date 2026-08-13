# The states of a model artifact

**Kind:** decision · **State:** current · **Decided:** 2026-08-11 · **Decided by:** project owner

This is the settled vocabulary for what Coquena knows about an artifact and what
it shows the operator. It supersedes the `STATE` table in the
`model-artifact-attestation` ruling, which was ambiguous in ways that cost four
review passes to surface. The diagram beside it,
[`model-artifact-state-diagram.puml`](model-artifact-state-diagram.puml), is the
same content as a derivation; read either, but do not let them drift.

## The words that caused the trouble

Two terms were used throughout the RFC as if they were understood, and every
reviewer used them differently. Neither survives.

**"Baseline" is retired.** It was `Artifact.SHA256` — the SHA-256 Coquena
computes *from the file on disk* and writes down. The word suggests a reference
point agreed with an authority; the value is nothing of the sort. It is
self-certified: if the bytes were already wrong when Coquena first hashed them,
the record enshrines that and `verify` reports `matched` forever. Say **digest
de integridad**, or "the digest Coquena computed and recorded", and the
confusion does not arise.

**"Attested" was doing two jobs.** An artifact has up to two digests, and they
are distinguished by *role*, not by algorithm:

| Role | Where it comes from | The question it answers |
|---|---|---|
| **integrity** | Coquena hashes the local file | Did the bytes change since they were recorded? |
| **repository match** | the repository publishes it | Are these the bytes the repository publishes? |

The two coincide in value for an LFS-backed file, where the local SHA-256 equals
`lfs.sha256`. They differ for a file stored directly in Git, where the
repository's identifier is a Git blob OID. That coincidence in the common case
is precisely why one field appeared to be enough, and why the schema was wrong
in a way nobody noticed.

An artifact may therefore carry one digest or several, each declaring its role
and its algorithm.

## The seven states

`STATE` is **derived at display time**, never stored. What the sidecar persists
is evidence; the state is a reading of it.

| State | Means | Usable | How it is reached |
|---|---|:---:|---|
| `untracked` | Present on disk, visible, outside Coquena's management. Can be adopted. | no | no sidecar |
| `altered` | The bytes no longer match the digest recorded for them. | no | `verify` recorded a mismatch |
| `incomplete` | The principal model is missing shards. | no | a required shard is absent |
| `local` | Adopted deliberately; no repository was ever consulted. | yes | `import` |
| `detached` | A repository was consulted and no match was established. | yes | `reconcile` did not match |
| `synced` | Matches the repository's current revision. | yes | `reconcile` matched |
| `upgradable` | Matches an earlier revision; the repository moved on or relocated. | yes | `reconcile`, or an update check |

**`usable` means one thing: eligible to be referenced by a preset.** It is
informative, derived wholly from the state, and never an independent judgement
about quality. The three unusable states are unusable for concrete reasons — a
preset cannot bind an artifact Coquena does not manage, whose bytes changed
underneath it, or that is missing pieces it needs to load.

A companion whose principal is not installed **is usable**. A projector cannot
run alone, but neither can any companion, and it may serve several principals.
Which principal it accompanies is a decision the preset makes, not the library.

### Why these names

Four of the previous names claimed more than the evidence supported, or were
named after a mechanism rather than the operator's situation.

| Was | Now | Why it changed |
|---|---|---|
| `installed` | *(gone)* | Conflated three things: presence, local adoption and remote provenance. |
| `unreconciled` | `untracked` | Named after the `reconcile` command, which is being split. `untracked` is the ordinary Git word for "present, not managed" and survives any command rename. |
| `modified` | `altered` | `modified` implies someone edited the file. `verify` cannot tell damage from an edit, and should not claim to. |
| `current` | `synced` | Collided with the listing's `DERIVATION` column, which already uses `current` for "the derived block is up to date". Two adjacent columns reading `current` meant two unrelated things. |
| `diverged` | *(gone)* | It asserted that local bytes differ from the remote. In the case it actually covered, nothing had been compared. Absorbed by `detached`. |

`incomplete`, `local` and `upgradable` were already accurate and are unchanged.

## `detached` names a situation, not a cause

An artifact reaches `detached` when a repository was consulted and no match was
established. Three causes produce it and **they are not distinguishable from the
artifact**:

- the repository could not be consulted at all;
- the bytes belong to a revision that no longer exists — publishers may
  super-squash history or delete LFS objects, and the Hub recommends it for
  reclaiming storage, per
  [`hugging-face-publishers-can-destroy-history`](../findings/hugging-face-publishers-can-destroy-history.md);
- the bytes are not from that repository.

The state therefore names what is known — no established link — and leaves the
cause to a diagnostic. Searching a repository's history for a matching revision
is a best-effort aid, never a decision procedure: a negative answer proves
nothing, because the matching revision may have been destroyed.

Reaching `detached` is not a failure to report and discard. A sidecar is
written, the artifact is usable, and the operator keeps a working model rather
than an error message.

## What is persisted, and why `altered` forced it

The sidecar stores evidence on independent axes:

- the digests, each with its role, algorithm and when it was computed;
- **the outcome of the last `verify`, and when it ran**;
- the last remote observation, and when it was made;
- the repository match, once established, and when.

The second is not a stylistic choice. `hashFile` is called in exactly three
places — download, `verify` and `reconcile` — and **the listing never hashes**.
Deriving `altered` at display time would mean re-hashing every model on every
`model list`, gigabytes per row. `verify` therefore records what it found, the
way `chkdsk` records that it found errors, and the listing reads the record.

This corrects the ruling, which stated that `STATE` is never persisted while
including `modified` in its table. Both hold once the distinction is kept: the
*observation* is persisted, the *state name* is derived from it.

## The commands that move an artifact

Each verb has one responsibility.

| Command | Reach | Effect |
|---|---|---|
| `import` | local only | Adopts bytes already on disk → `local` |
| `reconcile` | online only | Establishes a repository match → `synced`, `upgradable` or `detached` |
| `verify` | local only | Records whether the bytes still match their digest |
| `update` | online only | Observes; never alters bytes |
| `upgrade` | online | Replaces bytes, staging and checking the target first |

`import` and `reconcile` replace the current single `reconcile` with its
`--accept` and `--online` modes. The vocabulary above deliberately does not
depend on those names: `untracked` says what the artifact is, not which command
has not been run on it.

## Open

One question this vocabulary does not settle: when a repository has been
renamed and answers with a 307, `upgradable` reports that the artifact can be
relocated — but which name the sidecar records, the requested or the canonical,
was raised as F3 in the RFC and answered by nobody. Tracked in
[`record-canonical-repository-name`](../backlog/record-canonical-repository-name.md).
