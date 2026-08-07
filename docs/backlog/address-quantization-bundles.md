# Make the quantization, not the file, the addressable unit

## Status

- **State:** open
- **Opened:** 2026-08-05
- **Owner:** project-owner
- **Priority:** high
- **Story step:** *"I ask Coquena for the quantization … it must be a complete bundle"*
- **Candidate change:** `address-quantization-bundles`

## Problem

Coquena manipulates files. The operator manipulates quantizations. Nearly every
complication in the current model library follows from that mismatch, and the
ecosystem does not leave it as a matter of taste: per
`quantizations-can-be-shard-directories`, `unsloth/DeepSeek-R1-GGUF` publishes
**no `.gguf` at its root at all** — every quantization is a directory of shards.

Two relationships hide behind the word "file", and they are not the same:

```text
shard      a mandatory part of exactly one quantization; useless alone
companion  shared across several quantizations of one repository
```

## What the file-as-unit choice currently costs

- A projector can exist alone, so `orphaned-projector` had to be invented as a
  reason an artifact is unusable.
- Usability of one artifact depends on which others are installed, so
  `AssessUsability` takes the whole library and cannot be computed per artifact.
- `hasCompatiblePrincipal` reconstructs the bundle on every read.
- `--with-companions` makes the bundle optional, contradicting the story.
- Library enumeration walks the tree and reports every file, so one sharded
  quantization appears as N rows. Shards 2..N carry no full metadata, so N−1 of
  them classify as unusable and one is real.
- Path handling assumes `<repo>/<filename>`; a nested sibling path misparses.

## The sharing and deletion rule

Stated by the project owner and adopted by the use case:

> A projector is complementary to one or several quantizations. Download a
> second quantization and it is already there. Delete a quantization and the
> projector stays if any other still needs it; delete the last and it goes too.
> The same applies to any companion kind that appears later.

This needs **no persisted reference count**. "Does any remaining quantization in
this repository require it?" is answerable by enumerating the repository at the
moment of deletion, and a derived answer cannot drift the way a stored counter
can — a file removed with the operating system's file manager would silently
corrupt a counter.

The sharing boundary is the repository. Two repositories publishing the same
model each carry their own companion copy, and they do not interact.

## Required outcome

Future work should:

- make a quantization resolve to a set of files, and treat the set as complete
  or incomplete rather than each file as present or absent;
- report one row per quantization, with its total size;
- read metadata from the shard that carries it rather than classifying each
  shard independently;
- handle repository paths that contain directories;
- express deletion in terms of the sharing rule above;
- remove `--with-companions`: a bundle is not optional.

## Acceptance questions

- Is a partially downloaded set a quantization in a bad state, or not a
  quantization yet?
- Does a shard set share one sidecar, or does each file keep one?
- How is a private fine-tune with a single file expressed in the same model?
- What identifies "the shard that carries the metadata" without opening all of
  them?

## Scope boundary

The unit and its consequences for download, enumeration and deletion. Deletion
itself is `remove-installed-models`. Do not fold in attestation, update
detection, or the reference grammar, which is already settled.

## Relevant implementation areas

- `internal/model/manager.go`
- `internal/model/library.go`
- `internal/model/usability.go`
- `internal/model/compatibility.go`
- `cmd/coquena/model.go`
