# Proposal: Restructure model list output — group by repository, QUANT column, CountParameters

## Problem

`coquena model list` currently shows a flat list with a `REFERENCE` column that uses `general.file_type` for all artifacts, including mmproj. For mmproj files, this displays the principal's quantization (e.g., `Q4_0_4_8 (removed)`) instead of the mmproj's identity, which is confusing.

Additionally, the flat layout doesn't group related artifacts (model + its mmproj) together, making it harder to see the full picture per repository.

## Goal

Restructure `model list` output to:
1. Group entries by repository
2. Replace `REFERENCE` with `QUANT` (short reference relative to repo)
3. Add `CountParameters` as a `Classification` field, always computed during download/reconcile
4. Sort within each repo: type (model → mmproj → lora → draft → vocoder), then state (installed → unreconciled), then parameters

## Scope

- `cmd/coquena/model.go`: Restructure `runModelList` output
- `internal/model/classification.go`: Add `CountParameters` field
- `internal/model/gguf.go`: Implement `CountParameters` function
- `internal/model/manager.go`: Compute `CountParameters` during `classifyFile`
- `cmd/coquena/model_usability_test.go`: Update tests

## Out of Scope

- Presets companion references (still use `:mmproj` format)
- JSON output structure for other commands (download, reconcile, etc.)

## Constraints

- `QUANT` for `model` is just the quantization (e.g., `Q4_K_M`)
- `QUANT` for `mmproj` is `architecture:quantization` (e.g., `clip:BF16`)
- `CountParameters` is always computed, even if 0
- Sorting priority: type → state → parameters

## References

- GGUF loading phases: [discovery/gguf-loading-principal-vs-mmproj-metadata-phases](obs-339c63744364c017)
- mmproj is filename convention, not metadata field: [discovery/mmproj-is-filename-convention-not-metadata-field](obs-e3b05cac8b60911e)
- CountParameters: classification field, always computed: [decision/countparameters-classification-field-always-computed](obs-5788805ded3700d2)
