# Spec: Model list output — group by repository, QUANT column, sorting

## Context

The `coquena model list` command currently displays a flat list of all artifacts with columns: `STATE`, `TYPE`, `USABLE`, `REFERENCE`, `FILENAME`. The `REFERENCE` column uses `FormatReference` which shows `<repo>:<quant>` for all artifacts, including mmproj. This is confusing because mmproj files show the principal's quantization (e.g., `Q4_0_4_8 (removed)`) instead of their identity.

## Current Behavior

```
STATE	TYPE	USABLE	REFERENCE	FILENAME
unreconciled	model	yes	Q8_K	ornith-1.0-35b-Q4_K_M.gguf
unreconciled	mmproj	no	Q4_0_4_8 (removed)	mmproj-gemma-4-12B-it-QAT-BF16.gguf
```

## Desired Behavior

Group by repository with a header, use `QUANT` column (short reference relative to repo), and sort within each group.

```
REPOSITORY: lmstudio-community/gemma-4-12B-it-QAT-GGUF
  STATE          TYPE    USABLE  QUANT              FILENAME
  unreconciled   mmproj  yes     clip:BF16          mmproj-gemma-4-12B-it-QAT-BF16.gguf
  unreconciled   model   yes     Q4_0               gemma-4-12B-it-QAT-Q4_0.gguf

REPOSITORY: prism-ml/Bonsai-27B-gguf
  STATE          TYPE    USABLE  QUANT              FILENAME
  unreconciled   model   yes     NVFP4              Bonsai-27B-Q1_0.gguf
  unreconciled   mmproj  yes     clip:BF16          Bonsai-27B-mmproj-BF16.gguf
```

## Requirements

### R1: Group by repository

- Entries are grouped by `Source.Repository`
- Each group has a header: `REPOSITORY: <repository>`
- Empty groups are not displayed

### R2: QUANT column

- For `model`: shows the quantization type (e.g., `Q4_K_M`, `Q8_K`)
- For `mmproj`: shows `architecture:quantization` (e.g., `clip:BF16`, `clip:Q8_K`)
- For other types (lora, draft, vocoder): TBD

### R3: Sorting within repository

- Primary: type priority (`model` → `mmproj` → `lora` → `draft` → `vocoder`)
- Secondary: state (`installed` → `unreconciled`)
- Tertiary: `CountParameters` (descending)

### R4: JSON output structure

- Changes from flat list to dictionary keyed by repository
- Each entry has: `state`, `type`, `usable`, `quant`, `filename`
- Full reference is preserved in a separate field (optional)

```json
{
  "schemaVersion": 1,
  "result": {
    "lmstudio-community/gemma-4-12B-it-QAT-GGUF": [
      {
        "state": "unreconciled",
        "type": "mmproj",
        "usable": true,
        "quant": "clip:BF16",
        "filename": "mmproj-gemma-4-12B-it-QAT-BF16.gguf"
      },
      {
        "state": "unreconciled",
        "type": "model",
        "usable": true,
        "quant": "Q4_0",
        "filename": "gemma-4-12B-it-QAT-Q4_0.gguf"
      }
    ]
  }
}
```

### R5: Backward compatibility

- `--json` output structure changes (new dictionary format)
- Human output columns change (REFERENCE → QUANT, add REPOSITORY)
- Existing presets with `:mmproj` references continue to work (not affected by this change)

## Acceptance Criteria

- [ ] Human output groups entries by repository
- [ ] QUANT column shows correct format for each type
- [ ] Sorting follows type → state → parameters
- [ ] JSON output uses dictionary keyed by repository
- [ ] Tests updated for new output format
