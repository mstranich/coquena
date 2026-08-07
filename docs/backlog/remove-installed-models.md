# Delete an installed quantization or companion

## Status

- **State:** blocked
- **Opened:** 2026-08-05
- **Owner:** project-owner
- **Priority:** high
- **Story step:** *"delete a quantization or a companion"*
- **Blocked by:** `address-quantization-bundles`
- **Candidate change:** `remove-installed-models`

## Problem

There is no way to delete a model. `coquena preset remove`, `coquena field
remove` and `coquena runtime remove` all exist; `coquena model remove` does not.

A step of the story has no operation at all, and the omission survived two weeks
of design work because there was no story to check the command surface against.

The consequence is not theoretical: quantizations are large, the operator is
managing disk space, and today the only way to reclaim it is to delete files by
hand — which also strands the sidecar and desynchronises the library from disk.

## Why it is blocked

Deletion cannot be defined before the addressable unit is. Deleting "a file"
would leave a shard set half-present and a companion stranded. The rule the use
case adopts is expressed in terms of quantizations:

> Delete a quantization and its companion stays if any other quantization still
> needs it; delete the last and the companion goes too.

`address-quantization-bundles` establishes the unit that makes that statement
meaningful.

## Required outcome

Future work should:

- delete a quantization as a unit: its files and its sidecar together;
- apply the sharing rule to companions, deriving the answer from what remains in
  the repository rather than from a stored count;
- leave the repository directory clean — no orphan sidecars, no empty
  directories masquerading as artifacts;
- refuse, or warn explicitly, when a preset still binds the artifact;
- require an explicit reference. No bulk form.

## Acceptance questions

- Does deletion require confirmation, and what does the non-interactive
  behaviour look like?
- What happens to a preset bound to the deleted quantization — refuse, or
  delete and leave the binding unresolved with a clear state?
- Is deleting a companion directly permitted, or only as a consequence of
  deleting the last quantization that needed it?
- Does an imported, operator-owned artifact delete the same way as a downloaded
  one?

## Scope boundary

Local deletion only. Nothing is removed remotely, and this is not a step of
`upgrade`, which must stage its own replacement rather than deleting first.

## Relevant implementation areas

- `cmd/coquena/model.go`
- `internal/model/manager.go`
- `internal/model/library.go`
- `internal/presets/binding.go`
