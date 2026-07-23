## ADDED Requirements

### Requirement: Fetch system capabilities through the local API
The Angular Web UI SHALL obtain host capability data from `GET /api/system` and SHALL use the API report rather than browser-side hardware probing or hard-coded host data.

#### Scenario: First in-session hardware view
- **WHEN** a user opens Dashboard or Runtimes without a valid cached report
- **THEN** the Web UI requests `GET /api/system`
- **AND** renders the returned normalized report when the request succeeds

### Requirement: Cache successful reports per browser session with a bounded TTL
The Web UI SHALL store only successful, supported-schema system reports in `sessionStorage` with their fetch time and SHALL reuse a cached report while it is no more than 15 minutes old. It SHALL not use `localStorage` for system capabilities.

#### Scenario: Reuse a fresh report across workspaces
- **WHEN** Dashboard has obtained a successful report and the user opens Runtimes within 15 minutes in the same browser session
- **THEN** Runtimes renders the cached report
- **AND** does not initiate an unnecessary second system API request

#### Scenario: Refresh an expired report
- **WHEN** a cached report is older than 15 minutes
- **THEN** the Web UI removes or replaces the expired cache entry
- **AND** requests a fresh report before displaying ready capability data

#### Scenario: Discard malformed or unsupported cached data
- **WHEN** the session-storage entry cannot be parsed, lacks its fetch time, or contains an unsupported report schema
- **THEN** the Web UI discards that entry
- **AND** requests a fresh report instead of rendering guessed hardware data

### Requirement: Render reusable host capability information
The Web UI SHALL provide one reusable hardware-capability widget and SHALL compose it in both Dashboard and Runtimes. In its ready state the widget SHALL show operating-system and architecture information, CPU/logical processor information, known memory when available, graphics devices or their absence, and backend capability states; it SHALL preserve unknown optional values as unknown.

#### Scenario: Inspect hardware from Dashboard
- **WHEN** a user opens Dashboard after report retrieval succeeds
- **THEN** the reusable widget displays the detected host capability summary

#### Scenario: Inspect hardware from Runtimes
- **WHEN** a user opens Runtimes after report retrieval succeeds
- **THEN** the same reusable widget displays the detected host capability summary
- **AND** the prior hard-coded Windows/CUDA/GPU summary is not presented as detected data

### Requirement: Make retrieval failure understandable and retryable
The widget SHALL distinguish loading from an unavailable report. If the API request fails or returns invalid data, it SHALL not cache that result and SHALL show a non-fatal error state; a subsequent widget initialization or explicit retry action SHALL be able to request the API again.

#### Scenario: Hardware API is unavailable
- **WHEN** `GET /api/system` fails or returns an invalid report
- **THEN** the widget shows that hardware information is unavailable
- **AND** the Dashboard and Runtimes remain usable
