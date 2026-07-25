## MODIFIED Requirements

### Requirement: Self-contained installation structure
Coquena SHALL create `config`, `data`, `downloads`, `logs`, `models`, `runtimes`, and `tmp` directories directly beneath the installation root and SHALL keep the executable in its existing location.

#### Scenario: Initialize an empty portable distribution
- **WHEN** the executable directory contains the Coquena executable but no initialization artifacts
- **THEN** Coquena creates every required directory beneath that executable directory
- **AND** Coquena does not copy, move, rename, or remove the executable

### Requirement: JSON application configuration
Coquena SHALL create the initial application configuration at `config/coquena.json` as valid JSON containing integer `schemaVersion` value `2`, and SHALL NOT create application configuration in YAML format. The version-2 document SHALL permit an optional absolute `models.directory` override. Coquena SHALL continue to recognize an existing valid schema-version-1 configuration as initialized and use the portable default models directory without rewriting that configuration.

#### Scenario: Create initial configuration
- **WHEN** `coquena init` initializes a new portable installation successfully
- **THEN** `config/coquena.json` contains valid JSON with `schemaVersion` equal to `2`
- **AND** the JSON document ends with a newline
- **AND** no YAML application configuration is created

#### Scenario: Recognize a prior schema-version-1 configuration
- **WHEN** an existing installation contains a valid schema-version-1 `config/coquena.json`
- **THEN** `coquena init` succeeds and preserves that file byte-for-byte
- **AND** Coquena resolves the default models directory beneath the application home
