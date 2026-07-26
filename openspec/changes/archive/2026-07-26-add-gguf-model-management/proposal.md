## Why

Coquena already manages the location in which models live, but operators must still use external tooling to discover, inspect, and download GGUF artifacts. Bringing that workflow under Coquena makes the model library reproducible and independent from any installed inference runtime.

## What Changes

- Add Hugging Face GGUF resolution, remote metadata inspection, and managed resumable downloads for public repositories and resolvable Hugging Face file URLs.
- Add a Go random-access GGUF metadata reader that works over local files and HTTP Range requests without reading tensors or downloading an entire artifact for inspection.
- Persist incomplete content and recovery state beside each configured model target, discover installed/incomplete units after cold restart, publish completed artifacts with individual raw-metadata JSON and commit-SHA `.revision` sidecars, and cache an optional publisher avatar at namespace level.
- Expose the shared backend service through CLI commands, JSON output, local HTTP APIs, and an API-backed Models WebUI workflow.
- Keep model-library metadata informational: it must not generate llama.cpp arguments, parse runtime help, or alter the existing manual runtime-field/preset workflow.

## Capabilities

### New Capabilities
- `gguf-model-resolution`: Resolve public Hugging Face repositories and file URLs to GGUF artifacts and inspect raw GGUF metadata.
- `gguf-model-downloads`: Create, discover, resume, pause, cancel, validate, and publish durable revision-pinned model download jobs.
- `gguf-model-library`: Store completed GGUF artifacts with portable per-artifact JSON and `.revision` sidecars in the configured model directory.
- `gguf-model-interfaces`: Provide equivalent model-management operations through CLI, local HTTP API, and the Models WebUI.

### Modified Capabilities

- `webui-serving`: Route model-management API requests before static and Angular route fallback.

## Impact

- Adds Go packages for Hugging Face resolution, GGUF parsing, persistent download jobs, and model-library storage; uses the existing installation models-directory configuration.
- Extends `coquena model` and `coquena download`, WebUI HTTP routing, and the Angular Models workspace.
- Requires outbound HTTPS access to Hugging Face and standard HTTP Range/conditional-request handling; no database or llama.cpp dependency is introduced.
