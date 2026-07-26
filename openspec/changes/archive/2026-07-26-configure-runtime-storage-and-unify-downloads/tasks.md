## 1. Configuration foundation

- [x] 1.1 Replace portable configuration parsing, validation, and atomic publication with schema version 3 and independent model/runtime directory overrides; reject legacy schemas without migration.
- [x] 1.2 Add effective runtime-directory read/write services that validate existing absolute writable paths and reject changes while runtime work is nonterminal.
- [x] 1.3 Remove `<home>/downloads` from portable initialization and refactor runtime store opening so installations, `<asset>.partial` transfers, `<asset>.download.json` sidecars, metadata, and locking use only the effective runtime directory.

## 2. Configuration interfaces

- [x] 2.1 Extend `coquena config` grammar, text/JSON output, dependency injection, and tests with `runtimes-directory`.
- [x] 2.2 Extend the WebUI configuration service/API with strict GET/PUT runtime-directory behavior and Go tests.
- [x] 2.3 Add an Angular runtime-directory settings service/control with loading, success, and safe error states; add component/service tests and rebuild embedded assets.
- [x] 2.4 Replace the global Runtime Operation presentation with a Models-like managed runtime library whose individual entries expose status, transfer and extraction phases, progress, errors, and lifecycle controls.

## 3. Shared download jobs

- [x] 3.1 Extract reusable durable-job, adjacent `<asset>.partial`/`<asset>.download.json` sidecar, progress, and recovery primitives from the model download flow; cover exact progress fields, safe errors, and root scanning.
- [x] 3.2 Adapt model jobs to the shared primitives without losing interruption, resume, cancel, or adjacent-artifact guarantees.
- [x] 3.3 Implement runtime sidecars under the configured runtime directory, discovery by scanning that tree, pause/cancel/restart recovery, and verified resume while preserving official provenance, digest, archive, probe, atomic publication, and runtime locking tests.
- [x] 3.4 Separate short GitHub metadata request deadlines from large archive transfers so runtime assets are caller-cancellable without inheriting the API client's whole-request timeout.

## 4. Unified CLI behavior

- [x] 4.1 Refactor unfiltered `coquena download` management and its resource-filtered model/runtime namespaces to share one typed job catalog and lifecycle, making no-ID `status` the matching table view.
- [x] 4.2 Render list/status tables as `ID | TYPE | NAME | STATUS | PROGRESS | PATH`, retaining a separate IEC-size/percentage PROGRESS value and exact schema-versioned JSON bytes.
- [x] 4.3 Add CLI integration tests for mixed global lists, filtered model/runtime list and status views, cross-entry-point state equivalence, runtime pause/resume/cancel, invalid runtime selections, and JSON/text compatibility.

## 5. Verification and documentation

- [x] 5.1 Update user-facing CLI/configuration documentation with runtime-directory and unified download-management examples.
- [x] 5.2 Run focused Go and Angular test suites, static checks, and a manual CLI/WebUI smoke test using an external runtime directory.
