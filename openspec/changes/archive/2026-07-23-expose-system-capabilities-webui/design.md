## Context

`internal/system.Inspect` already builds the versioned `system.Report` used by `coquena system --json`. The embedded Web UI server only provides `/api/health`, while the Angular Dashboard and Runtimes screens are a mock control plane and hard-code a Windows/CUDA/RTX hardware line. The browser must remain an API client: it must not run operating-system probes itself.

The first vertical slice is local and read-only. It needs to make the existing report available to the UI, render it consistently in both requested workspaces, and avoid repeatedly running native hardware probes during normal in-session navigation. Hardware can change after reboot, so browser persistence must not survive a browser session.

## Goals / Non-Goals

**Goals:**

- Serve the existing normalized report from the Web UI process without duplicating CLI detection logic or changing the CLI JSON schema.
- Provide one reusable Angular presentation for a compact, useful view of host OS, CPU, memory, graphics devices, and backend availability.
- Cache only successful responses in `sessionStorage` for a 15-minute TTL and share the in-flight/result state between Dashboard and Runtimes.
- Make loading, expired-cache, malformed-cache, unavailable-API, and unknown optional hardware data safe and understandable in the UI.

**Non-Goals:**

- Re-probing on a timer, manual refresh controls, cross-tab synchronization, or persistence across browser sessions.
- Selecting, installing, activating, or recommending a llama.cpp runtime from the report.
- Changing system inspection probes, report schema version, CLI rendering, listener binding, or adding authentication/remote API access.
- Replacing the remaining mock runtime, model, or performance information with live control-plane data.

## Decisions

### Reuse `system.Report` behind a narrow HTTP handler dependency

`internal/webui` will add `GET /api/system` to its existing handler. The endpoint invokes an injected inspection function (production wiring uses `system.Inspect`), validates/serializes through the existing report JSON path, and returns the same schema-versioned document as `coquena system --json` with `application/json; charset=utf-8`. Inspection or encoding failure produces a non-success JSON HTTP response without serving the Angular fallback. The existing `Handler()` can retain a production convenience wrapper while a dependency-accepting constructor supports deterministic handler tests.

This retains a single detection implementation and keeps HTTP transport separate from native probes. Calling a CLI subprocess was rejected because it adds process startup, output parsing, and error-channel duplication. Moving detection into the browser was rejected because it would require privileged platform APIs and violate the API-client boundary.

### Cache successful report envelopes in session storage for 15 minutes

The Angular service will use `HttpClient` and RxJS, expose signal-based state for templates, and store a versioned cache envelope containing the report and `fetchedAt` timestamp under a namespaced session-storage key. It accepts only a structurally valid report whose `schemaVersion` is supported and whose timestamp is within 15 minutes; invalid JSON, wrong schema, or expired entries are removed before fetching. A successful response replaces the envelope. Failed requests are not cached and are represented as an error state so a later navigation/retry can request fresh data.

`sessionStorage` satisfies the requirement to discard data when the browser session ends, unlike `localStorage`, while TTL limits stale data within a long-lived session. A server-side cache was rejected for this slice because it would introduce lifecycle and invalidation policy beyond the requested browser-navigation optimization. HTTP cache headers alone were rejected because their behavior varies with browsers and cannot conveniently validate schema/timestamped report envelopes.

### Present one standalone hardware widget in both workspaces

The widget receives the shared service state and renders loading, error, and ready views. In its ready view it presents the detected operating system/architectures, CPU and logical processors, known total memory, graphics devices (or the report's no-device state), and backend state chips. It preserves unknown optional values as unknown rather than inferring a value; diagnostics are available in a compact details section. Dashboard and Runtimes each compose this same widget rather than maintaining separate report transformations.

This isolates formatting and accessibility from the route component and makes future placement elsewhere cheap without building a generic dashboard framework. Two page-specific summaries were rejected because they would inevitably drift and fail to demonstrate that both views share one report/cache behavior.

## Risks / Trade-offs

- [Native inspection can be slow or fail on a machine] → Cache only successful reports and render non-fatal loading/error states; preserve the backend's diagnostic semantics.
- [A system changes while a browser remains open] → A short 15-minute TTL bounds staleness and session-scoped storage is discarded on browser close; periodic refresh is deferred.
- [A future report schema is incompatible] → The client rejects unsupported cached/API schema versions and reports an unavailable state instead of rendering guessed fields.
- [A malformed API response could poison the cache] → Validate before persisting and remove malformed stored values.
- [Widget density may be high on narrow screens] → Use the existing responsive card/grid styling and test the essential compact content.

## Migration Plan

1. Add and test the dependency-injected HTTP endpoint while retaining health/static-route behavior.
2. Add typed Angular client/cache service and widget tests with deterministic HTTP/storage fixtures.
3. Compose the widget into Dashboard and Runtimes, replacing only the Runtimes hard-coded hardware line.
4. Build and run the existing Go/Angular verification tasks.

Rollback removes the endpoint, client service, and widget; no persisted server state or migration is involved. Existing cache entries become harmless orphaned session data and expire with the session.

## Open Questions

None blocking this slice. A user-initiated refresh and runtime compatibility/recommendation policy can be proposed once live runtime management exists.
