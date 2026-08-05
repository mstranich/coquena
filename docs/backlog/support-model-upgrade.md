# Replace installed model bytes through an authoritative upgrade

## Status

- **State:** blocked
- **Opened:** 2026-08-05
- **Owner:** project-owner
- **Priority:** normal
- **Related RFC:** `gguf-model-artifact-identity`
- **Blocked by:** `establish-sidecar-authority`
- **Candidate change:** `support-model-upgrade`

## Problem

Coquena can observe remote drift with `model check-updates`, but it has no
operator-facing operation that replaces an installed GGUF with authoritative
remote bytes. Downloading another artifact, reconciling metadata, and verifying
an existing integrity baseline answer different questions and must not be
presented as an upgrade workflow.

An upgrade cannot be defined safely until the sidecar authority contract
distinguishes observed remote revisions, byte-attested revisions, operator
ownership, local integrity, and derived evidence. Otherwise Coquena could
replace bytes based on a partial or ambiguous provenance claim.

## Required outcome

Future work should:

- provide an explicit, per-artifact `coquena model upgrade <reference>`
  operation rather than changing bytes as a side effect of `check-updates`;
- require or establish authoritative remote byte evidence before replacement;
- preserve the authored selector while updating the bound canonical artifact
  and its resolved revision intentionally;
- stage and publish model bytes and the adjacent sidecar as one recoverable
  logical unit;
- define confirmation, interruption, failure recovery, and rollback semantics;
- keep derivation, integrity, attestation, drift, and operator ownership in
  their single-writer authority domains;
- report the resulting canonical reference and authority state to the operator.

## Acceptance questions

- Which authority states permit an upgrade to start?
- Must the target remote bytes expose a comparable content hash before
  download, or can Coquena establish authority after a complete transfer?
- How is the previous artifact retained or restored if sidecar publication
  fails after the new bytes have been downloaded?
- Which preset and companion bindings remain valid when the resolved revision
  or exact filename changes?
- Which confirmation and non-interactive behavior should the CLI expose?

## Scope boundary

Do not implement upgrade semantics until `establish-sidecar-authority` defines
the required evidence and sidecar writers. Do not fold replacement into
`check-updates`, `verify`, reconciliation, or an automatic bulk operation.
WebUI work remains postponed until the project owner explicitly resumes it.

## Relevant implementation areas

- `cmd/coquena/model.go`
- `internal/model/check_updates.go`
- `internal/model/library.go`
- `internal/model/manager.go`
- `internal/model/types.go`
