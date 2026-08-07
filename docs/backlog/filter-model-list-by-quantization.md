# Filter the model list by quantization and artifact facts

## Status

- **State:** open
- **Opened:** 2026-08-05
- **Owner:** project-owner
- **Priority:** high
- **Related RFC:** `gguf-model-artifact-identity`
- **Candidate change:** `filter-model-list`

## Problem

`coquena model list` has no filters. Every installed artifact is printed, and
the operator narrows the result by reading. The library grows with every
download, and companion artifacts multiply the row count further.

This was deferred deliberately. The project owner recorded that
`general.file_type` would be useful for filtering and improving `model list`,
but in a separate change from the classification work. That classification work
is now complete: quantization identity is correct, versioned, and available
from the sidecar without opening the artifact.

The enabling work has landed and the payoff has not. Of everything this RFC
made possible, this is the change the operator would notice first.

## Current behavior

1. `model list` enumerates the library grouped by repository.
2. It reports the declared quantization recipe, parameter count, and state.
3. It offers no way to restrict output to a quantization, a type, a state, or
   a usability condition.
4. Narrowing is done by eye, or by piping into an external tool.

## Required outcome

Future work should:

- filter by declared quantization recipe, using the same case-insensitive
  matching the reference grammar already uses;
- filter by artifact type, so principals and projectors can be listed apart;
- filter by state and usability, so unreconciled or unusable artifacts can be
  found without scanning the whole catalog;
- report artifacts whose derived block is stale, using `derivation.version`
  compared against `DerivationVersion` — this is an `O(1)` sidecar read and
  requires no hashing, no artifact open, and no network access;
- keep every filter free of network access and free of gigabyte reads, so the
  command's cost profile does not change.

The stale-derivation filter is the one that replaces a bulk repair sweep. With
it, the operator can name exactly which artifacts need
`verify --repair-derived` instead of running it blindly across the library.

## Acceptance questions

- Should filters compose with AND, and is an OR form needed?
- Does a quantization filter match the declared recipe only, or also an
  inferred physical quantization? These are different evidence and the RFC
  requires they remain distinguishable.
- Should `--json` output carry the filter that produced it?
- Does filtering interact with repository grouping, or is a flat filtered list
  the better shape?

## Scope boundary

Filtering reads the sidecar only. Do not open artifacts, hash bytes, or contact
Hugging Face to satisfy a filter. Do not introduce a filter that would require
recomputing derived evidence; report staleness instead and let
`verify --repair-derived` fix it per artifact.

## Relevant implementation areas

- `cmd/coquena/model.go`
- `internal/model/manager.go`
- `internal/model/offline.go`
- `internal/model/record_json.go`
