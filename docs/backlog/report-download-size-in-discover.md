# Make discovery answer "what will this cost me?"

## Status

- **State:** open
- **Opened:** 2026-08-05
- **Owner:** project-owner
- **Priority:** high
- **Story step:** *"discover which quantizations are available … so I know how many GB this is going to cost me"*
- **Candidate change:** `report-download-size-in-discover`

## Problem

The story is explicit that discovery exists to support a decision: which
quantization to take, knowing which files will actually be fetched and how much
disk that costs. `model discover` currently leads with the repository's model
card — a markdown document that is interesting once and noise every time
afterwards, and that pushes the information the operator came for further down.

Choosing a quantization from a terminal should be a glance, not a scroll.

## Current behaviour

1. `discover` resolves the repository and its siblings.
2. It renders the model card by default.
3. The quantizations and their files are reported alongside it.

## Required outcome

Future work should:

- report the available quantizations, the files each resolves to, and the total
  size of each, as the default output;
- sum the size across the set — per `quantizations-can-be-shard-directories`, a
  quantization may be a directory of shards, so one file's size is not the
  answer and a sharded quantization must not appear as N unrelated entries;
- move the model card behind an explicit flag;
- keep the output narrow enough to compare quantizations by eye in a terminal.

## Acceptance questions

- Is the total download size available without extra requests? Per
  `hugging-face-publishes-content-hash-under-blobs`, `?blobs=true` returns
  `size` per sibling alongside the content hash, so the same call may serve
  both.
- Should companions be included in a quantization's reported size, given they
  are shared and may already be present locally?
- Should discovery indicate which quantizations are already installed, or does
  that belong to `list`?
- What is shown for a repository whose siblings expose no size?

## Scope boundary

Presentation and the data needed to produce it. Do not add filtering here;
narrowing an existing library is `filter-model-list-by-quantization`. Do not
change what downloading does.

## Relevant implementation areas

- `cmd/coquena/model.go`
- `internal/model/discover.go`
- `internal/model/huggingface.go`
