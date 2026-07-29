## 1. Create constant file

- [x] 1.1 Create `internal/model/filetypes.go` with the `fileTypeNames` map.

## 2. Update model list table

- [x] 2.1 Modify `cmd/coquena/model.go` to use `FormatReference` for the REFERENCE column.
- [x] 2.2 Remove REPOSITORY column from the table header (keep FILENAME).
- [x] 2.3 Update the table output to show REFERENCE and FILENAME.

## 3. Update resolveLibrary

- [x] 3.1 Modify `internal/model/offline.go` to use `fileTypeNames` for the REFERENCE column.

## 4. Add tests

- [x] 4.1 Add test for `fileTypeNames` map.
- [x] 4.2 Add test for the new REFERENCE column format.
