## Why

Operators can place large GGUF models outside the portable home, but runtime
archives and installed llama.cpp runtimes are always stored underneath it.
This is inconsistent in both the CLI and WebUI and makes it difficult to use a
dedicated volume.  The CLI also has two different download experiences: model
downloads are durable jobs managed through `coquena download`, while runtime
downloads are synchronous one-off commands.

## What Changes

- Add an independently configurable, existing, absolute, writable runtime
  directory with a portable-home default; expose it through the CLI, local HTTP
  API, and the WebUI Settings page alongside the models directory.
- Replace the pre-release portable configuration with one schema that persists
  model and runtime directory overrides atomically without losing either
  setting; no legacy-schema compatibility is required.
- Introduce durable typed download jobs for both model and runtime downloads.
  Make `coquena download` the unfiltered CLI management surface, and retain
  `coquena model download` and `coquena runtime download` as filtered views
  over the same lifecycle for listing, status, pause, resume, and cancellation.
- Preserve `coquena model download` and `coquena runtime download` as
  resource-specific creation/management entry points that delegate to the
  same job lifecycle and expose a common result/progress shape.
- Keep runtime-specific provenance, digest, extraction, probing, immutable
  publication, and mutation-lock safeguards intact.

**Non-goals:** this change does not migrate existing installed runtimes or
models automatically, unify the WebUI's model/runtime download screens, add
parallel downloads, or relax runtime verification and archive-safety rules.

Because Coquena has not reached a publishable release, compatibility with the
previous portable layout, `<home>/downloads`, or prior configuration schemas is
explicitly out of scope.

## Capabilities

### New Capabilities
- `runtime-directory-configuration`: Configure and present the effective
  runtime storage directory through shared configuration, CLI, local API, and
  WebUI settings.
- `unified-download-management`: Define durable typed CLI download jobs and a
  single management contract for models and runtimes.

### Modified Capabilities
- `portable-initialization`: Evolve the portable configuration schema while
  retaining compatibility with prior initialized installations.
- `llamacpp-runtime-artifact-management`: Store managed runtime staging and
  installations under the effective runtime directory and execute downloads as
  managed jobs.
- `gguf-model-interfaces`: Align resource-specific model download CLI behavior
  with the shared download-management lifecycle.

## Impact

- Configuration: `internal/installation`, `cmd/coquena/config.go`,
  `internal/webui/config.go`, and Angular settings services/components.
- Runtime storage and lifecycle: `internal/runtime`, `internal/runtime/store`,
  `cmd/coquena/runtime*.go`, and runtime WebUI wiring.
- Download CLI: `cmd/coquena/model.go`, `cmd/coquena/download.go`, plus new or
  refactored shared internal download-job components and tests.
