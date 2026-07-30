# Tasks: Restructure model list output — group by repository, QUANT column, CountParameters

## Tasks

- [x] **T1**: Add `CountParameters` field to `Classification` struct in `internal/model/classification.go`
- [x] **T2**: Implement `CountParameters` function in `internal/model/gguf.go` with `ggufTypeSize` mapping
- [x] **T3**: Compute `CountParameters` during `classifyFile` in `internal/model/library.go`
- [x] **T4**: Update `modelListEntry` struct to add `Repository`, `Quant` fields in `cmd/coquena/model.go`
- [x] **T5**: Restructure `runModelList` to group by repository, add QUANT column, implement sorting
- [x] **T6**: Update JSON encoding to use dictionary keyed by repository
- [x] **T7**: Update `model_usability_test.go` for new output format
- [x] **T8**: Add tests for `CountParameters` function
- [x] **T9**: Add tests for new sorting logic

## Dependencies

- T1, T2, T3: No dependencies
- T4, T5, T6: Depends on T1, T2, T3
- T7: Depends on T4, T5, T6
- T8: Depends on T2
- T9: Depends on T5

## Acceptance

- Human output groups entries by repository
- QUANT column shows correct format for each type
- Sorting follows type → state → parameters
- JSON output uses dictionary keyed by repository
- Tests pass for new output format
