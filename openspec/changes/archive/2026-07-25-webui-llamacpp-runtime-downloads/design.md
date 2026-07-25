## Context

The CLI has a complete llama.cpp artifact workflow: it obtains official GitHub
release metadata, classifies distributions, filters to the current host,
resolves a selector, validates every contributing asset, and atomically
publishes a runnable installation. That orchestration is currently in
`cmd/coquena`, while `internal/webui` can only obtain system inspection data.
The Angular Runtimes workspace consequently presents a mock installed/latest
runtime view.

The WebUI is a local API client served on loopback. It must request operations
from the Go process and must not select arbitrary URLs or reproduce runtime
compatibility rules in browser code.

## Goals / Non-Goals

**Goals:**

- Reuse one Go application service for CLI and HTTP runtime operations.
- Let the Runtimes view discover the latest compatible official llama.cpp
  distributions, inspect local installations, request one exact asset, and
  remove one exact local installation.
- Preserve the CLI's validation, compatibility, locking, atomic publication,
  and idempotency guarantees for API requests.
- Provide typed Angular state for loading, errors, discovered rows, local rows,
  and an in-flight download operation with real progress.

**Non-Goals:**

- Release-history browsing, backend/release filters, arbitrary selector input,
  manual URLs, or non-llama.cpp runtimes.
- Download cancellation, persistent operation history, activation, rollback,
  or runtime execution.
- Changing the portable installation layout, upstream release taxonomy, or
  compatibility policy.

## Decisions

### Extract a shared runtime application boundary

Move the runtime-manager implementation and its public record/request/mutation
types from `cmd/coquena` into an internal package. The CLI keeps parsing its
own command grammar and delegates to that service; the WebUI receives the same
service through an interface supplied at server construction.

This prevents security-sensitive behavior from diverging. Copying only the
download path into `internal/webui` was rejected because it would duplicate
the release and store policy. Moving HTTP concerns into the CLI was rejected
because `cmd` is composition code, not a reusable server dependency.

### Use a small resource-oriented local API

The server will add these routes before static-file fallback:

- `GET /api/runtimes/llama.cpp/remote`: latest compatible official
  distributions.
- `GET /api/runtimes/llama.cpp`: valid managed local installations with their
  current compatibility.
- `DELETE /api/runtimes/llama.cpp/{assetId}`: remove one exact valid managed
  local installation.
- `POST /api/runtimes/llama.cpp/downloads`: an object containing one exact
  `assetId`; response creates an asynchronous download operation.
- `GET /api/runtimes/llama.cpp/downloads/{operationId}/events`: Server-Sent
  Events for that operation's latest state and subsequent progress.

All successful JSON documents use schema version 1 and the existing normalized
runtime record shape. The download route accepts no URL, selector, release, or
backend value. It resolves the supplied exact ID again from authoritative
metadata and current host facts, so a stale or forged browser response cannot
bypass policy.

The server owns an in-memory, single-active-operation registry. It reserves an
operation before starting work, returns `202 Accepted` with its opaque ID, and
rejects a competing creation request rather than relying on a later lock
failure. An operation exposes phases `queued`, `downloading`, `verifying`,
`extracting`, `probing`, `publishing`, `completed`, and `failed`. Download
events include the current asset, per-asset transferred/total bytes when known,
and aggregate transferred/total bytes across all required assets. The store
reports progress through a callback that is throttled by the server (at most
four updates per second) so archive-sized downloads do not flood the browser.

The staged `llama-server --version` probe uses one ten-second total budget and
may retry up to three times within that budget. This tolerates a transient
post-extraction Windows loader or security-scanner race without downloading
the verified archives again. A final probe failure includes bounded captured
stdout/stderr in the server diagnostic log, while the browser still receives a
safe terminal failure.

The event endpoint sends the latest snapshot immediately, continues with
`text/event-stream`, and sends one terminal completed or failed event. It is
safe for an EventSource reconnect while the operation remains in memory;
completed/failed snapshots are retained for ten minutes only for reconnect and
UI completion, and the browser closes its EventSource after receiving a
terminal snapshot so native automatic reconnection does not loop. Expired
records are pruned when the next operation is created. Restarting Coquena loses
these ephemeral operation records, while
the atomic runtime store remains authoritative.

Removal is synchronous because it only isolates and deletes an existing local
installation; it reuses the shared service's exact-ID validation and
installation-scoped mutation lock. It returns a schema-versioned mutation
result on success. The browser must identify the installation through its exact
asset ID and ask for a deliberate destructive confirmation before issuing the
request. A busy download or removal returns an actionable non-success response
and changes nothing.

### Make Runtimes data-driven and narrowly interactive

An injectable Angular service obtains remote and local records through
`HttpClient`. It creates a download operation through `HttpClient`, wraps the
browser `EventSource` stream in an RxJS Observable, and converts that stream to
signal-based component state. Entering Runtimes loads both lists. The workspace
renders available distributions with release, target/backend, dependencies,
exact ID, and a Download button, and renders local installations from API data
rather than mock values. It disables additional download actions while one
operation is active, displays phase and progress, presents errors, and
refreshes both lists after a terminal completed result.

Each local row additionally exposes a Remove action. After explicit
confirmation, it sends that row's exact ID through the deletion API, disables
conflicting mutation actions, renders any failure, and refreshes local and
remote inventories after success.

The browser uses only the exact ID emitted by remote discovery. A free-form
selector form was rejected because it introduces ambiguity without a needed
user workflow.

### Preserve immutable installations and defer runtime lifecycle state

Downloading a new asset publishes a distinct immutable installation; it never
replaces an installation that may be used by a future llama-server process.
This Change does not add `stale` files, runtime selection, active-version
reporting, or a Restart Service action. A future lifecycle capability will
persist desired runtime selection in a versioned configuration document, while
the service supervisor reports the actually running asset. It will derive
restart-required state from desired versus observed identity, rather than
persisting a `stale` boolean that can become false after a crash or external
change. That future capability must also reject removal of the desired or
observed running runtime; the present Change mirrors the CLI because no runtime
supervisor or active-runtime state exists yet.

### Preserve API error boundaries

The server returns JSON API errors and never returns the Angular shell for an
API route. Unsupported methods return `405`; malformed request JSON or a
missing/invalid asset ID returns a client error; unavailable/busy/conflicting
runtime operations and upstream discovery failures return non-success
responses with a safe actionable message. Internal errors and upstream details
are not exposed verbatim. The server writes the underlying runtime error to
its standard-error log, including the failed operation context, while the API
returns only a safe message. A missing portable installation is a known
operator condition and returns actionable initialization guidance.

## Risks / Trade-offs

- [In-memory operations disappear when Coquena restarts] → Keep the immutable
  store authoritative, expose safe terminal failure to reconnecting clients,
  and defer persistent job history to a dedicated change.
- [Progress events can be excessively frequent] → Throttle updates and emit
  only the latest snapshot required to render useful UI feedback.
- [The latest release may change between discovery and download] → Re-resolve
  the exact asset against official metadata at download time and report failure
  rather than downloading a different asset.
- [API and CLI refactor could alter established behavior] → Preserve and run
  existing CLI/service tests, then add API-specific contract tests.
- [A running WebUI build may use stale embedded assets during development] →
  This change does not alter the development workflow; production embedding
  remains the tested product path.
- [A freshly extracted Windows runtime can fail its first process launch] →
  Retry the non-server probe within one bounded budget and retain bounded
  command output in server diagnostics if all attempts fail.
