## ADDED Requirements

### Requirement: Route runtime-management APIs independently of static content
The local WebUI server SHALL route `/api/runtimes/llama.cpp`,
`/api/runtimes/llama.cpp/remote`, `/api/runtimes/llama.cpp/{assetId}`,
`/api/runtimes/llama.cpp/downloads`, and
`/api/runtimes/llama.cpp/downloads/{operationId}/events` before static-file
and Angular client-route fallback. It SHALL return JSON API responses for
supported and failed runtime-management requests, `text/event-stream` for a
valid operation event stream, and SHALL reject unsupported HTTP methods with
status `405`.

#### Scenario: Runtime API does not fall back to the application shell
- **WHEN** a runtime-management API request fails or uses an unsupported method
- **THEN** the server returns its API error or `405` response
- **AND** it does not return `index.html`
