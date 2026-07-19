## Why

Coquena can describe the host but operators still manage llama.cpp distributions manually. The next reversible step is a runtime-manager workflow, analogous to fnm, nvm, or SDKMAN, that quickly discovers host-compatible official builds and safely manages concrete local installations without hiding their upstream identity.

## What Changes

- Add `runtime remote`, defaulting to the latest official release and showing only distributions compatible with the current OS, native architecture, and detected backend capabilities.
- Add remote filters for release, CPU/GPU target, backend, and backend version, plus bounded history lookup; show each primary asset filename and required companion assets.
- Add `runtime download` with two complementary selection modes: an exact primary asset ID, or a convenient release/backend selector whose release defaults to `latest`; require exactly one compatible candidate before any mutation.
- Download composite distributions such as Windows CUDA, verify every asset using the digest supplied by GitHub, extract safely, validate `llama-server`, and publish atomically.
- Add metadata-driven local listing with current compatibility state and exact upstream asset ID.
- Add removal by exact local asset ID while protecting unrelated installations.
- Provide concise human output and strict machine-readable JSON.
- Keep cross-platform/cross-architecture downloads, emulation, forced incompatible downloads, activation, automatic best-backend recommendation, upgrades, rollback, model management, execution, externally supplied builds, and non-llama.cpp providers out of scope.

## Capabilities

### New Capabilities

- `llamacpp-runtime-artifact-management`: Host-compatible discovery and safe local lifecycle of official versioned llama.cpp runtime distributions.

### Modified Capabilities

None.

## Impact

The Go CLI gains a `runtime` command family and llama.cpp catalog, compatibility, download, archive, metadata, locking, and local-store code under `internal/runtime/llamacpp`. It consumes the existing normalized system report, reads official GitHub Releases metadata over HTTPS, and writes only beneath the initialized portable installation's `downloads`, `runtimes`, and `tmp` directories. No main configuration schema or active-runtime behavior changes.
