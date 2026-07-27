## ADDED Requirements

### Requirement: Protect fields referenced by persisted presets
Coquena SHALL refuse to remove a field when that field, or any field in its descendant subtree, holds a value in a persisted preset, reporting a diagnostic that names the referencing presets. Removal SHALL proceed only when the operator explicitly forces it, and a forced removal SHALL cascade: it SHALL delete the field and its descendant subtree from the catalog and SHALL delete every value those fields hold in every preset, including the global preset. A forced removal SHALL publish the catalog and the presets so that neither document is left referencing content the other has dropped.

#### Scenario: Refuse to remove a referenced field
- **WHEN** an operator removes a field whose values exist in one or more presets and does not force the removal
- **THEN** Coquena refuses the removal and names every referencing preset
- **AND** the catalog and the presets are unchanged

#### Scenario: Refuse to remove a parent whose descendant is referenced
- **WHEN** an operator removes a field whose descendant holds a value in a preset and does not force the removal
- **THEN** Coquena refuses the removal and names the referencing presets and the referenced descendant
- **AND** the catalog and the presets are unchanged

#### Scenario: Force a cascading removal
- **WHEN** an operator forces removal of a referenced field
- **THEN** Coquena removes the field and its descendant subtree from the catalog
- **AND** it removes every value those fields held in every preset, including the global preset
- **AND** later reads report neither the fields nor those values

#### Scenario: Remove an unreferenced field without forcing
- **WHEN** an operator removes a field no preset references
- **THEN** Coquena removes it and its descendant subtree without requiring the operator to force it

#### Scenario: Leave both documents intact on a failed cascade
- **WHEN** publishing either document during a forced removal fails
- **THEN** the previously persisted catalog and presets are both still readable and unchanged

#### Scenario: Reveal an inherited value as a reference
- **WHEN** a field holds a value only in the global preset
- **THEN** removing it without forcing is refused and names the global preset

## MODIFIED Requirements

### Requirement: Manage the catalog through the local HTTP API
The local WebUI server SHALL expose fields as resources under `/api/fields`, addressed by canonical name. `GET /api/fields` SHALL return the catalog with each field's availability. `POST /api/fields` SHALL define one field and SHALL refuse a name already in the catalog. `GET /api/fields/<name>` SHALL read one field, `PUT /api/fields/<name>` SHALL create or replace that field, and `DELETE /api/fields/<name>` SHALL remove it subject to the protection of fields referenced by persisted presets, accepting an explicit force parameter to cascade. Every mutation SHALL be validated against the whole catalog before the document is published. Invalid input and known validation failures SHALL be non-success JSON API responses and SHALL NOT fall back to the Angular application document.

#### Scenario: Read the catalog through HTTP
- **WHEN** a browser requests `GET /api/fields` after initialization
- **THEN** it receives status `200` and a JSON document containing every field with its availability

#### Scenario: Define a field through HTTP
- **WHEN** a browser sends `POST /api/fields` with a valid field definition
- **THEN** it receives a successful JSON document containing the defined field
- **AND** a later `GET /api/fields/<name>` returns that field

#### Scenario: Refuse to define an existing name through HTTP
- **WHEN** a browser sends `POST /api/fields` for a name already in the catalog
- **THEN** the server returns a non-success JSON API response identifying the existing field
- **AND** the persisted catalog is unchanged

#### Scenario: Upsert a field through HTTP
- **WHEN** a browser sends `PUT /api/fields/<name>` with a valid definition
- **THEN** the server creates that field if it is absent and replaces it if it is present
- **AND** a later `GET /api/fields/<name>` returns the submitted definition

#### Scenario: Remove a field through HTTP
- **WHEN** a browser sends `DELETE /api/fields/<name>` for a field no preset references
- **THEN** the server removes that field and its descendant subtree
- **AND** a later `GET /api/fields/<name>` reports that the field is absent

#### Scenario: Refuse to remove a referenced field through HTTP
- **WHEN** a browser sends `DELETE /api/fields/<name>` for a field a preset references without the force parameter
- **THEN** the server returns a non-success JSON API response naming the referencing presets
- **AND** the persisted catalog and presets are unchanged

#### Scenario: Force a cascading removal through HTTP
- **WHEN** a browser sends `DELETE /api/fields/<name>` with the force parameter for a referenced field
- **THEN** the server removes the field and its descendant subtree and the values they held in every preset

#### Scenario: Reject an invalid mutation
- **WHEN** a submitted field has an invalid name, a colliding alias, an unknown kind, or a parent reference that does not resolve
- **THEN** the server returns a non-success JSON API response identifying the problem
- **AND** the persisted catalog is unchanged

#### Scenario: Reject malformed HTTP input
- **WHEN** a browser sends an unsupported method, malformed JSON, or an unknown property
- **THEN** the server returns `405` or a non-success JSON API response as appropriate
- **AND** it changes no persisted content

### Requirement: Manage the catalog through the CLI
Coquena SHALL expose a `coquena field` command family analogous to the field API: `define <kind> <name>` to add a field, `remove <name>` to delete one, `list [--json]` to display the catalog, and `show <name> [--json]` to display one field by canonical name or alias. `define` SHALL state the kind before the name and SHALL accept the remaining field metadata as flags for default value, a comma-separated list of enum options, parent prerequisite, short-argument and environment-variable aliases, provider, and the first admitting and first excluding releases. `define --overwrite` SHALL create or replace the named field; without `--overwrite` an existing name SHALL prompt for confirmation on an interactive terminal and SHALL fail with a diagnostic otherwise. `remove` SHALL be subject to the protection of fields referenced by persisted presets and SHALL accept `--force` to cascade. Human output SHALL identify each field's name, kind, default value when one is set, enum options when the kind is enum, parent prerequisite when one is declared, and declared availability, without requiring `--json` to discover them.

#### Scenario: Define a field
- **WHEN** an operator runs `coquena field define integer context-size --default 4096 --provider official --introduced-in b10107`
- **THEN** the command exits successfully and reports the defined field
- **AND** a later `coquena field list` includes it

#### Scenario: Define an enum field from a comma-separated option list
- **WHEN** an operator runs `coquena field define enum spec-type --options none,draft-simple,draft-eagle3`
- **THEN** the command exits successfully and defines a field with those three options in order
- **AND** it does not require repeating `--option` for each value

#### Scenario: Upsert a field
- **WHEN** an operator runs `coquena field define integer context-size --default 8192 --overwrite` for a name already in the catalog
- **THEN** the command exits successfully and replaces that definition
- **AND** a later `coquena field show context-size` reports the new default

#### Scenario: Replay a catalog script
- **WHEN** an operator runs a script of `coquena field define ... --overwrite` commands twice against the same installation
- **THEN** both runs exit successfully
- **AND** the resulting catalog is identical after each run

#### Scenario: Refuse a non-interactive overwrite
- **WHEN** an operator runs `coquena field define` for an existing name without `--overwrite` and the terminal is not interactive
- **THEN** the command exits unsuccessfully identifying the existing field and `--overwrite`
- **AND** it changes no persisted content

#### Scenario: Remove a field
- **WHEN** an operator runs `coquena field remove <name>` for a field no preset references
- **THEN** the command exits successfully and removes that field and its descendant subtree
- **AND** a later `coquena field list` omits them

#### Scenario: Refuse to remove a referenced field
- **WHEN** an operator runs `coquena field remove <name>` for a field a preset references without `--force`
- **THEN** the command exits unsuccessfully naming the referencing presets and `--force`
- **AND** it changes no persisted content

#### Scenario: Force a cascading removal from the CLI
- **WHEN** an operator runs `coquena field remove <name> --force` for a referenced field
- **THEN** the command exits successfully and reports every preset value it removed

#### Scenario: List the catalog
- **WHEN** an operator runs `coquena field list` after initialization
- **THEN** the command exits successfully and reports each field with its kind, default value, enum options, and availability

#### Scenario: Inspect default and enum options without JSON
- **WHEN** an operator runs `coquena field list` or `coquena field show <name>` on a catalog containing a field with a default value and an enum field with options
- **THEN** the human-readable output displays that default value and those options directly
- **AND** the operator does not need `--json` to discover them

#### Scenario: Show a field by alias
- **WHEN** an operator runs `coquena field show` with a field's short argument or environment-variable alias
- **THEN** the command reports that field under its canonical name

#### Scenario: Reject an invalid definition
- **WHEN** an operator runs `coquena field define` with an unknown kind, an invalid name, a colliding alias, or an unresolvable parent
- **THEN** the command exits unsuccessfully with an actionable diagnostic
- **AND** it changes no persisted content

#### Scenario: Use the commands before initialization
- **WHEN** an operator runs a field command before `coquena init`
- **THEN** the command exits unsuccessfully with guidance to run `coquena init`
- **AND** it creates no configuration document
