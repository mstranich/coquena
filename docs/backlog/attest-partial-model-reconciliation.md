# Attest local bytes during partial model reconciliation

## Status

- **State:** open
- **Opened:** 2026-08-04
- **Owner:** project-owner
- **Priority:** high
- **Related RFC:** `gguf-model-artifact-identity`
- **Candidate change:** `establish-sidecar-authority`

## Problem

Online reconciliation can resolve the repository's current revision while being
unable to compare the local artifact bytes with a remote content hash. In that
partial result, the sidecar may associate the current remote revision with local
bytes that have not been attested to belong to that revision.

The stable artifact-reference change intentionally did not solve this provenance
problem. A canonical reference can identify what the sidecar claims without
proving that the claim is authoritative.

## Witness

The manual witness was the locally cached Gemma 4 12B repository. Reconciliation
completed with a diagnostic equivalent to:

```text
partial: Hugging Face reported no content hash to compare against
```

This is not a command failure. It means Coquena hashed the local bytes but could
not compare them with an LFS object ID or other authoritative remote byte hash.

## Current behavior

1. Reconciliation computes and records the local artifact hash.
2. The remote repository resolves to its current revision.
3. When no remote content hash is available, byte equality is not established.
4. The sidecar can nevertheless record the resolved remote revision.
5. Later verification can validate the recorded local baseline, but that only
   proves the bytes have not changed since reconciliation.
6. Update checks remain partial when the remote service still exposes no
   comparable content hash.

Therefore, an older local file with the same filename could be attributed to a
newer repository revision without byte-level evidence.

## Required outcome

Future work should:

- distinguish a remotely observed revision from a byte-attested revision;
- persist the strength and source of the attestation in the sidecar;
- avoid presenting partial reconciliation as complete provenance;
- define useful update-check behavior when no remote byte hash exists;
- make model upgrade consume or establish authoritative evidence rather than
  relying on an ambiguous partial state.

## Acceptance questions

- Which sidecar field is authoritative for local-byte identity?
- Should partial reconciliation preserve the prior attested revision, record a
  separate observed revision, or leave revision identity unresolved?
- Which remote evidence is acceptable besides an LFS object ID?
- How should CLI and WebUI distinguish `verified`, `partial`, and `unattested`?
- What precondition must `model upgrade` enforce before replacing local bytes?

## Scope boundary

Do not fold this work back into stable artifact reference parsing. Address it in
the sidecar-authority change and use that contract as an input to a later model
upgrade change.

## Relevant implementation areas

- `internal/model/reconcile.go`
- `internal/model/reconcile_repository.go`
- `internal/model/check_updates.go`
