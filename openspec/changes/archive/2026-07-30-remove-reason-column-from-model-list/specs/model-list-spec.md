## REMOVED Requirements

### Requirement: model list REASON column
**Reason**: Not requested by users; adds noise to the common case where every entry is usable
**Migration**: The JSON output path still carries the full `Detail` field for programmatic consumption

#### Scenario: model list without REASON column
- **WHEN** user runs `coquena model list`
- **THEN** table displays columns: STATE, TYPE, USABLE, REFERENCE, FILENAME (no REASON column)

#### Scenario: model list JSON output retains Detail
- **WHEN** user runs `coquena model list --json`
- **THEN** JSON output includes `detail` field in each entry
