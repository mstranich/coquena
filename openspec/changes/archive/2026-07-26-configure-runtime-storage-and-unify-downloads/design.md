## Context

The configuration document currently supports only `models.directory` (schema
2). Model downloads are durable sidecar-backed jobs rooted in the configured
models directory. In contrast, the runtime manager always uses the portable
home, stages archives in `<home>/downloads`, and runs synchronously; the WebUI
runtime operation state is process-local. This makes separate storage
impossible and prevents the CLI from giving both artifact classes the same
management UX.

## Goals / Non-Goals

**Goals:**
- Provide one validated effective runtime directory across CLI and WebUI.
- Persist independent model and runtime directory overrides atomically in the
  new pre-release configuration schema.
- Give model and runtime downloads durable typed jobs with common CLI commands
  and progress/result fields.
- Retain every runtime integrity, publication, and mutual-exclusion guarantee.

**Non-Goals:**
- Migrate or remain compatible with old portable homes, old configuration
  schemas, or `<home>/downloads` state.
- Replace the current WebUI runtime SSE flow with a cross-resource download
  dashboard.
- Run multiple downloads concurrently or support non-llama.cpp runtimes.

## Decisions

### Store runtime configuration beside model configuration

Replace `config/coquena.json` with schema version 3 containing optional
`models.directory` and `runtimes.directory` fields. Both values use the same
absolute-existing-writable validation and the write operation reads, updates,
and republishes the complete document atomically. Schemas 1 and 2 are rejected:
the project has not reached a publishable milestone, so retaining their layout
or migration code would add unsupported complexity.

The effective default remains `<home>/runtimes`; `<home>/downloads` is removed
from the portable-home layout. A configured runtime root contains its own
controlled store: completed `llama.cpp` installations plus incomplete runtime
artifacts and their adjacent `<asset>.partial` bytes and `<asset>.download.json` job sidecars. Runtime
discovery recursively scans that configured root for valid installations and
incomplete sidecars. No runtime mutation reads or writes `<home>/downloads` or
`<home>/tmp`. Keeping all runtime-owned mutable state under one root makes
relocation meaningful and prevents cross-volume publication problems.

### Use a shared job contract with resource-owned executors

Add an internal download application package that owns a versioned `Job`
envelope: opaque ID, resource kind (`model` or `runtime`), status, timestamps,
display name, progress byte counters, safe error, and resource-specific
request/checkpoint payload.  It owns discovery across configured storage
roots, lookup, and status transitions.  Model and runtime executors retain
their domain validation and persistence details but implement the same create,
run/resume, pause, cancel, list, and status contract.

Runtime jobs persist resolved official asset/dependency metadata in an adjacent
`<asset>.download.json` sidecar and transfer bytes to an adjacent `<asset>.partial` file under the
configured runtime root. Resume reconciles actual partial length, revalidates
official provenance, and verifies the final digest before bytes are extracted
or published. A single runtime job is allowed while the runtime mutation lock
is held. The reusable job/state, partial-file, progress, and recovery code
shall be factored from the model implementation where behavior is identical;
resource executors retain only their domain-specific validation and publication.

Alternatives rejected: command aliases alone would not provide common pause,
resume, or crash recovery; relocating only final runtimes would leave large
archives on the portable volume and violate the configured-storage expectation.

### Make CLI adapters resource-neutral at the global boundary

`coquena download list|status|pause|resume|cancel` delegates to the shared
catalog without a kind filter; `status` without a job ID is a table view of all
matching jobs, while an optional exact ID retains single-job inspection.
`coquena model download list|status|pause|resume|cancel`
and `coquena runtime download list|status|pause|resume|cancel` use the same
catalog with their respective kind filter; they do not implement separate state
machines. Human list and status tables use `ID | TYPE | NAME | STATUS | PROGRESS |
PATH`: `TYPE` disambiguates models from runtimes, `NAME` replaces model-specific
terminology, and `PROGRESS` remains distinct from status as `50.0% (2.0 GiB /
4.0 GiB)` when the total is known (otherwise an em dash). JSON stays
schema-versioned and preserves exact byte values.

### Make WebUI runtime entries self-managing

The Runtimes page presents installed and incomplete runtimes as one managed
library, following the Models page rather than exposing a global "Runtime
Operation" panel. Each runtime entry owns its status label, download progress,
verification/decompression/installation phase text, safe error, and applicable
Pause, Resume, Cancel, or Remove controls. The backend may still serialize
runtime mutations for safety, but that implementation constraint is not modeled
as a global operation in the user experience.

Composite runtime downloads distinguish per-asset progress from overall job
progress. The primary archive and dependencies each have resumable sidecars, so
persisted paused progress is an asset metric. Live SSE additionally exposes the
aggregate metric. The WebUI labels both explicitly and never substitutes one
for the other when state changes.

### Extend configuration adapters symmetrically

CLI gains `config runtimes-directory`; the HTTP API gains
`/api/config/runtimes-directory`; the Angular settings view receives a second
independent service/control.  APIs follow existing strict-body/error behavior.

## Risks / Trade-offs

- [Runtime resume after a partial archive changes integrity semantics] → retain
  bounded download limits and verify final SHA-256 before extraction; discard
  invalid checkpoints.
- [Changing a directory while a job is active can orphan state] → reject that
  resource's directory update while a nonterminal job exists, with no config
  write.
- [Cross-root aggregate listing can encounter malformed state] → report a safe
  diagnostic for that entry and continue listing valid jobs.
- [Schema evolution can overwrite an existing override] → setters always read
  the full document and tests cover mixed model/runtime updates.

## Migration Plan

This is a pre-release breaking refactor. Initialization and configuration
accept only schema 3; the application neither reads nor migrates schemas 1/2
or `<home>/downloads` contents. Development homes are recreated or manually
cleaned before use. Rollback is a source checkout rollback, not an in-place
data migration contract.

## Open Questions

- None for this planning slice; the implementation can select a compact
  checkpoint representation while preserving the specified job contract.
