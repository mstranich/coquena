# Replace installed model bytes through an authoritative upgrade

## Status

- **State:** blocked
- **Opened:** 2026-08-05
- **Owner:** project-owner
- **Priority:** normal
- **Related RFCs:** `gguf-model-artifact-identity`, `model-artifact-attestation`
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

## What this command is

`upgrade` is the acting half of a two-command pair. The project owner stated
the division on 2026-08-10, with a second analogy alongside the `apt` one
already recorded in `detect-model-updates`:

```text
update   consults the provider            git fetch    apt update
upgrade  downloads and applies changes    git pull     apt upgrade
```

The consequence is that `upgrade` is defined by *acting on what `update`
found*, not by any single operation. Replacing bytes is one kind of change it
applies. Relocating a renamed repository — assigned here by the owner on
2026-08-10, per `hugging-face-redirects-renamed-repositories` — is another. Both
are the same act at the level that matters: `update` observed a divergence,
`upgrade` resolves it.

Assigning relocation to `import` (today `reconcile`) was considered and
rejected: adopting local bytes and applying an observed remote change are
different acts, and `align-model-command-surface` requires that the command not
choose between them silently.

## Not every change it applies has the same prerequisite

Byte replacement is blocked by `establish-sidecar-authority` for the reason
stated above. **Relocation is not.** It replaces no bytes, so no attestation
precondition applies: the 307 from the provider API is itself the identity
evidence, and there is nothing to compare digests against because the content
does not change. Q6 of `model-artifact-attestation` — what precondition byte
replacement must enforce — does not arise.

The recommended handling is to stage the change: `upgrade` lands with
relocation first and gains byte replacement once `establish-sidecar-authority`
exists. That keeps one verb with one owner while the reported blocking stays
accurate. Splitting relocation into its own entry is the alternative if the
`blocked` state must be literal rather than approximate.

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
- report the resulting canonical reference and authority state to the operator;
- relocate a renamed repository's directory to its canonical name, consuming
  the rename recorded by `update` rather than re-discovering it;
- update or report every preset bound to the old path when relocating — see the
  hazard below;
- treat relocation as recoverable: record the intended move before performing
  it, so an interruption leaves a resumable state rather than a half-moved
  tree.

## Verified hazard: relocation breaks preset bindings

`internal/presets/render.go:61` computes a preset's model path with
`model.PathFor(modelsDir, p.Model.Requested.Repository, …)`, and
`internal/model/library.go:25` splits that repository string to build
`<models>/<user>/<repo>/<filename>`. The path is derived from the **requested**
name the preset persisted (`internal/presets/types.go:21`), not the canonical
one.

Moving the directory therefore invalidates every preset bound to that
repository, which fails with "model path could not be computed". This is not
hypothetical: both Ornith repositories in the operator's library are already
renamed upstream.

A rename can also change the **publisher** segment
(`deepreinforce-ai` → `ornith-ai`), so relocation may create a new publisher
directory and leave the old one holding only its
`publisher-avatar.<format>`. Orphaned-publisher cleanup is part of this work,
not an afterthought.

## Acceptance questions

- Which authority states permit an upgrade to start?
- Does `upgrade` rewrite preset bindings automatically, or refuse and report
  them for the operator to fix? Rewriting is convenient but edits documents the
  operator authored; refusing is safe but strands the rename.
- Is relocation offered per reference like byte replacement, or is a renamed
  repository relocated as a whole? The rename is a repository-level fact, so a
  per-artifact form may not be meaningful.
- What happens if the operator declines the relocation — does the index keep
  reporting the divergence indefinitely, and does `update` keep surfacing it?
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
