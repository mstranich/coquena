# Design: Restructure model list output — group by repository, QUANT column, CountParameters

## Architecture Decisions

### A1: QUANT column format

- For `model`: shows quantization type (e.g., `Q4_K_M`)
- For `mmproj`: shows `architecture:quantization` (e.g., `clip:BF16`)
- Rationale: `TYPE` already identifies the role, so QUANT can be specific to the type

### A2: CountParameters in Classification

- `CountParameters` is a field in `Classification`, not just `Record`
- Always computed during GGUF reading (even if 0)
- Rationale: It's a property of the artifact, derived from the GGUF tensor table

### A3: Sorting priority

1. Type: `model` → `mmproj` → `lora` → `draft` → `vocoder`
2. State: `installed` → `unreconciled`
3. Parameters: descending (`CountParameters`)

### A4: JSON output structure

- Changes from flat list to dictionary keyed by repository
- Each entry has: `state`, `type`, `usable`, `quant`, `filename`
- Full reference is preserved in a separate field (optional)

## Implementation Plan

### Step 1: Add CountParameters to Classification

- Add field to `Classification` struct in `internal/model/classification.go`
- Implement `CountParameters` function in `internal/model/gguf.go`
- Compute during `classifyFile` in `internal/model/manager.go`

### Step 2: Restructure model list output

- Update `modelListEntry` struct to add `Repository`, `Quant` fields
- Update `runModelList` to:
  - Group by repository
  - Add repository header
  - Use QUANT column instead of REFERENCE
  - Sort within each group
- Update JSON encoding to use dictionary format

### Step 3: Update tests

- Update `model_usability_test.go` for new output format
- Add tests for `CountParameters` function
- Add tests for new sorting logic

## Files Changed

| File | Change |
|------|--------|
| `internal/model/classification.go` | Add `CountParameters` field |
| `internal/model/gguf.go` | Add `CountParameters` function, `ggufTypeSize` map |
| `internal/model/manager.go` | Compute `CountParameters` during `classifyFile` |
| `cmd/coquena/model.go` | Restructure `runModelList`, update `modelListEntry` |
| `cmd/coquena/model_usability_test.go` | Update tests for new output format |

## Risks

- **Risk**: Tensor table reading is complex and error-prone
  - **Mitigation**: Test with multiple quantization types
- **Risk**: Breaking changes to JSON output
  - **Mitigation**: Update all consumers of JSON output
- **Risk**: Performance impact from reading tensor table
  - **Mitigation**: Compute once during download/reconcile, store in sidecar

## Open Questions

- Should QUANT include full reference (e.g., `repo:clip:BF16`) or just relative (e.g., `clip:BF16`)?
  - **Decision**: Relative to repo (e.g., `clip:BF16`), since repo is in the header
- Should we support other artifact types (lora, draft, vocoder) in QUANT format?
  - **Decision**: TBD, but TYPE column already supports them
