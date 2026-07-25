## 1. Shared runtime application service

- [x] 1.1 Extract normalized runtime records, requests, mutations, progress reporting, and the runtime manager from `cmd/coquena` into an internal reusable package without changing CLI behavior.
- [x] 1.2 Adapt CLI command parsing and formatting to the shared service, and preserve its runtime-management tests.
- [x] 1.3 Add focused tests proving the shared service retains compatible discovery, exact asset resolution, idempotency, safe removal, mutation-lock behavior, and aggregate/per-asset progress reporting.

## 2. Runtime-management HTTP API

- [x] 2.1 Extend WebUI server construction to receive the shared runtime service and register remote, local, exact-removal, operation-creation, and SSE event routes before static fallback.
- [x] 2.2 Implement an in-memory single-active-download registry with safe operation IDs, throttled latest progress snapshots, terminal retention, and restart-safe failure semantics.
- [x] 2.3 Implement schema-versioned JSON success documents and safe JSON error handling for discovery, local listing, malformed requests, unsupported methods, busy mutation/operation creation, and runtime-operation failures.
- [x] 2.4 Implement the exact-asset operation-creation handler and the SSE event handler with the shared service only; do not accept URLs or selectors.
- [x] 2.5 Implement the exact-asset removal handler with the shared service only and return a schema-versioned mutation result.
- [x] 2.6 Add HTTP handler tests for remote/local/removal responses, operation lifecycle and event stream, repeat download, error paths, methods, reconnection snapshot, and protection from Angular fallback.
- [x] 2.7 Retain terminal operation snapshots for ten minutes, prune expired records on subsequent operation creation, and test the retention boundary.
- [x] 2.8 Log underlying runtime API failures to standard error, map an uninitialized installation to actionable safe guidance, and surface safe backend errors in the Angular workspace.
- [x] 2.9 Retry transient staged-runtime probe failures within one bounded budget, preserve bounded command output for diagnostics, and add focused regression tests.

## 3. Runtimes workspace

- [x] 3.1 Create a typed Angular llama.cpp runtime API service that loads remote and local records, creates exact-asset download operations through `HttpClient`, and wraps `EventSource` as an RxJS Observable.
- [x] 3.2 Implement signal-based Runtimes state for loading, errors, discovered rows, local installations, and a single in-flight download operation with phase and byte progress.
- [x] 3.3 Replace mock installed/latest runtime content with API-backed local inventory and compatible-distribution discovery, including dependencies, exact asset IDs, and a confirmed Remove action for local rows.
- [x] 3.4 Add phase/progress and removal interaction feedback, prevent concurrent UI mutations, and refresh both inventories after successful mutation completion.
- [x] 3.5 Add Angular tests covering initial loads, exact-ID operation creation, EventSource progress/terminal/error states, confirmed removal, busy behavior, and post-mutation refresh.

## 4. Verification

- [x] 4.1 Run Go formatting and backend tests, including embedded WebUI build prerequisites.
- [x] 4.2 Run frontend format check, lint, tests, and production build.
- [x] 4.3 Run `openspec validate webui-llamacpp-runtime-downloads --strict` and resolve all validation findings.
