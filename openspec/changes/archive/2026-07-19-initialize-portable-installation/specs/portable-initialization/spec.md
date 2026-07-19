## ADDED Requirements

### Requirement: Explicit portable initialization
Coquena SHALL expose a `coquena init` command that initializes an installation in the directory containing the resolved Coquena executable and SHALL NOT use the process working directory as the installation root.

#### Scenario: Initialize from another working directory
- **WHEN** the operator invokes `coquena init` while the process working directory differs from the executable directory
- **THEN** Coquena creates the installation structure in the executable directory
- **AND** Coquena creates no installation files in the process working directory

#### Scenario: Executable reached through a link
- **WHEN** the operator invokes `coquena init` through a filesystem link to the executable
- **THEN** Coquena uses the directory containing the resolved executable target as the installation root

### Requirement: Self-contained installation structure
Coquena SHALL create `config`, `data`, `downloads`, `logs`, `runtimes`, and `tmp` directories directly beneath the installation root and SHALL keep the executable in its existing location.

#### Scenario: Initialize an empty portable distribution
- **WHEN** the executable directory contains the Coquena executable but no initialization artifacts
- **THEN** Coquena creates every required directory beneath that executable directory
- **AND** Coquena does not copy, move, rename, or remove the executable

### Requirement: JSON application configuration
Coquena SHALL create the initial application configuration at `config/coquena.json` as valid JSON containing integer `schemaVersion` value `1`, and SHALL NOT create application configuration in YAML format.

#### Scenario: Create initial configuration
- **WHEN** `coquena init` initializes a new portable installation successfully
- **THEN** `config/coquena.json` contains valid JSON with `schemaVersion` equal to `1`
- **AND** the JSON document ends with a newline
- **AND** no YAML application configuration is created

### Requirement: Atomic configuration publication
Coquena SHALL publish the initial JSON configuration atomically so that `config/coquena.json` is either absent or complete and valid.

#### Scenario: Configuration publication fails
- **WHEN** Coquena cannot complete publication of the initial configuration
- **THEN** Coquena exits unsuccessfully
- **AND** Coquena does not leave a partial `config/coquena.json`
- **AND** Coquena does not report the installation as initialized

### Requirement: Idempotent initialization
Coquena SHALL allow `coquena init` to be run repeatedly without replacing a valid existing configuration or deleting existing installation content.

#### Scenario: Repeat initialization of a valid installation
- **WHEN** the required directories and a valid supported `config/coquena.json` already exist
- **THEN** `coquena init` succeeds
- **AND** the existing configuration remains byte-for-byte unchanged
- **AND** Coquena reports that the installation was already initialized

#### Scenario: Complete missing directories safely
- **WHEN** a valid supported `config/coquena.json` exists and one or more required directories are absent
- **THEN** `coquena init` creates the missing required directories
- **AND** preserves the existing configuration and all other content

### Requirement: Initialization conflict safety
Coquena SHALL stop initialization without overwriting content when a required directory path is occupied by a non-directory or when an existing configuration is invalid or unsupported.

#### Scenario: Required directory path is a file
- **WHEN** a non-directory already exists at a required directory path
- **THEN** `coquena init` exits unsuccessfully
- **AND** identifies the conflicting path
- **AND** does not replace or delete the conflicting entry

#### Scenario: Existing configuration is invalid JSON
- **WHEN** `config/coquena.json` exists but is not valid JSON
- **THEN** `coquena init` exits unsuccessfully
- **AND** preserves the existing file unchanged

#### Scenario: Existing configuration uses an unsupported schema
- **WHEN** `config/coquena.json` contains an unsupported `schemaVersion`
- **THEN** `coquena init` exits unsuccessfully
- **AND** preserves the existing file unchanged

### Requirement: Clear initialization result
Coquena SHALL write a concise result to standard output on success and a diagnostic to standard error on failure, including the resolved installation root or conflicting path relevant to the result.

#### Scenario: New initialization succeeds
- **WHEN** Coquena completes a new initialization
- **THEN** the command exits successfully
- **AND** standard output reports that Coquena was initialized and identifies the installation root

#### Scenario: Initialization lacks write access
- **WHEN** Coquena cannot create required content because the installation root is not writable
- **THEN** the command exits unsuccessfully
- **AND** standard error identifies the path that could not be written

### Requirement: No operating-system installation side effects
The initializer SHALL limit its filesystem changes to the installation root and SHALL NOT register Coquena with the operating system, elevate privileges, modify `PATH`, create a service, or download external components.

#### Scenario: Initialization completes
- **WHEN** `coquena init` completes successfully
- **THEN** all files and directories created by the command are beneath the installation root
- **AND** no llama.cpp binary or other external component is downloaded
