## ADDED Requirements

### Requirement: Route model-management APIs independently of static content
The local WebUI server SHALL route all GGUF model resolution, inspection, library, installed-metadata, publisher-avatar, download-job, job-control, and model-download SSE API paths before embedded static-file and Angular client-route fallback handling. It SHALL return JSON API responses, bounded avatar image content, or `text/event-stream` for a valid stream and SHALL reject unsupported methods with `405`.

#### Scenario: Model API failure does not fall back to the application shell
- **WHEN** a model-management API request fails or uses an unsupported method
- **THEN** the server returns its API error or `405`
- **AND** it does not return `index.html`
