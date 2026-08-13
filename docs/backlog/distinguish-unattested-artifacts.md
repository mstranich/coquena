# Never present an unattested artifact as an attested one

## Status

- **State:** open
- **Opened:** 2026-08-04
- **Owner:** project-owner
- **Priority:** high
- **Story step:** *"import files obtained elsewhere"*
- **Candidate change:** `establish-sidecar-authority`

## Problem

Online reconciliation can resolve a repository's current revision while being
unable to compare the local bytes against a remote content hash. When that
happens, **nothing records that the result was partial**, and the artifact
becomes indistinguishable from one whose bytes were actually attested.

This entry is only about that: the gap between what Coquena knows and what it
shows. What "attested" should mean, how its strength is recorded, and what
`upgrade` may require are the subject of the RFC `model-artifact-attestation`
and are deliberately not restated here.

## Witness

The locally cached Gemma 4 12B repository. Reconciliation reported `partial` on
both artifacts, and the listing changed only in the artifact's favour:

```text
before   unreconciled  current  model  no   …:gemma-4-12B-it-QAT-Q4_0.gguf
after    installed     current  model  yes  …@aaec3dd…:gemma-4-12B-it-QAT-Q4_0.gguf
```

No column, state or usability value records the difference. The artifact now
looks exactly like the models Coquena downloaded itself, which are attested by
construction.

## Why the defect survives every other fix

Per `hugging-face-publishes-content-hash-under-blobs`, the diagnostic that
prompted this — *"Hugging Face reported no content hash to compare against"* —
misattributes the cause: the hash is published and simply was not requested.
That client defect is `request-remote-content-hash` and should land first.

But it does not close this entry. Some repository will always fail to provide
comparable evidence — no LFS, a network failure mid-check, a private fine-tune
with no remote at all. Presenting an unattested artifact as attested is a
representation problem, and it persists for whatever residue remains.

## Current behaviour

`partial` is not a state of the artifact. It is a boolean on the result of one
reconciliation run, printed once and discarded; the sidecar has no field for it.
Usability is derived from whether a valid sidecar exists, and never consults
attestation — so the artifact reports `usable: yes` because nothing wrote down
that it should not.

## Required outcome

Future work should:

- persist whether local bytes were attested against remote evidence, or only
  hashed locally;
- surface that distinction wherever an operator chooses an artifact — the
  listing at minimum;
- decide whether an unattested artifact is `usable`, and make the answer
  deliberate rather than incidental;
- keep a private fine-tune, which has no remote and never will, from being
  reported as deficient.

## Acceptance questions

- Is an unattested artifact usable? It currently reports `yes`.
- Is "attested locally" a third value, or the absence of attestation?
- How does the CLI render the distinction without adding a column for a state
  most artifacts are not in?

## Scope boundary

Representation only. Do not define attestation semantics here; consume what
`model-artifact-attestation` decides. Do not absorb the client defect tracked in
`request-remote-content-hash`.

## RFC resolution consumed

`model-artifact-attestation` closed on 2026-08-11. The implementation contract
is now settled: local, remote-observation, attestation, ownership and drift
evidence remain independent; accepted attestation is SHA-256-only with an
algorithm/domain-bearing identifier; `STATE` is derived and distinguishes
`local`, `diverged`, `current`, `upgradable`, `modified`, `incomplete` and
`unreconciled`. This entry is therefore open and ready to be incorporated into
`establish-sidecar-authority` rather than blocked on further architecture.

## Relevant implementation areas

- `internal/model/reconcile.go`
- `internal/model/reconcile_repository.go`
- `internal/model/usability.go`
- `cmd/coquena/model.go`
