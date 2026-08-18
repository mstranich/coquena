# Design — establish-sidecar-authority

## Context

See [`proposal.md`](proposal.md) for motivation. Two documents govern this
design, and they are not interchangeable:

- the closed RFC [`model-artifact-attestation`](../../../docs/rfcs/archive/model-artifact-attestation.md),
  whose ruling (Q1–Q6) and post-closure amendments (A1–A7) are the field-level
  answer this change implements;
- the decision [`model-artifact-states`](../../../docs/decisions/model-artifact-states.md),
  which governs the state vocabulary and the digest roles where the two disagree.

Three facts about the current code shape every decision below.

**`Artifact` is one struct doing two jobs.** `internal/model/types.go` defines a
single `Artifact` used both as the persisted sidecar block (`Record.Artifact`)
and as in-memory transport metadata on a resolved `Candidate`. `ETag` reaches the
record only because the same struct carries it from the HEAD response
(`huggingface.go:143`) into `Job.ETag` (`manager.go:108`), where it is genuinely
needed for `If-Range` resume validation (`manager.go:375`, `:396`).

**The published hash is already fetched but never bound.**
`request-remote-content-hash` landed (archived 2026-08-07), so
`Candidate.ContentSHA256` now carries `lfs.sha256`. RFC F1 is closed. What is
still missing is not the value but the record of what was done with it.

**The listing never hashes, and must not start.** `hashFile` runs in three places
only, all commands the operator invoked deliberately. `AssessUsability` performs
no I/O beyond what enumeration already did.

## Goals / Non-Goals

Goals beyond the proposal's scope statement:

- Make the authority partition structural rather than a convention a reviewer has
  to enforce: it should be hard to write a field you do not own.
- Make every persisted identifier self-describing, so a reader never infers
  meaning from the field a value was stored under — the exact failure `ETag`
  embodies.
- Keep every derivation the listing performs free of artifact I/O.

Non-goals at the design level:

- No general migration framework. The reader accepts exactly one preceding
  schema, as it does today.
- No abstraction over "evidence sources". The axes are enumerated, not
  registered.

## Decisions

### Five axes, because collapsing any two is how the current bug happened

`Record` grows `Local`, `Verification`, `Observation`, `Attestation` and
`Ownership`. The ruling (Q2) and amendments A1–A3 name these; the design's only
contribution is the Go shape.

The temptation is to collapse *observation* and *attestation* into one "did the
repository match?" flag. That is exactly what the ruling rejected under
"replacing an earlier attestation with a later unverified observation was
rejected because it launders remote recency into byte provenance". Observation
answers *where does the followed channel point now*; attestation answers *do
these bytes belong to that revision*. An artifact whose repository advanced is
`upgradable` precisely because both are recorded and they disagree — a single
flag cannot express it.

Ownership (Q5) is separate for the symmetric reason: an operator who accepted an
artifact has asserted something no later network call can retract.

### Digests carry role, algorithm **and** domain

`Digest{Role, Algorithm, Domain, Value, ComputedAt}`.

Q1 ruled the persisted identifier self-describing with `algorithm` and `domain`;
A1 added `role` and the decision document repeats role and algorithm without
mentioning domain. That silence is not disagreement, and dropping domain would
break the very distinction the ETag finding established: the Xet CAS hash and
`lfs.sha256` are **both SHA-256**, over different inputs. `(role, algorithm)`
alone cannot tell them apart, so `(role, algorithm, domain)` is the identifying
tuple.

### Acceptance is a question of reproducibility, not of algorithm

Q1 accepted SHA-256 over `raw-file` and nothing else, and explicitly withheld
permission for Git blob SHA-1. **A1 supersedes that**, and an earlier draft of
this design still carried the Q1 rule.

The reason Q1's position did not survive is that it makes an unreachable state
reachable. A repository that publishes *no* content identifier does not exist:
Git hashes everything it stores, so an identifier is always present and only its
domain varies. Under SHA-256-only, a file stored directly in Git publishes a Git
blob OID, Coquena declines it, and the artifact can never be matched — not
because the evidence is missing but because the policy refuses the only evidence
there is. That is the "evidence available but unsupported" branch the reviews
argued over, and accepting by reproducibility removes it rather than naming it.

So the test is: **can Coquena reproduce the published identifier from the
complete local bytes?** If yes, it is evidence. Today that admits SHA-256 over
`raw-file` and SHA-1 over `git-blob`, and between them they cover everything the
Hub serves — an artifact above 10 MiB cannot be stored in Git and therefore
carries `lfs.sha256`, and one below it carries a blob OID.

`xet-cas` stays unaccepted, and the reason is worth recording precisely because
it is *not* a claim about the identifier. The Xet file hash is a full-content
identifier and is reproducible: the published specification and its conformance
vectors were reproduced end to end from raw bytes during the RFC. Coquena simply
does not implement the chunker, and implementing it would buy nothing, since a
Git-backed Hub repository must supply the raw SHA-256 for its LFS pointer
anyway. Declining it is a statement about what Coquena implements. Recording it
with its domain keeps that honest, so a reader is never told "no evidence" when
the truth is "evidence Coquena does not compute".

The collision-resistance question `git-blob` raises is real and is not settled by
file size: SHA-1 is weak against a chosen-prefix attack regardless of how small
the input is. It is bounded instead by where the domain applies — artifacts of at
most 10 MiB, which no usable quantization reaches — and by the fact that the
domain is recorded, so a later policy can withdraw it by name without touching
any other evidence.

*Alternative considered and rejected*: named fields (`integrity: {...}`,
`repositoryMatch: {...}`). It encodes the current role set into the document
shape, so adding a role becomes a schema bump — and a hand-editable sidecar
should state what a value is rather than rely on where it sits.

### A managed download is attested by construction

`method: download | remote-comparison`, per Q1 and RFC F2.

This overturns the position an earlier draft of this design took, which was that
a download should record "no match established" because nothing compares the
received bytes against `Candidate.ContentSHA256`. That reasoning is wrong, and
the RFC had already rejected it: bytes fetched from a **revision-pinned URL**
have provenance from where they came from, not from a comparison. Only artifacts
adopted from an existing local cache need a hash to establish after the fact that
they belong to a revision. Recording downloads as unattested would understate
what Coquena actually knows, and re-derive a refuted conclusion — the specific
failure mode `AGENTS.md` exists to prevent.

`method` is what keeps the two honest without flattening them: an operator can
tell an attestation that came from the transfer from one reconstructed later.
Adding a digest comparison at download time remains worthwhile as defence in
depth and is `verify-download-against-remote-digest`; it strengthens the
evidence, it does not create the attestation.

### A disagreement is recorded, not refused

`reconcileAgainst` currently aborts when the local digest disagrees with the
published one, writing nothing. A3 reverses that, and A4 explains why it must.

The three ways an artifact can fail to match — the repository could not be
consulted, the bytes belong to a revision that no longer exists, or they are not
from that repository at all — are **indistinguishable from the artifact**. A4
removed `diverged` for exactly that reason: the word asserted a difference, when
in the case it actually covered nothing had been compared. Refusing on
disagreement makes the same mistake from the other side. It treats one of three
indistinguishable causes as decisive enough to discard the observation and hand
the operator an error instead of a working model.

The middle ground the current code misses is that recording a disagreement is
safe. The observation axis is not the attestation axis: writing what the
repository said cannot launder anything into provenance, because attestation
stays unset. Nothing an operator relies on is overwritten — the existing
attestation, the ownership assertion and the `integrity` digest are all left
alone — so the disagreement costs nothing and preserves what was learned.

That the bytes may belong to a **destroyed** revision is not speculative: a
publisher can super-squash history or delete LFS objects, and the Hub recommends
it for reclaiming storage. So "matches nothing available" is not evidence that
anything is wrong, and a design that refuses on it downgrades healthy artifacts
as publishers do routine housekeeping.

*Alternative considered and rejected*: a distinct standing for "compared and
disagreed", separate from "could not compare". It is more informative in the
moment and unusable as a durable state, because the next reconciliation of the
same artifact against a repository whose history has moved cannot tell which one
it is looking at.

### A companion is usable without its principal

`usabilityOf` reports a projector unusable when no compatible principal is
installed. A6 reverses this, and the reason is that `usable` had drifted from
what it means.

Usability answers one question: **is this artifact eligible to be referenced by a
preset?** A projector is exactly the kind of artifact a preset references, and
one projector may serve several principals. Which principal it accompanies is
decided by the preset that binds them. Gating it on what the library happens to
hold at read time pre-empts that decision from the wrong place, using an accident
of ordering: an operator who imports a projector first, meaning to fetch its
principal next, is told the artifact they just imported is unusable.

The asymmetry the current code documents without justifying — a principal
without a projector stays usable, a projector without a principal does not —
falls out once the question is stated properly. Neither can be *run* alone;
that is true of every companion and is not what usability asks.

The compatibility computation stays, because it remains useful for suggesting and
validating preset bindings. What changes is its consequence: "no compatible
principal installed" becomes advice rather than a reason for unusability.

### The verification record names the digest it checked

`Verification{Outcome, CheckedAt, AgainstDigest}`, written only by `verify` (A2).

The obvious alternative — store outcome and timestamp, and have any operation
that replaces the digest clear the record — reintroduces exactly the coupling the
authority partition exists to remove: `reconcile` would have to know about
`verify`'s field. Binding the record to the digest value inverts it. A record
naming a digest the sidecar no longer carries simply does not apply, so replacing
evidence invalidates the reading of it *by identity*, and `verify` stays the only
writer. Nobody has to remember to clear anything.

*Alternative considered and rejected*: recording the file's size and modification
time at verification instead. It answers a weaker question, and a file rewritten
in place at the same length with a restored mtime would defeat it.

### The attestation carries the same guard, for the same reason

`Attestation` binds a digest, and a recorded attestation whose bound digest is no
longer among the artifact's digests does not apply. `AppliedAttestation` is the
exact mirror of `AppliedVerification`, and the symmetry is the point: both axes
make a claim *about specific bytes*, so both stop applying when those bytes stop
being the recorded ones.

Without it there is a reachable state in which the record lies. An artifact
attested to revision A against digest X has its file changed to Y; reconciliation
records Y, the comparison disagrees so it establishes nothing, and
`preserveUnownedAxes` faithfully carries the old attestation forward — leaving a
sidecar that says *these bytes are from revision A* about bytes that are not.
Guarding at the reader neutralises that on every path at once, including
`upgrade`, rather than asking each writer to remember. The stale attestation may
remain in the document; what it may not do is be believed.

### `preserveUnownedAxes` cannot clear an axis, only carry it

The helper copies an axis forward when the writing command left it `nil`. That
makes "I did not touch this" the default, which is what the authority partition
needs today — but it also means **no writer can deliberately clear an axis**,
because clearing and not-writing are the same gesture.

Nothing needs that yet. Two things will. `upgrade` replaces an artifact's bytes
and must not inherit the attestation of the bytes it replaced; the digest guard
above already neutralises the claim, but the stale record would linger. And Q5
anticipates ownership being revoked, which is a writer setting an axis to
"absent" rather than declining to write it. Both want an explicit "clear this"
that the current shape cannot express — a tri-state, or a writer that names the
axes it owns instead of inferring them from `nil`. Recording it here so it is a
known limitation rather than a discovery.

### Staleness is derived from mtime, not persisted

The listing already stats every artifact it enumerates, so comparing modification
time against `CheckedAt` is free. Persisting a staleness flag would be wrong the
instant the file changed. mtime is advisory — trivially forged, unreliable across
some filesystems — so it only ever **weakens** a claim ("this result may be
old"), never establishes one. A stale record still reports what it recorded; it
is never upgraded to matched or downgraded to altered on mtime alone.

### Never verified is *unverified*, and unverified is usable

The backlog asks whether an artifact never verified reads as unknown or as clean.
Answer: unknown, rendered as unverified — and still **usable**.

Reading it as clean asserts a state on no evidence. Reading it as unusable makes
every artifact in every existing library unusable the moment this change lands,
since none carries the record. Only a recorded mismatch produces `altered`, which
is what the decision document says: `altered` is reached by "`verify` recorded a
mismatch", not by the absence of a record.

### `verified` / `partial` / `unattested` derive from the axes

The RFC's resolution requirements demanded this CLI vocabulary and left it to the
consuming change. It falls out of the axes with no extra field: attestation
present → `verified`; observation present, attestation absent → `partial`;
neither → `unattested`. Deriving rather than storing it keeps the same rule the
whole state vocabulary follows, and means `adopt-artifact-state-vocabulary` can
map `partial` onto `detached` (A4) without a schema change.

### `ETag` leaves the record and stays on the wire

`Artifact.ETag` moves to `Candidate.ETag`, beside the `ContentSHA256` already
there for exactly this reason — transport-scoped, `json:"-"`, never persisted.
Q1 removes it from the authority model and permits the implementation field to go
with the schema change; this takes that permission. `Job.ETag` is untouched: a
download job is live transport state, and `If-Range` needs precisely the opaque
last-hop value.

*Not done here*: reading `X-Linked-ETag` off the first response to recover
`lfs.sha256`. Reviewers proposed correcting the ETag rather than isolating it, and
the ruling chose removal. Coquena already gets `lfs.sha256` from the repository
API; a second path to the same value would be work justified by a header rather
than a need. The spec's prohibition is therefore worded against *opaque* tags
rather than against headers as a class — `X-Linked-ETag` carries a known domain
and would be admissible evidence if a need for it ever appeared.

*Answering the entry's own acceptance question*: no current behaviour reads
`Artifact.ETag` from a sidecar. Its only consumer is `manager.go:108`, reading it
off the freshly resolved candidate before any sidecar exists. Dropping it from
the persisted shape regresses nothing.

### Schema 4, and no reader for anything else

`RecordSchemaVersion` becomes 4, and the "accept the immediately preceding
version" rule is **deleted rather than re-applied**.

Coquena has not been released. The rule existed to protect an upgrade path for
users who do not exist, and it is not free: it is a second document shape living
inside the reader, exercised by tests, and permanently able to admit a partially
understood record. The evidence axes make that worse, because a schema-3 sidecar
can only be admitted by *guessing* — its single digest becomes `integrity` over
`raw-file`, which is true, while observation, attestation and ownership are
invented as absent. Guessing is precisely what this change exists to stop.

So the rule becomes: the current version, or invalid. An invalid sidecar is not a
loss — the artifact takes the recovery path that already exists for a foreign
GGUF, and `reconcile` or `import` writes a current one. The cost is a local hash
and a metadata request; the model file is never touched, moved or re-fetched.

*What this buys beyond simplicity*: with no legacy shape to admit, an
`integrity` digest becomes **mandatory in any valid sidecar**, since writing a
sidecar requires computing one. That retires `VerifyNoBaseline` — whose own
comment describes it as "an artifact installed, or reconciled, before content
hashing existed" — and with it the last state in which Coquena manages an
artifact it cannot check. A case disappears from the schema rather than being
carried forward as a branch nobody can reach on purpose.

*Alternative considered and rejected*: keeping a deprecated `Artifact.SHA256`
accessor over the digest collection so the ~49 test call sites could be migrated
gradually. It trades a mechanical sweep for a period in which two spellings of
the same evidence coexist — the exact ambiguity being removed. The suite breaks
once, in one commit, and is repaired.

*Note on `Record.Extra`*: unknown-key preservation stays, but its justification
changes. It is not a version bridge; it exists because sidecars are deliberately
hand-editable plain text and an operator's annotation should survive an operation
that had no reason to discard it.

## Risks / Trade-offs

- **The change is larger than a single backlog entry.** It implements the RFC's
  whole field-level answer. → Accepted: the axes are mutually defining, and
  landing them separately would mean three schema bumps and a window in which
  observation and attestation are collapsed — the bug being fixed.
- **A later schema bump for repository identity.** RFC F3 is unanswered.
  → Accepted. Guessing the owner's answer costs more than the bump: the choice
  propagates into presets, which bind by directory name.
- **`method: download` inherits the trust of the transfer.** A download that
  silently truncated or was tampered with in flight would be attested. → Size is
  already checked at completion; `verify-download-against-remote-digest` closes
  the remainder, and `method` makes the weaker basis visible rather than hiding
  it behind an undifferentiated "attested".
- **Every existing sidecar becomes invalid.** → Accepted deliberately. Coquena is
  pre-release, the sidecar is derived data, and recovery is one command that
  touches no model bytes. The alternative — a reader that admits a shape it can
  only partially understand — is the failure this change exists to remove.
- **The suite breaks in one step, not gradually.** Replacing `Artifact.SHA256`
  invalidates ~49 call sites across 14 test files, and `SchemaVersion` appears in
  21 more. → Accepted: groups 1–3 land as a single breaking commit and the sweep
  is mechanical. Staging it behind a deprecated accessor would keep two spellings
  of the same evidence alive across the change.
- **The digest list can be hand-edited into an invalid state.** Sidecars are
  deliberately hand-editable plain text. → At most one digest per
  `(role, algorithm, domain)`; a violation is an invalid sidecar rather than a
  silently preferred first entry.
- **mtime is unreliable.** → Used only to weaken a claim; see above.
- **Accepting `git-blob` admits SHA-1.** A chosen-prefix SHA-1 collision is
  feasible and does not become harder because the input is small. → Bounded by
  where the domain applies: at most 10 MiB, which no usable quantization reaches.
  The domain is recorded per digest, so withdrawing it later is a policy change
  by name rather than a schema change.
- **Recording a disagreement instead of refusing loosens a guard.** → The guard
  moves rather than disappearing: attestation stays unset, and the existing
  attestation, ownership and `integrity` digest are explicitly untouched. What
  the old refusal actually protected was the record, and the record is still
  protected.

## Migration Plan

There is no migration, by decision rather than by omission. Every existing
sidecar becomes invalid the moment this lands, and every affected artifact
appears as one Coquena does not manage until it is reconciled or imported again.

That is the whole plan, and it is safe for the reason that makes it acceptable
at this stage: **the sidecar is derived data and the model file is not**. Nothing
in a sidecar cannot be recomputed from the artifact plus one metadata request.
The operator re-runs reconciliation over the library, which re-hashes locally and
re-establishes provenance; no download is repeated.

Rollback is symmetric and equally cheap: reverting leaves schema 4 sidecars
unreadable, the same artifacts appear unmanaged, and the same command restores
them. No data is destroyed in either direction, because the local file is never
touched by any of it.

## Open Questions

None that affect the specs, the approach, or the task breakdown. Two questions
this change deliberately hands to their own backlog entries: which repository
name is canonical (RFC F3, `record-canonical-repository-name`) and how the seven
states are rendered (RFC A5, `adopt-artifact-state-vocabulary`).
