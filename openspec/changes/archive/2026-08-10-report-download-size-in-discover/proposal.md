## Why

`coquena model discover` must help an operator choose a quantization by showing the complete set of files it represents and the resulting download size. Today it reports individual GGUF candidates, so split quantizations appear as unrelated rows and their real disk cost must be calculated by hand.

## What Changes

- Make the default discovery summary group GGUF artifacts into operator-facing quantizations, listing each quantization's constituent files and their summed download size.
- Report an unknown total when any required file lacks size metadata rather than presenting a misleading partial total.
- Keep shared companions separate from quantization totals, because they may serve several quantizations and this change does not alter the current optional companion-download behavior.
- Add an explicit CLI option for fetching and showing the model card; the default path remains a compact comparison view and does not request the card.
- Preserve per-artifact discovery evidence for API and inspection consumers while adding the grouped projection needed by terminal and machine-readable discovery output.
- Do not report local installation state, change download behavior, add discovery filtering, or make quantization bundles independently downloadable in this change.

## Capabilities

### New Capabilities

- None.

### Modified Capabilities

- `gguf-model-interfaces`: Discovery presents quantizations as file sets with trustworthy total sizes and exposes the model card only on explicit CLI request.

## Impact

- Affects discovery aggregation and Hugging Face metadata use in `internal/model`, plus CLI parsing/rendering in `cmd/coquena/model.go`.
- Extends the discovery JSON result without removing the existing per-candidate evidence consumed by the local API and Angular UI.
- Reuses sibling `size` values already returned by the existing `?blobs=true` revision request; no new dependency or default network request is required.
