## 1. Local system-capability API

- [x] 1.1 Add an injectable system-inspection dependency to the Web UI HTTP handler while preserving its production `system.Inspect` wiring.
- [x] 1.2 Implement `GET /api/system` with the existing normalized JSON report, JSON content type, read-only behavior, and correct method/error responses.
- [x] 1.3 Extend Go handler tests for successful reports, optional backend absence, inspection failure, non-GET rejection, and protection from Angular fallback.

## 2. Angular capability client and session cache

- [x] 2.1 Add typed Angular representations for the supported normalized system report and configure the HTTP client.
- [x] 2.2 Implement the signal-backed RxJS capability service with a versioned `sessionStorage` envelope, 15-minute TTL, malformed/unsupported-cache eviction, in-flight sharing, and uncached error/retry behavior.
- [x] 2.3 Add focused service tests for first fetch, fresh cache reuse across consumers, expiry refresh, malformed/schema-invalid cache, and failed request recovery.

## 3. Reusable operational hardware view

- [x] 3.1 Build and test a reusable hardware-capability widget with accessible loading, unavailable, and ready states for OS/architecture, CPU, known memory, graphics, backend states, and diagnostics/unknown values.
- [x] 3.2 Compose the widget into Dashboard and Runtimes, removing the Runtimes hard-coded Windows/CUDA/GPU line while leaving unrelated mock control-plane data intact.
- [x] 3.3 Add component/integration tests proving both workspaces render the shared API-derived view and do not duplicate a fresh API request within the cache TTL.

## 4. Verification

- [x] 4.1 Run the relevant Go tests and Angular tests/build.
- [x] 4.2 Run `openspec validate expose-system-capabilities-webui --strict` and resolve all validation findings.
