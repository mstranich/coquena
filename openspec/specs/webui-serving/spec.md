## Purpose

Define the local HTTP server that exposes Coquena's embedded Angular Web UI and its initial health endpoint.
## Requirements
### Requirement: Expose a health endpoint

The server SHALL expose `GET /api/health` independently of the embedded Angular asset tree.

#### Scenario: Health check succeeds
- **WHEN** a client sends `GET /api/health`
- **THEN** the server responds with status `200`, content type `text/plain`, and a body equal to `ok` followed by a newline

#### Scenario: Health endpoint rejects other methods
- **WHEN** a client sends a method other than `GET` to `/api/health`
- **THEN** the server responds with status `405` and does not serve the Angular application document

### Requirement: Start the local Web UI

The product SHALL provide `coquena webui` that starts an HTTP server serving the production Angular application embedded in the executable.

#### Scenario: Serve the application root
- **WHEN** a user runs `coquena webui` and requests `GET /`
- **THEN** the server responds with status `200`, an HTML content type, and the embedded Angular `index.html`

#### Scenario: Serve a static asset
- **WHEN** a client requests an existing Angular asset such as `/styles.css` or a hashed JavaScript file
- **THEN** the server returns status `200` and the exact embedded asset bytes

#### Scenario: Serve assets with browser-compatible MIME types
- **WHEN** a browser requests an Angular JavaScript module
- **THEN** the server returns a JavaScript MIME type instead of `text/plain`

### Requirement: Support Angular client-side routes

The server SHALL fall back to `index.html` for a route-like request that has no matching embedded file and does not identify an asset by file extension.

#### Scenario: Refresh an application route
- **WHEN** a client requests `/settings` directly
- **THEN** the server returns status `200` with the Angular application document

#### Scenario: Reject an unknown asset
- **WHEN** a client requests `/missing.js`
- **THEN** the server responds with status `404` and SHALL NOT return `index.html`

### Requirement: Restrict the initial listener to localhost

The command SHALL bind only to `127.0.0.1`, use port `8080` by default, and support an optional `--port` flag. The command SHALL NOT expose a host-binding flag in this change.

#### Scenario: Override the port
- **WHEN** a user runs `coquena webui --port 9090`
- **THEN** the server listens on `127.0.0.1:9090`

#### Scenario: Reject unsupported host configuration
- **WHEN** a user supplies `--host`
- **THEN** the command exits unsuccessfully with a usage diagnostic and does not start a listener

### Requirement: Report the effective URL

After the listener is successfully created, the command SHALL print the effective `http://127.0.0.1:<port>` URL to standard output.

#### Scenario: Startup reports the URL
- **WHEN** the server starts successfully on port `9090`
- **THEN** standard output contains `http://127.0.0.1:9090`

### Requirement: Shut down cleanly

The command SHALL handle an interrupt signal by stopping the HTTP server and SHALL return success for that normal shutdown.

#### Scenario: User interrupts the server
- **WHEN** the running server receives `Ctrl+C`
- **THEN** it stops accepting requests, releases its listener, and exits with status `0`

### Requirement: Report startup failures

The command SHALL report listener and embedded-asset initialization failures on standard error and return a non-zero exit status.

#### Scenario: Port is unavailable
- **WHEN** the requested address is already occupied
- **THEN** `coquena webui` exits unsuccessfully and reports the bind failure

### Requirement: Build the embedded UI as part of the product build

The repository SHALL make the Angular production build a prerequisite of the Go executable build and SHALL provide tests that prove the resulting executable serves the embedded application.

#### Scenario: Clean product build
- **WHEN** a developer runs the complete backend build task
- **THEN** Angular production assets are generated first and the Go executable embeds and serves them

### Requirement: Route the system capability API independently of static content
The local Web UI server SHALL route `/api/system` as an API request before Angular static-file and client-route fallback handling.

#### Scenario: System API does not fall back to the application shell
- **WHEN** processing a request to `/api/system` whose inspection cannot succeed
- **THEN** the server returns the system API failure response
- **AND** it does not return `index.html`

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
