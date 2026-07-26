## 1. GGUF and Hugging Face foundations

- [x] 1.1 Define JSON-compatible model, source-identity, metadata, and download-job contracts with unit tests for validation and state transitions.
- [x] 1.2 Implement the bounded `RandomAccessReader` contract and local-file reader with tests that prove no tensor reads are required.
- [x] 1.3 Implement the HTTP Range reader, including remote length/validator discovery and bounded-range failure handling tests.
- [x] 1.4 Implement the GGUF header and metadata reader with fixtures covering representative scalar, array, unknown-key, and malformed-file cases.
- [x] 1.5 Implement public Hugging Face repository/file-URL resolution, GGUF filtering, selector disambiguation, requested-to-resolved revision pinning, SHA-pinned URL construction, and source identity tests.
- [x] 1.6 Add remote and local inspection service operations with tests that confirm raw metadata is retained and complete file downloads are not initiated for inspection.

## 2. Durable model library and downloads

- [x] 2.1 Implement model-scoped job creation beside the target, atomic JSON persistence, job lookup/listing exclusively from the configured models tree, cold-start recovery to paused, and path-confinement tests.
- [x] 2.2 Implement partial-file transfer with progress checkpoints, bounded retries, pause, and cancel; test each terminal and retry transition.
- [x] 2.3 Implement validator-aware HTTP Range resume against the persisted SHA-pinned URL, including changed-validator, unsupported-range, and partial-size mismatch failure tests.
- [x] 2.4 Implement configured-model-directory target derivation, collision/idempotency checks, and safe repository/filename sanitization tests.
- [x] 2.5 Implement completion size validation, local GGUF reinspection, atomic GGUF/JSON/`.revision` publication, rollback on either sidecar failure, and sidecar schema tests.
- [x] 2.6 Implement JSON/`.revision` consistency validation and revision lookup tests.
- [x] 2.8 Add service-level integration tests using a controllable HTTP fixture for resolve, inspect, download, pause/resume, cancellation, recovery, and failed validation.

## 3. CLI and HTTP API

- [x] 3.1 Add shared-model-service construction to the executable without coupling it to runtime registration or llama.cpp.
- [x] 3.2 Add `coquena model resolve`, `inspect`, and `download` commands, human output, schema-version-1 `--json`, and command parsing tests.
- [x] 3.3 Add `coquena model revision`, including consistent-sidecar tests.
- [x] 3.4 Add `coquena download list`, `status`, `pause`, `resume`, and `cancel` commands with state/error and JSON-output tests.
- [x] 3.5 Add local HTTP resolve, inspect, job creation, list/status, pause/resume/cancel, and safe JSON error endpoints with handler tests.
- [x] 3.6 Add a throttled SSE job event stream that sends an immediate snapshot, progress updates, and one terminal event; cover reconnect, terminal, unknown-job, and method tests.
- [x] 3.7 Update WebUI routing tests to prove every model API and SSE path bypasses static/client-route fallback.

## 4. Models WebUI

- [x] 4.1 Add an Angular model-management API service using RxJS boundaries and signal-backed view state, with HTTP and EventSource tests.
- [x] 4.2 Add a Models workflow to resolve repositories or canonical file URLs, select one GGUF candidate, and inspect bounded metadata summaries before download while retaining explicit raw inspection outside the DOM.
- [x] 4.3 Add job progress/status rendering and state-appropriate pause, resume, and cancel actions, including visible API failure states.
- [x] 4.4 Present useful metadata summaries and raw metadata as read-only reference beside existing manual preset controls; test that no field or preset mutation is triggered.

## 5. Verification and documentation

- [x] 5.1 Document public Hugging Face GGUF workflow, job recovery semantics, sidecar layout, CLI examples, and explicit metadata/runtime-argument separation in the root documentation.
- [x] 5.2 Run focused Go and Angular test suites, formatting/lint tasks, `task verify`, and `openspec validate add-gguf-model-management --strict`; fix regressions.

## 6. Manual WebUI verification follow-up

- [x] 6.1 Discover artifact sizes and validators through commit-pinned HEAD requests when Hugging Face repository siblings omit size metadata; recover size from download responses for existing zero-size jobs.
- [x] 6.2 Persist transfer checkpoints independently of an in-process progress callback and render human-readable sizes plus a determinate progress element in Models.
- [x] 6.3 Add HTTP Range read-ahead caching so remote Inspect completes without one request per GGUF metadata value, and expose visible inspection activity/results.
- [x] 6.4 Close paused SSE streams, reconnect them on resume, and execute WebUI resume asynchronously.
- [x] 6.5 Emit common model-service lifecycle/progress logs for both direct CLI and WebUI-backed operations.

## 7. Model-scoped recovery reconciliation

- [x] 7.1 Store new `.gguf.partial` and `.gguf.download.json` files beside the final configured target and publish by same-directory rename without copying the model.
- [x] 7.2 Discover installed and incomplete model units from the configured models tree after cold restart and expose them in the Models library.
- [x] 7.3 Reconcile a partial that is ahead of its last persisted checkpoint, retain safe failure when it is smaller, and surface backend resume diagnostics in WebUI and terminal logs.
- [x] 7.4 Fix and test the exact model-download cancel endpoint and remove its adjacent incomplete files.
- [x] 7.5 Summarize very large tokenizer arrays in default WebUI inspection responses while preserving explicit full raw inspection for CLI/API consumers.
- [x] 7.6 Remove installed-model update checking/downloading from the current change scope and record it for future work.
- [x] 7.7 Render independent download progress and controls inside every incomplete model-library entry and remove the shared download-management panel.
- [x] 7.8 Preserve the completed job snapshot in memory until SSE emits its terminal event, then refresh the installed library entry.
- [x] 7.9 Replace the WebUI raw inspection document and omission notice with a responsive read-only Metadata/Value table.
- [x] 7.10 Give each discovered/installed quantization an independent reusable collapsible metadata view, load installed sidecars lazily, and cache/display one optional avatar per publisher namespace.
- [x] 7.11 Accept download-management actions beneath `coquena model download` without interpreting action names as repository references, while retaining top-level aliases.
- [x] 7.12 Render human download listings as a table and gracefully convert CLI Ctrl+C during resume into an explicitly reported durable paused state.
- [x] 7.13 Render shared transfer progress logs with human-readable sizes and a percentage while preserving exact byte counts in structured state.
- [x] 7.14 Add a human-readable percentage and byte-progress column to CLI download list/status output.
