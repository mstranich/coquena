## Why

The `model list` command prints a `REASON` column that shows operator-facing detail. This column was not requested by users and adds noise to the common case where every entry is usable. The JSON output includes `detail` and `reason` fields that correspond to this column; since the column is removed, both fields should be removed.

## What Changes

- **Remove REASON column**: Strip the `REASON` column from the `model list` table header and row output.
- **Remove `detail` and `reason` from JSON**: Strip both fields from the JSON output since they correspond to the removed column.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

None.

## Impact

- `cmd/coquena/model.go`: `runModelList` table header (line 391) and row output (line 400) will no longer emit the `REASON` column.
- `cmd/coquena/model.go`: JSON output will no longer include the `detail` and `reason` fields.
