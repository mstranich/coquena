# A companion without an installed principal is still usable

## Status

- **State:** closed
- **Opened:** 2026-08-11
- **Closed:** 2026-08-15
- **Owner:** project-owner
- **Priority:** normal
- **Story step:** *"import files obtained elsewhere"*
- **Candidate change:** `adopt-artifact-state-vocabulary`

## Problem

`usabilityOf` reports a projector as unusable when no compatible principal is
installed at that moment:

```go
if entry.Classification.Type == TypeProjector && !hasCompatiblePrincipal(...) {
    return unusable(ReasonOrphanedProjector, "no installed principal model is
    compatible with this projector; install one it can accompany")
}
```

`usability.go:109-110`. The same file documents the asymmetry without
justifying it: a principal with no projector stays usable, but a projector with
no principal does not.

## Why it matters

`usable` means one thing — eligible to be referenced by a preset. A projector is
exactly the kind of artifact a preset references, and one projector may serve
several principal models. Which principal it accompanies is a decision the
preset makes; the library reporting it unusable pre-empts that decision from the
wrong place, using the accident of what happens to be installed right now.

The consequence is backwards: an operator who imports a projector first, meaning
to pair it with a model they are about to fetch, is told the artifact they just
imported is unusable.

No engine can run a companion alone — but that is true of every companion, and
it is not what `usable` asks.

## Required outcome

Future work should:

- report a companion as usable regardless of which principals are installed;
- keep the compatibility computation, which remains useful for suggesting and
  validating preset bindings;
- retire `ReasonOrphanedProjector`, or restate it as an informational note
  rather than a reason for unusability;
- leave the genuinely unusable states — `untracked`, `altered`, `incomplete` —
  untouched.

## Acceptance questions

- Should the listing still flag "no compatible principal installed" as advice,
  and if so in which column?
- Does a preset binding a companion with no principal fail at preset validation
  instead, where the pairing is actually decided?

## Scope boundary

Companion usability only. Do not change companion compatibility criteria or
bundle completeness, which stay with `address-quantization-bundles`.

## Relevant implementation areas

- `internal/model/usability.go`

## Outcome

Closed by `establish-sidecar-authority` rather than by
`adopt-artifact-state-vocabulary`, because that change rewrote the usability
requirement anyway and leaving the companion rule behind would have meant
shipping a requirement everyone already knew to be wrong.

`ReasonOrphanedProjector` is retired. Usability now answers one question — is
this artifact eligible to be referenced by a preset — and a companion is
eligible whatever the library holds beside it. The asymmetry the old code
documented without justifying (a principal without a projector stayed usable, a
projector without a principal did not) disappears once the question is stated
properly: neither can be *run* alone, and that is not what eligibility asks.

The compatibility computation survives; only its consequence changed. "No
compatible principal is currently installed" is reported as **advice**, not as a
reason for ineligibility, so an operator who imports a companion first — meaning
to fetch its principal next — is no longer told the artifact they just imported
is unusable.
