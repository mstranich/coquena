# webui-llamacpp-runtime-management Specification

## Purpose
TBD - created by archiving change webui-llamacpp-runtime-downloads. Update Purpose after archive.
## Requirements
### Requirement: Discover latest compatible llama.cpp distributions through the WebUI API
The local WebUI server SHALL expose `GET /api/runtimes/llama.cpp/remote` and
return only host-compatible official llama.cpp primary distributions from the
latest eligible release, using the same normalized runtime records and
compatibility policy as `coquena runtime remote`. A successful response SHALL
be a JSON schema-version-1 document and SHALL include each distribution's
release, target, backend, exact primary asset ID, and required dependencies.

#### Scenario: Discover compatible latest distributions
- **WHEN** a browser requests `GET /api/runtimes/llama.cpp/remote` on a host
  with compatible official CPU and GPU distributions
- **THEN** it receives status `200` and a schema-version-1 JSON document
- **AND** its result contains one normalized row for every compatible primary
  distribution and no dependency-only or incompatible asset

#### Scenario: Remote catalog cannot be obtained
- **WHEN** official release metadata or host inspection cannot be obtained
- **THEN** the server returns a non-success JSON API response
- **AND** it does not return an Angular application document or a partial
  invented catalog

### Requirement: Inspect managed llama.cpp installations through the WebUI API
The local WebUI server SHALL expose `GET /api/runtimes/llama.cpp` and return
every valid managed llama.cpp installation with its recalculated compatibility
using the same records and local-store validation as `coquena runtime list`.

#### Scenario: Inspect locally installed runtime
- **WHEN** a browser requests `GET /api/runtimes/llama.cpp` after an official
  runtime has been installed
- **THEN** it receives status `200` and a schema-version-1 JSON document
- **AND** the matching exact primary asset ID and current compatibility appear
  in the result

#### Scenario: Local runtime store is invalid
- **WHEN** the local runtime store cannot be validated
- **THEN** the server returns a non-success JSON API response
- **AND** it does not represent malformed content as a valid installation

#### Scenario: Local portable installation is not initialized
- **WHEN** the WebUI requests the local runtime inventory before the portable
  installation has been initialized
- **THEN** the API returns a safe non-success response instructing the operator
  to run `coquena init`
- **AND** the server logs the underlying diagnostic to standard error without
  exposing filesystem details in the browser response

### Requirement: Remove an exact managed llama.cpp installation through the WebUI API
The local WebUI server SHALL expose `DELETE
/api/runtimes/llama.cpp/{assetId}` and remove only the valid managed local
runtime identified by that exact primary asset ID. It SHALL use the same ID
validation, exact local-store lookup, isolation, deletion, and
installation-scoped mutation-lock behavior as `coquena runtime remove`. A
successful response SHALL be a schema-version-1 JSON mutation result.

#### Scenario: Remove a selected local runtime
- **WHEN** a browser sends `DELETE` for the exact primary asset ID of a valid
  local installation
- **THEN** the server removes only that installation
- **AND** returns a successful mutation result identifying that asset ID

#### Scenario: Reject unsafe or missing removal target
- **WHEN** a browser supplies an unsafe, malformed, partial, or absent asset ID
- **THEN** the server returns a non-success JSON API response
- **AND** it removes no runtime content

#### Scenario: Reject removal while another mutation is active
- **WHEN** a browser requests removal while a runtime download or removal owns
  the mutation lock
- **THEN** the server returns a non-success JSON API response
- **AND** it does not modify any local installation

### Requirement: Download an exact compatible llama.cpp asset through the WebUI API
The local WebUI server SHALL expose `POST /api/runtimes/llama.cpp/downloads`
that accepts JSON containing exactly one official primary `assetId`. It SHALL
create one asynchronous download operation and return status `202` with a
schema-version-1 JSON document containing an opaque operation ID. Before
mutating local state, the operation SHALL validate and resolve that ID against
official release metadata and current-host compatibility, then use the same
dependency, digest, archive, probe, atomic-publication, idempotency, and
mutation-lock rules as `coquena runtime download`.

#### Scenario: Download a discovered distribution
- **WHEN** a browser posts the exact primary asset ID returned by remote
  discovery
- **THEN** the server returns `202` with an operation ID and starts exactly one
  validated download operation
- **AND** the operation downloads and publishes the complete runtime only after
  all existing runtime-download checks pass

#### Scenario: Repeat an installed exact asset
- **WHEN** a browser posts an exact primary asset ID whose valid installation
  is already present
- **THEN** the server returns `202` with an operation ID
- **AND** its terminal result reports `changed` false and leaves the existing
  installation unchanged

#### Scenario: Reject invalid download input
- **WHEN** a browser posts malformed JSON, omits `assetId`, or supplies a
  selector, URL, dependency asset, unsafe name, or non-official primary ID
- **THEN** the server returns a client-error JSON API response
- **AND** it performs no download or filesystem mutation

#### Scenario: Reject concurrent download creation safely
- **WHEN** a browser requests a download while another download operation is
  active
- **THEN** the server returns a non-success JSON API response
- **AND** it starts no second download operation or filesystem mutation

#### Scenario: Download operation fails safely
- **WHEN** a started operation fails validation, retrieval, integrity,
  extraction, or probing
- **THEN** its event stream terminates with a failed event and a safe message
- **AND** it preserves existing installations and publishes no partial runtime

#### Scenario: Staged runtime probe fails transiently
- **WHEN** the first launch of a fully verified and extracted `llama-server`
  fails during the bounded probe window
- **THEN** the service retries the probe within the same total time budget and
  without downloading the assets again
- **AND** if every attempt fails, the server log includes bounded probe output
  while the event stream exposes only a safe terminal error

### Requirement: Stream download-operation progress through Server-Sent Events
The local WebUI server SHALL expose `GET
/api/runtimes/llama.cpp/downloads/{operationId}/events` as a
`text/event-stream` response for a known download operation. It SHALL send the
latest operation snapshot immediately and stream phase and progress updates
until one terminal `completed` or `failed` event. Downloading updates SHALL
identify the current required asset and include transferred and total bytes for
that asset and the aggregate operation when their official sizes are known.
The server SHALL throttle progress emissions so that it emits no more than four
updates per second for one operation.

#### Scenario: Receive download byte progress
- **WHEN** a browser subscribes to the event stream of an operation that is
  downloading a primary asset or dependency
- **THEN** it receives the current asset identity and its byte progress
- **AND** it receives aggregate operation byte progress when official sizes are
  known

#### Scenario: Receive terminal operation outcome
- **WHEN** a download operation completes or fails
- **THEN** the event stream sends one terminal event with the mutation result or
  safe failure message
- **AND** it does not report an installation as completed before publication
  succeeds

#### Scenario: Reconnect to an existing operation stream
- **WHEN** a browser reconnects to a known operation while its in-memory record
  is retained
- **THEN** the server sends its latest snapshot immediately before later events

#### Scenario: Expire terminal operation state
- **WHEN** a completed or failed operation has remained terminal for more than
  ten minutes and a later operation is created
- **THEN** the server prunes the expired operation record from memory
- **AND** a later event request for its ID receives a non-success API response

#### Scenario: Unknown or unsupported event request
- **WHEN** a browser requests an unknown operation event stream or uses a
  method other than `GET`
- **THEN** the server returns a non-success API response or `405`
- **AND** it does not return an Angular application document

### Requirement: Present API-backed llama.cpp runtime management in Runtimes
The Angular Runtimes workspace SHALL load local installations and latest
compatible remote llama.cpp distributions from the local WebUI API. It SHALL
render the remote distribution release, target/backend, dependencies, exact
asset ID, and a download action, and SHALL render local installation data from
the API rather than mock installed/latest values. Every local installation row
SHALL offer a Remove action that requires explicit operator confirmation before
it sends the exact asset ID to the removal API.

#### Scenario: Inspect runtime discovery from the WebUI
- **WHEN** an operator opens Runtimes and both API requests succeed
- **THEN** the workspace shows the available compatible official distributions
  and the local managed installations returned by the API

#### Scenario: Request a runtime download from the WebUI
- **WHEN** an operator selects Download for a discovered exact distribution
- **THEN** the workspace sends only that row's exact asset ID to create a
  download operation and subscribes to that operation's event stream
- **AND** disables additional download actions while the request is in flight
- **AND** displays the current phase and available byte progress
- **AND** closes the EventSource after a terminal event rather than reconnecting
  to the completed operation
- **AND** refreshes remote and local data after a completed terminal event

#### Scenario: Remove a local runtime from the WebUI
- **WHEN** an operator confirms removal of one local runtime row
- **THEN** the workspace sends only that row's exact asset ID to the removal
  API
- **AND** disables conflicting runtime mutation actions until the response
  completes
- **AND** refreshes the runtime inventories after successful removal

#### Scenario: Decline local runtime removal
- **WHEN** an operator dismisses the removal confirmation
- **THEN** the workspace sends no removal request
- **AND** the local runtime inventory remains unchanged

#### Scenario: Runtime API request fails in the WebUI
- **WHEN** discovery, local listing, or download returns an error
- **THEN** the workspace presents a visible error state
- **AND** it remains usable and does not claim that the runtime was installed
