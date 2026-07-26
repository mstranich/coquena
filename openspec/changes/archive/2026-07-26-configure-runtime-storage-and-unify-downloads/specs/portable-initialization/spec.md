## MODIFIED Requirements

### Requirement: Self-contained installation structure
Coquena SHALL create `config`, `data`, `logs`, `models`, `runtimes`, and `tmp`
directories directly beneath the installation root and SHALL keep the executable
in its existing location. It SHALL NOT create or use `<home>/downloads` as part
of the portable installation structure.

#### Scenario: Initialize an empty portable distribution
- **WHEN** the executable directory contains the Coquena executable but no initialization artifacts
- **THEN** Coquena creates every required directory except `downloads` beneath that executable directory
- **AND** Coquena does not copy, move, rename, or remove the executable

### Requirement: JSON application configuration
Coquena SHALL create the initial application configuration at
`config/coquena.json` as valid JSON containing integer `schemaVersion` value
`3`, and SHALL NOT create application configuration in YAML format. The
version-3 document SHALL permit independent optional absolute `models.directory`
and `runtimes.directory` overrides. Coquena SHALL reject schema-version-1 and
schema-version-2 configuration rather than read, migrate, or rewrite it.

#### Scenario: Create initial configuration
- **WHEN** `coquena init` initializes a new portable installation successfully
- **THEN** `config/coquena.json` contains valid JSON with `schemaVersion` equal to `3`
- **AND** the JSON document ends with a newline
- **AND** no YAML application configuration is created

#### Scenario: Reject a prior schema-version configuration
- **WHEN** an existing installation contains a valid schema-version-1 or schema-version-2 `config/coquena.json`
- **THEN** Coquena rejects the configuration with an explicit unsupported-schema diagnostic
- **AND** it does not create or migrate runtime or download state
