## ADDED Requirements

### Requirement: Expose the normalized system report through the local API
The embedded Web UI server SHALL expose `GET /api/system` and SHALL obtain its response from the same normalized system-inspection result used by `coquena system --json`. A successful response SHALL be JSON with `Content-Type: application/json; charset=utf-8` and SHALL preserve the report's integer `schemaVersion` and normalized capability, graphics, and diagnostic data.

#### Scenario: Retrieve a detected system report
- **WHEN** a client sends `GET /api/system` and inspection succeeds
- **THEN** the server responds with status `200` and one valid normalized system-report JSON document
- **AND** the document is not a separately shaped or hard-coded Web UI summary

#### Scenario: Report optional capability absence
- **WHEN** the inspected host has no usable optional acceleration backend
- **THEN** `GET /api/system` responds successfully with the report's unavailable, unsupported, or unknown capability state
- **AND** does not treat the absence as an HTTP failure

### Requirement: Keep the system endpoint read-only and method-specific
The system endpoint SHALL perform no installation, runtime-management, or host-state mutation and SHALL reject methods other than `GET` with status `405` without serving the Angular application document.

#### Scenario: Reject a non-GET system request
- **WHEN** a client sends `POST /api/system`
- **THEN** the server responds with status `405`
- **AND** it does not invoke a state-changing operation or return the Angular index document

### Requirement: Report inspection failures as API failures
When the server cannot obtain or encode a valid normalized system report, it SHALL return a non-success HTTP response and SHALL NOT return stale, invented, partial, or Angular fallback content as the report.

#### Scenario: Inspection fails
- **WHEN** system inspection returns an error for `GET /api/system`
- **THEN** the server responds with status `500`
- **AND** the response identifies an API error without exposing a fabricated capability report
