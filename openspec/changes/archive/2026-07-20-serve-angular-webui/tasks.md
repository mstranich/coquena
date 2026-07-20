## 1. Embed the Angular application

- [x] 1.1 Choose and configure the Angular production output directory consumed by the Go embed package; preserve ignored generated output and verify `index.html` exists.
- [x] 1.2 Add the `internal/webui` embedded asset package with a startup validation for the required application document.

## 2. Implement HTTP serving

- [x] 2.1 Implement the `GET /api/health` endpoint with `ok` response and method rejection.
- [x] 2.2 Implement an SPA-aware `net/http` file handler for embedded assets, route fallback, content types, and safe URL paths.
- [x] 2.3 Add unit tests for health, root, assets, client-side routes, missing assets, and traversal attempts.
- [x] 2.4 Add listener lifecycle, fixed loopback binding, port validation, injectable shutdown behavior, and tests.

## 3. Add the CLI command

- [x] 3.1 Add `webui` dispatch and `--port` parsing with usage and startup diagnostics; reject unsupported `--host`.
- [x] 3.2 Add effective-URL output and OS interrupt handling with clean shutdown.
- [x] 3.3 Add command tests for successful serving, invalid port/flags, interrupt shutdown, and occupied-port failures.

## 4. Integrate and document

- [x] 4.1 Make the Go build depend on the Angular production build and update verification tasks without duplicating generated output.
- [x] 4.2 Document `coquena webui`, startup URL, `Ctrl+C`, port override, loopback restriction, no-CORS local behavior, and development alternative in `README.md`.
- [x] 4.3 Run frontend tests/build and Go formatting/tests/build; review the resulting diff.
