## 1. Portable configuration and shared behavior

- [x] 1.1 Evolve installation configuration parsing/publication to create schema version 2, retain schema-version-1 compatibility, and include the portable `models/` directory in initialization.
- [x] 1.2 Add an interface-neutral configuration application service that resolves the effective directory, validates existing absolute writable overrides with a temporary-file write probe, and atomically persists a valid override.
- [x] 1.3 Add focused installation/configuration tests for default resolution, version-1 compatibility, atomic version-2 writes, and every invalid directory class.

## 2. CLI configuration adapter

- [x] 2.1 Add `coquena config models-directory [<absolute-path>] [--json]` parsing, usage text, text/JSON output, and initialized-installation diagnostics.
- [x] 2.2 Connect the command to the shared configuration service without duplicating validation.
- [ ] 2.3 Add CLI unit and executable integration tests for read, update, JSON, invalid input, and pre-init behavior.

## 3. Local WebUI configuration API

- [x] 3.1 Add `GET` and `PUT /api/config/models-directory` routing, strict request decoding, schema-version-1 response documents, and safe JSON error mapping.
- [x] 3.2 Connect the HTTP adapter to the shared configuration service and preserve the Angular fallback boundary for non-API routes only.
- [x] 3.3 Add HTTP contract tests for reads, writes, methods, malformed/unknown JSON fields, invalid paths, and uninitialized installations.

## 4. API-backed Settings workspace

- [x] 4.1 Add an Angular configuration service using `HttpClient`, RxJS async boundaries, and signal-based state for loading, saving, and error handling.
- [x] 4.2 Replace the models-directory mock with manual path editing, save feedback, and retained last-known-good value after API failure.
- [x] 4.3 Remove the browser directory-picker affordance because it cannot provide the native path required to save the setting.
- [ ] 4.4 Add Angular tests for API rendering/save/error states and manual-entry behavior.

## 5. Verification

- [x] 5.1 Run Go formatting and the complete Go test suite.
- [x] 5.2 Run frontend formatting, linting, tests, and production build; refresh embedded WebUI assets if the repository workflow requires it.
- [x] 5.3 Validate the OpenSpec Change and manually verify `coquena init`, CLI read/update, and WebUI Settings against a real existing writable directory.
