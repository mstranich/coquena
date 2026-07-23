## ADDED Requirements

### Requirement: Route the system capability API independently of static content
The local Web UI server SHALL route `/api/system` as an API request before Angular static-file and client-route fallback handling.

#### Scenario: System API does not fall back to the application shell
- **WHEN** processing a request to `/api/system` whose inspection cannot succeed
- **THEN** the server returns the system API failure response
- **AND** it does not return `index.html`
