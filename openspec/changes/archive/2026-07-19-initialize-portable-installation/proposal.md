## Why

Coquena currently has no product-level initialization flow, so a portable executable cannot establish the directories and configuration required for later runtime management. The first product capability should let an operator initialize Coquena exactly where it was placed, without copying the executable or installing anything into the operating system.

## What Changes

- Add an explicit `coquena init` command that initializes Coquena beside the running executable rather than in the caller's working directory.
- Create the portable installation's required directory structure and initial JSON configuration safely and idempotently.
- Report the resolved installation root and initialization result to the operator.
- Fail without presenting the installation as initialized when the target is not writable, conflicts with required paths, or configuration creation cannot complete.
- Keep all Coquena application configuration in JSON; the initializer does not create YAML configuration.
- Do not copy or move the executable, register Coquena with the operating system, elevate privileges, modify `PATH`, create a service, or download llama.cpp.

## Capabilities

### New Capabilities

- `portable-initialization`: Initialize and recognize a self-contained Coquena installation rooted at the directory containing the executable.

### Modified Capabilities

None.

## Impact

- Extends the Go CLI entry point and introduces internal filesystem and configuration initialization behavior.
- Adds portable installation directories and a versioned JSON configuration contract.
- Adds automated tests for path resolution, first initialization, repeated initialization, atomic failure behavior, and filesystem conflicts.
- Does not affect the Angular application, llama.cpp integration, operating-system installers, services, or external APIs.
