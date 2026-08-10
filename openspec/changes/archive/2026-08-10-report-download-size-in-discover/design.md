## Context

Discovery already requests the Hugging Face revision endpoint with `blobs=true`, stores each sibling's published `size` in `Artifact.SizeBytes`, and retains bounded model-card support while deliberately not calling it. Its result is nevertheless artifact-shaped: a split GGUF is rendered as several candidates, even though `quantizations-can-be-shard-directories` establishes that those shards form one operator-visible quantization. `hugging-face-publishes-content-hash-under-blobs` establishes that size metadata arrives in the same revision response already needed for discovery.

The separate `address-quantization-bundles` backlog item owns changing the addressable download/library unit. This design adds a discovery projection only; it does not preempt those mutation semantics.

## Goals / Non-Goals

**Goals:**

- Derive deterministic quantization groups from repository-relative paths and standard split-GGUF names.
- Make total-size knowledge explicit and avoid partial totals that look complete.
- Keep the existing candidate evidence usable by API and WebUI consumers.
- Restore model-card access as an opt-in CLI cost.

**Non-Goals:**

- Making a grouped quantization downloadable as one new unit.
- Inferring installed state or subtracting locally present bytes.
- Charging shared companions to every quantization or deciding future mandatory-companion semantics.
- Filtering the repository candidate set.

## Decisions

### Add a derived grouping beside candidates

`Discovery` will retain `Candidates` and add a `Quantizations` projection plus a separate companion projection. A quantization contains a stable display label, its constituent discovered candidates, a byte total, and an explicit boolean indicating whether that total is known.

Keeping candidates avoids a breaking API migration and preserves independent inspection diagnostics. Replacing candidates outright was rejected because the current Angular and HTTP surfaces consume artifact-level classification. Teaching each renderer to regroup independently was rejected because CLI and JSON could then disagree.

### Group from publisher paths and split naming, not quantization classification

Artifacts under the same non-root repository directory belong to that directory's quantization group. At the root, standard split names ending in `-00001-of-000NN.gguf` through `-000NN-of-000NN.gguf` group by the filename prefix and declared shard count. Other principal GGUF files remain single-file groups. Path separators are normalized for grouping but original sibling filenames are preserved in output.

This uses the publication structure demonstrated by the linked finding and still handles a flat standard split series. Grouping by derived quantization classification was rejected: classification may fail independently, and two distinct artifacts can legitimately share a recipe label. A generalized bundle identity is deferred to `address-quantization-bundles`.

Candidates classified as companions are projected separately and excluded from quantization totals. Unknown or failed classifications remain principal candidates unless their available evidence identifies them as companions; discovery must not silently lose them.

### Treat missing or non-positive size as unknown for the whole group

The total is known only when every constituent has a positive published size. Otherwise the output carries an explicit unknown state and human output renders an em dash rather than a partial sum. Integer summation must reject overflow and degrade the group to unknown rather than wrap.

Using a nullable pointer alone was rejected because the existing zero value already conflates absent size with zero; an explicit knowledge flag makes JSON intent testable.

### Fetch the model card only behind `--model-card`

CLI parsing passes an include-card option to discovery. Only then does the manager call the existing bounded `Resolver.ModelCard` function and populate `Discovery.ModelCard`. The normal path continues to issue no card request. With `--json`, the option includes `modelCard` in the same schema-versioned result rather than mixing prose with JSON.

A separate `model card` command was rejected as unnecessary command-surface growth, and fetching the card by default but hiding it was rejected as wasted network work.

## Risks / Trade-offs

- [A publisher places unrelated GGUFs in one directory] → Group only directory-contained split series when the directory contains more than one GGUF; retain ordinary non-split files as individual groups unless the repository layout provides unambiguous bundle evidence.
- [Malformed or incomplete shard names create a false complete group] → Validate consistent `of-N` counts and sequence membership; expose the observed files and do not claim bundle completeness when the series is inconsistent.
- [Additive JSON fields surprise strict consumers] → Keep schema version 1 and existing fields because the change is additive; cover the exact envelope in compatibility tests.
- [Per-candidate remote inspection remains relatively expensive] → Aggregation uses already collected results and introduces no additional artifact reads or default model-card request.

## Migration Plan

Ship the projection additively, switch CLI human rendering to it, and keep current candidates throughout. Rollback removes the additive projections and renderer use without changing persisted state because discovery is read-only.
