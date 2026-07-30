## 1. Split FileTypeNames map

- [x] 1.1 Move the 8 deprecated fileType entries (4, 5, 31, 32, 33, 36, 37, 38) from `FileTypeNames` to a new `DeprecatedFileTypeNames` map in `internal/model/filetypes.go`, stripping the " (removed)" suffix
- [x] 1.2 Add `FileTypeDisplayName(ft int64) string` lookup function that checks `FileTypeNames` first, then `DeprecatedFileTypeNames`, returning "unknown" if neither matches

## 2. Update consumers to use lookup

- [x] 2.1 Update `computeQuant` in `cmd/coquena/model.go` (lines 370, 375, 382) to use `FileTypeDisplayName` instead of direct `FileTypeNames` lookup
- [x] 2.2 Update `FormatReference` in `internal/model/offline.go` (line 32) to use `FileTypeDisplayName` instead of direct `FileTypeNames` lookup

## 3. Update tests

- [x] 3.1 Update `internal/model/filetypes_test.go` — remove the 8 test cases that expect " (removed)" suffix; add test cases for `DeprecatedFileTypeNames` and `FileTypeDisplayName` fallback behavior

## 4. Verify

- [x] 4.1 Run `go build ./...` to confirm compilation
- [x] 4.2 Run `go test ./internal/model/... ./cmd/coquena/...` to confirm all tests pass
- [x] 4.3 Run `openspec validate --strict` to confirm spec compliance
