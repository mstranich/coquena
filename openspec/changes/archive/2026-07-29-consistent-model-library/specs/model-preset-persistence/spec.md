## MODIFIED Requirements

### Requirement: Manage presets through the CLI
Coquena SHALL provide a `coquena preset` command family to define, rename, remove, list, and show presets, to set and unset values on a preset or on `global`, to bind a model, to assign and unassign a companion role, to bind and unbind a runtime constraint, and to render the INI for a runtime. `render` SHALL report the INI on standard output and SHALL write a file only when `--write` is given, taking an optional path. Human-readable output SHALL report each effective value with its inheritance source, and SHALL report the model binding, any companion assignments, and both the declared and computed runtime constraints without requiring JSON output. Coquena SHALL additionally report the installed artifacts admissible for a preset's companion criterion, selecting none of them. Every command SHALL also offer JSON output.

#### Scenario: Define and populate a preset
- **WHEN** an operator defines a preset with a model reference and then sets a value on it
- **THEN** both the binding and the value are persisted

#### Scenario: Show a preset
- **WHEN** an operator shows a preset
- **THEN** the output reports its name, title, model binding, companion assignments, declared and computed runtime constraints, and every effective value labeled owned or inherited

#### Scenario: Assign a companion from the CLI
- **WHEN** an operator assigns a compatible projector to a preset that is bound to a principal
- **THEN** the assignment is persisted
- **AND** showing the preset reports it

#### Scenario: Report admissible companions from the CLI
- **WHEN** an operator asks which installed artifacts satisfy a preset's companion criterion
- **THEN** the output reports every admissible artifact
- **AND** Coquena selects none of them

#### Scenario: Refuse an incompatible companion from the CLI
- **WHEN** an operator assigns a companion that does not satisfy its role's criterion
- **THEN** the CLI reports the criterion that failed and exits with a non-zero status
- **AND** the preset's existing companion assignment is unchanged

#### Scenario: Unassign a companion
- **WHEN** an operator unassigns a preset's companion role
- **THEN** the assignment is removed
- **AND** the preset's model binding and values are unchanged

#### Scenario: List presets
- **WHEN** an operator lists presets
- **THEN** the output reports the global preset distinctly from the model presets

#### Scenario: Set a global default from the CLI
- **WHEN** an operator sets a value on `global`
- **THEN** the value is stored in the global preset
- **AND** presets that do not override it report it as inherited

#### Scenario: Render from the CLI
- **WHEN** an operator runs the render command for a runtime without `--write`
- **THEN** the INI is written to standard output
- **AND** no file is created

#### Scenario: Report a refusal
- **WHEN** a command is refused
- **THEN** the CLI reports the diagnostic and exits with a non-zero status
- **AND** the persisted document is unchanged
