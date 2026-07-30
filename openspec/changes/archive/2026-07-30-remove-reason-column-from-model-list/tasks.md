## 1. Remove REASON column from model list

- [x] 1.1 Modify `cmd/coquena/model.go:391` to change the table header from `"STATE\tTYPE\tUSABLE\tREFERENCE\tFILENAME\tREASON"` to `"STATE\tTYPE\tUSABLE\tREFERENCE\tFILENAME"`
- [x] 1.2 Modify `cmd/coquena/model.go:400` to change the format string from `"%s\t%s\t%s\t%s\t%s\t%s\n"` to `"%s\t%s\t%s\t%s\t%s\n"` (drop the `entry.Detail` argument)
- [x] 1.3 Modify `cmd/coquena/model.go:46-47` to remove the `Reason` and `Detail` fields and their `json` tags from `modelListEntry`

## 2. Update tests

- [x] 2.1 Update `cmd/coquena/model_usability_test.go` to reflect the removed column in expected output
