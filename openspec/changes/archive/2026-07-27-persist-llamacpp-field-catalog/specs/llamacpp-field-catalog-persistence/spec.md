## ADDED Requirements

### Requirement: Persist one field catalog durably
Coquena SHALL persist the llama.cpp field catalog in `config/llamacpp-fields.json` beneath the resolved application home, containing an integer `schemaVersion` equal to `1`. The document SHALL be published atomically so that a failed write leaves the previous catalog intact. An initialized installation with no catalog document SHALL resolve an empty catalog rather than an error. Coquena SHALL NOT store the catalog in `config/coquena.json`.

#### Scenario: Resolve an installation with no catalog
- **WHEN** an initialized installation has no catalog document
- **THEN** Coquena reports an empty catalog
- **AND** it creates no catalog document until a field is defined

#### Scenario: Publish the first field
- **WHEN** an operator defines a field in an installation with no catalog document
- **THEN** Coquena creates `config/llamacpp-fields.json` with `schemaVersion` equal to `1`
- **AND** the document ends with a newline
- **AND** `config/coquena.json` is unchanged

#### Scenario: Survive a restart
- **WHEN** an operator defines a field and Coquena is restarted
- **THEN** a later read of the catalog contains that field

#### Scenario: Report an unreadable catalog
- **WHEN** the catalog document is malformed or declares an unsupported schema version
- **THEN** Coquena reports an actionable diagnostic identifying the document
- **AND** it does not rewrite, discard, or silently repair operator-authored content

### Requirement: Identify a field by its canonical name and kind
Each field SHALL be identified by a canonical argument name that is a bare INI key without leading dashes, unique across the catalog, and SHALL declare exactly one required immutable kind from string, integer, boolean, void, or enum. Coquena SHALL NOT assign a field any identifier other than its canonical name. A field MAY declare a short-argument alias and an environment-variable alias, which SHALL identify the same field and SHALL NOT be separate catalog entries. Coquena SHALL treat the canonical long name as the single authoritative name.

#### Scenario: Reject a field without a kind
- **WHEN** a submitted field declares no kind or an unknown kind
- **THEN** Coquena rejects it and identifies the supported kinds
- **AND** it does not infer a kind from the name or default value

#### Scenario: Recognize an alias as the same field
- **WHEN** a field declares a short argument and an environment-variable alias
- **THEN** the catalog exposes one field carrying those aliases
- **AND** it does not expose an additional field for either alias

#### Scenario: Reject a conflicting alias
- **WHEN** a submitted field declares an alias already used as the canonical name or alias of another field
- **THEN** Coquena rejects the submission and identifies the conflicting name

#### Scenario: Reject an invalid canonical name
- **WHEN** a submitted field declares a name containing leading dashes, whitespace, or `=`
- **THEN** Coquena rejects the submission because catalog names are bare INI keys

### Requirement: Declare provider-qualified build availability
Each field SHALL declare availability as a provider together with an optional first admitting release and an optional first excluding release. The only provider recognized by this capability SHALL be the official llama.cpp distribution, whose releases are `b<digits>` build tags compared as an ordered range. Coquena SHALL evaluate a field against a given official release as `admitted`, `not-admitted`, or `unknown`, and SHALL report `unknown` rather than guessing when a release cannot be ordered against the declared range.

#### Scenario: Admit a field within its range
- **WHEN** a field declares a first admitting release at or below the evaluated official release and no first excluding release at or below it
- **THEN** Coquena evaluates that field as `admitted` for that release

#### Scenario: Exclude a field outside its range
- **WHEN** an evaluated official release precedes the field's first admitting release or is at or after its first excluding release
- **THEN** Coquena evaluates that field as `not-admitted` for that release
- **AND** the result identifies the declared range

#### Scenario: Leave an unbounded field undetermined
- **WHEN** a field declares no first admitting release
- **THEN** Coquena evaluates it as `unknown` for every official release
- **AND** it does not infer a range from any other field

#### Scenario: Keep availability advisory
- **WHEN** a field evaluates as `not-admitted` or `unknown` for the current runtime
- **THEN** Coquena continues to expose that field in the catalog
- **AND** authoring, saving, and deleting that field remain permitted

### Requirement: Manage the catalog through the local HTTP API
The local WebUI server SHALL expose fields as resources under `/api/fields`, addressed by canonical name. `GET /api/fields` SHALL return the catalog with each field's availability. `POST /api/fields` SHALL define one field and SHALL refuse a name already in the catalog. `GET /api/fields/<name>` SHALL read one field, `PUT /api/fields/<name>` SHALL create or replace that field, and `DELETE /api/fields/<name>` SHALL remove it. Every mutation SHALL be validated against the whole catalog before the document is published. Invalid input and known validation failures SHALL be non-success JSON API responses and SHALL NOT fall back to the Angular application document.

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
- **WHEN** a browser sends `DELETE /api/fields/<name>`
- **THEN** the server removes that field and its descendant subtree
- **AND** a later `GET /api/fields/<name>` reports that the field is absent

#### Scenario: Reject an invalid mutation
- **WHEN** a submitted field has an invalid name, a colliding alias, an unknown kind, or a parent reference that does not resolve
- **THEN** the server returns a non-success JSON API response identifying the problem
- **AND** the persisted catalog is unchanged

#### Scenario: Reject malformed HTTP input
- **WHEN** a browser sends an unsupported method, malformed JSON, or an unknown property
- **THEN** the server returns `405` or a non-success JSON API response as appropriate
- **AND** it changes no persisted content

### Requirement: Manage the catalog through the CLI
Coquena SHALL expose a `coquena field` command family analogous to the field API: `define <kind> <name>` to add a field, `remove <name>` to delete one, `list [--json]` to display the catalog, and `show <name> [--json]` to display one field by canonical name or alias. `define` SHALL state the kind before the name and SHALL accept the remaining field metadata as flags for default value, a comma-separated list of enum options, parent prerequisite, short-argument and environment-variable aliases, provider, and the first admitting and first excluding releases. `define --overwrite` SHALL create or replace the named field; without `--overwrite` an existing name SHALL prompt for confirmation on an interactive terminal and SHALL fail with a diagnostic otherwise. Human output SHALL identify each field's name, kind, default value when one is set, enum options when the kind is enum, parent prerequisite when one is declared, and declared availability, without requiring `--json` to discover them.

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
- **WHEN** an operator runs `coquena field remove <name>`
- **THEN** the command exits successfully and removes that field and its descendant subtree
- **AND** a later `coquena field list` omits them

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
