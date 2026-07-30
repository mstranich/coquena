## Context

The `model list` command in `cmd/coquena/model.go` renders a tabular listing of installed and unreconciled GGUF artifacts. The table includes a `REASON` column that prints the operator-facing detail from `entry.Usability.Detail`. This column was not requested by users and only adds noise.

The JSON output struct `modelListEntry` (line 46) has three fields related to usability:
- `reason` (line 46): machine-readable code (e.g., `"unreconciled"`, `"orphaned-projector"`)
- `detail` (line 47): human-readable text (e.g., `"the artifact has no valid sidecar..."`)

Both correspond to the removed table column.

## Goals / Non-Goals

**Goals:**
- Remove the `REASON` column from the `model list` table output
- Remove the `detail` and `reason` fields from the JSON output

**Non-Goals:**
- Change the `model list` table beyond removing the `REASON` column
- Modify any other command's output
- Change the underlying `Usability` struct or `UnusableReason` enum

## Decisions

### 1. Remove REASON column from table

The table header at `cmd/coquena/model.go:391` changes from:
```
STATE\tTYPE\tUSABLE\tREFERENCE\tFILENAME\tREASON
```
to:
```
STATE\tTYPE\tUSABLE\tREFERENCE\tFILENAME
```

The row output at `cmd/coquena/model.go:400` drops the `entry.Detail` argument from the format string, so only five values are printed.

**Rationale**: The column was not requested and only adds noise.

### 2. Remove `detail` and `reason` from JSON output

The JSON struct `modelListEntry` (lines 46-47) removes the `Reason` and `Detail` fields and their `json` tags.

**Rationale**: Both fields correspond to the removed table column. The `state` field already indicates whether the artifact is installed or unreconciled.

## Risks / Trade-offs

[Trade-off: The `Usable` field remains on `modelListEntry` and `model.go` struct.] → Retained for JSON output consumers. It indicates whether the artifact is usable without needing the reason.

[Trade-off: JSON consumers who use `reason` or `detail` will need to update.] → These fields are removed along with the table column they correspond to.
