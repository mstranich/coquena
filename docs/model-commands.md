# `coquena model` command contracts

This document defines the operator-facing boundaries of the `coquena model`
namespace. It distinguishes the behavior implemented today from behavior that
has been accepted for a planned change. A planned contract is not documentation
that the corresponding flag or behavior is already available.

## Status vocabulary

- **Implemented**: available in the current CLI.
- **Accepted target**: approved by the project owner, but still requires an
  OpenSpec change and implementation.
- **Candidate**: a possible later capability whose contract is not yet settled.

## Command matrix

| Command | Status | Primary authority | Network | Reads model bytes | May write metadata | May write model bytes |
|---|---|---|---:|---:|---:|---:|
| `resolve <repository-or-url>` | Implemented | Hugging Face | yes | no | no | no |
| `discover <repository-or-url[:selector]>` | Implemented | Hugging Face | yes | no | no | no |
| `download <repository-or-url[:selector]>` | Implemented | Hugging Face | yes | yes | yes | yes |
| `download list|status|pause|resume|cancel` | Implemented | download manager | no | no | job state only | through an active download |
| `revision <reference>` | Implemented | installed sidecar | no | no | no | no |
| `list` | Implemented | local catalog and sidecars | no | no | no | no |
| `verify <reference>` | Implemented | local bytes and sidecar baseline | no | full artifact | no | no |
| `verify <reference> --repair-derived` | Implemented | local bytes | no | full artifact | derived fields only | no |
| `check-updates <reference>` | Implemented read-only; metadata recording is a later target | Hugging Face | yes | no | not currently | no |
| `reconcile <reference> --online` | Implemented | local artifact and Hugging Face | yes | as required | provenance/attestation | no |
| `reconcile <reference> --accept` | Implemented | operator | no | as required | accepted identity/ownership | no |
| `upgrade <reference>` | Candidate | remote attestation plus operator request | yes | yes | yes | yes |

## References and cardinality

The current CLI commonly accepts logical references such as
`user/repository:QUANT`. Work on stable artifact addressing is intentionally
separate from the GGUF evidence correction. Until that work is specified and
implemented, this document does not promise that every logical selector names a
unique physical file.

Commands that operate on one artifact must refuse ambiguity rather than select
a calibration, filename, or revision implicitly. The exact canonical reference
grammar belongs to a later change.

## Remote exploration

### `resolve`

```console
coquena model resolve org/repository
```

Resolves a Hugging Face repository or URL into Coquena's remote repository
representation. It may use the network and does not modify the local catalog,
sidecars, or model bytes.

### `discover`

```console
coquena model discover org/repository
coquena model discover org/repository:Q4_K_M
```

Lists remote GGUF candidates and shows which candidate a selector would match.
It is an exploration command: it may query Hugging Face, but it does not
download artifacts or mutate local state.

## Downloading and job management

### `download`

```console
coquena model download org/repository:Q4_K_M
coquena model download org/repository:Q4_K_M --with-companions
```

Downloads the selected artifact through the shared durable download manager.
`--with-companions` may enqueue additional compatible artifacts, with one job
per artifact. Download completion writes the artifact and its local record.

The nested job commands operate on model download jobs:

```console
coquena model download list
coquena model download status <job-id>
coquena model download pause <job-id>
coquena model download resume <job-id>
coquena model download cancel <job-id>
```

## Local catalog queries

### `revision`

```console
coquena model revision org/repository:Q4_K_M
```

Prints the resolved revision recorded in an installed artifact's sidecar. It is
local-only and does not establish that the remote repository is still at that
revision.

### `list`

```console
coquena model list
coquena model list --json
```

Lists local artifacts and their sidecars. It must remain cheap and read-only:
it performs no Hugging Face request, full-artifact hashing, GGUF rescan, repair,
or reconciliation.

The accepted GGUF evidence change will add a derivation version to sidecars so
`list` can identify stale derived evidence by comparing small integers. That
check must not open or hash the model artifact.

### Removed: `inspect`

```console
coquena model inspect org/repository:Q4_K_M  # rejected
```

The CLI surface has been removed. `verify` is the operator-facing comparison between bytes-derived
facts and the sidecar, while download will retain the internal `Inspection`
projection to avoid parsing a header twice.

Removing the command does **not** mean deleting the internal inspection type or
denying that the GGUF header is primary evidence.

## Local verification and repair

### `verify`

```console
coquena model verify <reference>
coquena model verify <reference> --json
```

The implemented command hashes the complete local artifact and compares it with
the SHA-256 baseline recorded in the sidecar. Its outcomes are matched,
mismatched, or no baseline. It makes no network request and is read-only.

The command must continue to be read-only by default. A verification result is
returned to the operator and is not persisted as a `lastVerifiedAt` event.

### `verify --repair-derived`

```console
coquena model verify <reference> --repair-derived
```

The flag recalculates only evidence derived from the local bytes, such
as GGUF classification, parameter count, and derivation version. Repair is
per-artifact and opt-in.

The operation must:

- require the current bytes to match the stored integrity baseline;
- preserve source, remote revision, provenance, attestation, drift, ownership,
  and the baseline digest;
- never query Hugging Face;
- never modify model bytes;
- tolerate the older sidecar version it is intended to repair;
- report declared GGUF recipe and physical tensor composition as distinct
  evidence rather than collapsing them into one outcome.

No `verify --all` form is part of the accepted contract.

## Remote drift and reconciliation

### `check-updates`

```console
coquena model check-updates <reference>
coquena model check-updates <reference> --json
```

The implemented command queries Hugging Face and reports whether the installed
artifact appears current, has an update, or can only be compared partially. It
does not change model bytes and currently does not persist the observation.

A later authority-model change may allow it to record remote drift metadata,
like a bounded `git fetch`, without changing derivation, provenance, ownership,
or model bytes. That persistence contract is not part of the GGUF evidence
change.

### `reconcile --online`

```console
coquena model reconcile <reference> --online
```

Consults Hugging Face and reconciles local artifacts with remote provenance and
content evidence. It may update sidecars but must not replace or modify the
model bytes. Refused or ambiguous artifacts remain operator-visible.

### `reconcile --accept`

```console
coquena model reconcile <reference> --accept
```

Records an explicit operator acceptance for one local artifact. It must not be
silently applied to a set. It modifies sidecar identity or ownership evidence,
not model bytes.

`reconcile` intentionally has no `--all` mode and rejects `--json` because the
current workflow may prompt for an operator decision.

## Future `upgrade` command

`model upgrade <reference>` is a candidate derived from the distinction between
observing remote drift and changing local bytes. It does not exist today. Its
download, replacement, rollback, and operator-confirmation semantics require a
separate RFC or change before implementation.

## Change ownership

The accepted implementation sequence is:

1. `correct-gguf-quantization-evidence`: trustworthy local GGUF evidence,
   derivation versioning, tolerant sidecar reading, and
   `verify --repair-derived`.
2. A later artifact-reference change: canonical physical addresses and
   ambiguity handling.
3. A later sidecar-authority change: bounded drift, attestation, and ownership
   axes with explicit writers.
4. A later upgrade change, if approved.

New unresolved architectural questions should be handled by a derived RFC. An
implementation-ready decision should instead become a bounded OpenSpec change.

## GGUF validation fixtures

The reproducible test suite uses small synthetic GGUF documents that exercise
metadata traversal, 64-bit tensor dimensions, structural-scope filtering, and
the overlapping numeric value `15` in both enum domains. The operator's Ornith
Q4_K_M artifact is not stored in this repository because it is a multi-gigabyte
model. When that artifact is locally available, `model verify` provides the
optional end-to-end acceptance check; its absence does not make the synthetic
parser and contract tests non-reproducible.
