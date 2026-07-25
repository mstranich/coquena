## Context

Coquena initializes portable state beneath its resolved application home and currently accepts only `{ "schemaVersion": 1 }` in `config/coquena.json`. The Angular Settings page contains a hard-coded Models directory mock, while the WebUI already exposes local JSON APIs and the CLI/WebUI runtime feature uses one interface-neutral application service.

This Change makes only the models artifact directory configurable. The setting must be usable by future model-management work without adding that work now.

## Goals / Non-Goals

**Goals:**

- Provide one durable effective models directory through CLI and WebUI.
- Default it to `<application-home>/models` without requiring an explicit setting.
- Validate every persisted override before it can affect later model I/O.
- Preserve existing initialized installations and keep browser filesystem access unprivileged.

**Non-Goals:**

- Discovering, downloading, listing, deleting, or running models.
- Creating a selected directory, accepting relative configured paths, or changing `COQUENA_HOME`.
- Giving the browser privileged native-path access, a desktop bridge, or model scanning through a browser handle.

## Decisions

### Use a shared configuration application service

Add a narrow internal configuration boundary that resolves the application home, opens the initialized configuration, returns the effective models directory, and validates/persists an override. CLI and HTTP adapters call this boundary; Angular remains an HTTP client. This matches the established runtime application-service pattern and avoids divergent CLI/WebUI validation.

### Store only an explicit override in schema version 2

New installations write schema version 2. The document has an optional `models.directory` absolute path; if absent, the effective value is `<resolved-home>/models`. Existing schema-version-1 files remain valid and resolve to that default without being rewritten by `coquena init` or a read. The first successful override atomically publishes schema version 2. This preserves prior idempotent initialization behavior and avoids a silent configuration migration.

The default directory is part of the portable installation layout, so `init` creates `models/` alongside its other required directories. An override is not created: it must already exist, be a directory, and pass a write probe.

### Expose read/update endpoints and a symmetric CLI command

Use `GET /api/config/models-directory` for the effective directory and `PUT /api/config/models-directory` with an exact JSON body containing `path` to update it. Successful documents use the existing schema-version-1 API envelope convention. Uninitialized installations, malformed bodies, and invalid paths return safe non-success JSON errors rather than Angular fallback content.

Use `coquena config models-directory [<path>] [--json]`: no path reads the effective value, and one path validates and persists it. This keeps the setting discoverable under a future `config` namespace while avoiding a premature generic configuration language.

### Keep path entry manual

Angular provides a manual text field and normal save action. The browser directory picker was rejected because a `FileSystemDirectoryHandle` does not provide the native absolute path required by the backend, creating a confusing two-step interaction. A future native desktop bridge could provide a true path picker without changing the persistence contract.

## Risks / Trade-offs

- [Schema changes reject older binaries reading new config] → Version 2 is intentional; the Change updates the current binary and keeps version 1 readable. Downgrade requires removing the explicit override or restoring a version-1 configuration backup.
- [Write probing a directory changes it] → Use a uniquely named temporary file in the candidate directory and remove it; report failure without changing configuration.
- [Network/UNC paths are slow or temporarily unavailable on Windows] → Validate synchronously with bounded filesystem operations and return an actionable error; do not cache a failed override.
- [Manual path entry can contain an invalid path] → Validate it through the backend and retain the last saved value after an error.

## Migration Plan

1. Install the updated executable.
2. Existing version-1 configurations continue using `<home>/models`; a later `coquena init` creates the new default directory without rewriting config.
3. A successful explicit update atomically writes version 2.
4. Roll back by restoring a version-1 config without `models.directory`; model artifacts and directories remain untouched.
