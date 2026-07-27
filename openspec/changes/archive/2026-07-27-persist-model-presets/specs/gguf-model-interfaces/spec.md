## ADDED Requirements

### Requirement: List installed models through the CLI
Coquena SHALL provide a CLI command that lists the models present in the configured model directory, reporting for each one the reference form accepted when binding it and its resolved artifact filename. The command SHALL issue no network request and SHALL report an empty list rather than an error when no model is installed.

#### Scenario: List installed models
- **WHEN** an operator lists models with artifacts installed
- **THEN** the output reports each installed model's repository, quant reference form, and resolved filename
- **AND** the command issues no network request

#### Scenario: List an empty library
- **WHEN** an operator lists models with no artifact installed
- **THEN** the command reports an empty list and succeeds

#### Scenario: List models as JSON
- **WHEN** an operator requests JSON output
- **THEN** the output is machine-readable and reports the same installed models
