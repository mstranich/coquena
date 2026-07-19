## Context

Coquena currently prints a development message and has no product configuration or initialized filesystem state. The next baby step establishes a portable application home that remains exactly where a user or a future installer places the executable. A later Bash installer, MSI/MSIX package, or manual extraction process may choose that location, but `coquena init` must only initialize it in place.

The executable directory may differ from the process working directory, and a launcher may reach the executable through a filesystem link. Coquena must also preserve Unix-style repository conventions while validating the first product runtime on Windows. All Coquena application configuration is JSON; OpenSpec's own YAML metadata is outside this product constraint.

## Goals / Non-Goals

**Goals:**

- Provide an explicit, testable `coquena init` CLI outcome.
- Derive a stable installation root from the resolved executable location.
- Create a self-contained directory tree and minimal versioned JSON configuration.
- Make repeated initialization non-destructive and make configuration publication atomic.
- Produce actionable path-based success and failure messages.

**Non-Goals:**

- Copying, moving, installing, upgrading, or uninstalling the Coquena executable.
- Selecting a platform installation location or changing operating-system integration.
- Registering a service, modifying `PATH`, elevating privileges, or creating shortcuts.
- Downloading or configuring llama.cpp, models, or any other external component.
- Supporting configuration migration or repair beyond safely completing missing directories.
- Adding UI or HTTP API behavior.

## Decisions

### Resolve the application home from the executable

The initializer will obtain the executable path from the operating system, resolve filesystem links, convert it to an absolute clean path, and use its parent directory as the installation root. It will not use the current working directory and will not accept a separate home-directory override in this change.

This preserves genuine in-place portability and lets future installation mechanisms choose the location without coupling that policy to Coquena. A configurable home was rejected for now because it would allow executable and application state to diverge and would expand the first capability.

### Use a fixed self-contained layout

The installation root will contain these application-owned directories:

```text
<application-home>/
├── coquena[.exe]
├── config/
│   └── coquena.json
├── data/
├── downloads/
├── logs/
├── runtimes/
└── tmp/
```

The directories are introduced now because each has an immediate boundary in the portable installation contract, even though later changes will populate most of them. The executable stays where it is; `init` never introduces a `bin` directory or relocates the process image.

### Use a minimal versioned JSON document

The first configuration will be a deterministic, human-readable JSON document:

```json
{
  "schemaVersion": 1
}
```

It will use two-space indentation and a final newline. No runtime defaults are added before a runtime capability exists. `schemaVersion` provides an explicit compatibility boundary without inventing migration behavior in this change.

### Publish configuration atomically

The initializer will encode and validate the complete configuration in memory, write it to a uniquely named temporary file in `config`, close it successfully, and atomically rename it to `coquena.json`. The temporary file must be removed on failure. Keeping the temporary file in the destination directory avoids cross-filesystem rename behavior.

Directory creation itself is safely repeatable but not transactional. A failed attempt may leave empty standard directories, which a later `init` can reuse; the existence of a complete, valid, supported `config/coquena.json` is the initialization marker.

### Preserve existing content

Initialization will create directories with non-destructive recursive directory creation. Before treating an existing installation as valid, it will parse `config/coquena.json`, require the supported schema version, and preserve its bytes. Invalid JSON, unsupported schema versions, and non-directory conflicts are errors rather than repair opportunities.

This favors operator data safety over automatic recovery. Configuration backup, migration, reset, and repair commands are deferred.

### Keep CLI parsing and initialization logic separated

The CLI entry point will dispatch the `init` subcommand, while an internal initialization package will own executable-path resolution, filesystem operations, JSON encoding/validation, and typed results. Filesystem-facing behavior will accept injectable path resolution or an explicit executable path at its test boundary so tests do not modify the built test binary's directory.

No external CLI or configuration dependency is needed for this narrow command.

## Risks / Trade-offs

- **The chosen executable directory may not be writable** → Fail with the affected path and leave permission/elevation decisions to the user or future installer.
- **A system installer may place Coquena under `/opt` or `Program Files` with unsuitable ownership** → Document that installers must arrange write access for the account that initializes and later operates the portable tree.
- **A crash can leave some empty directories** → Treat directory creation as safely repeatable and use only the valid JSON file as the initialization marker.
- **Concurrent `init` processes can race to publish configuration** → Use exclusive temporary creation and a destination publication strategy that never silently replaces an existing configuration; the loser re-validates the winning file or returns a safe conflict.
- **Resolving filesystem links changes the apparent launcher location** → Prefer the real executable directory so a link in a PATH directory does not accidentally become the application home.
- **Keeping mutable runtime data beside the executable limits read-only deployments** → This is an intentional consequence of the selected portable model; split system/user state is out of scope.

## Migration Plan

There is no existing product installation or configuration to migrate. The change is additive. Rolling back means using the prior executable; any initialized directory tree can remain untouched because the prior version does not consume it.

## Open Questions

None for this change. Installation-location policy, platform installers, and llama.cpp provisioning are intentionally deferred.
