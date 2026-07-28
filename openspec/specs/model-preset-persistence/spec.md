# model-preset-persistence Specification

## Purpose
TBD - created by archiving change persist-model-presets. Update Purpose after archive.
## Requirements
### Requirement: Persist presets durably
Coquena SHALL persist model presets in `config/model-presets.json` beneath the resolved application home, containing an integer `schemaVersion` equal to `1`. The document SHALL be published atomically so that a failed write leaves the previous document intact. An initialized installation with no preset document SHALL resolve an empty preset set rather than an error. Coquena SHALL NOT store presets in `config/coquena.json` or in `config/llamacpp-fields.json`.

#### Scenario: Resolve an installation with no presets
- **WHEN** an initialized installation has no preset document
- **THEN** Coquena reports an empty preset set with an empty global preset
- **AND** it creates no preset document until a preset or a global value is authored

#### Scenario: Publish the first preset
- **WHEN** an operator defines a preset in an installation with no preset document
- **THEN** Coquena creates `config/model-presets.json` with `schemaVersion` equal to `1`
- **AND** the document ends with a newline
- **AND** `config/coquena.json` and `config/llamacpp-fields.json` are unchanged

#### Scenario: Survive a restart
- **WHEN** an operator authors a preset value and Coquena is restarted
- **THEN** a later read reports that value

#### Scenario: Leave the document intact on a failed write
- **WHEN** publishing a preset mutation fails
- **THEN** the previously persisted document is still readable and unchanged

#### Scenario: Reject an unsupported document version
- **WHEN** the preset document declares a `schemaVersion` Coquena does not support
- **THEN** Coquena reports an actionable diagnostic naming the document and the unsupported version
- **AND** it does not overwrite the document

### Requirement: Identify a preset by its INI section name
Coquena SHALL identify a preset by a name that is also its `--models-preset` INI section name and the model identifier it will be served under. A name SHALL be lowercase, composed of alphanumeric segments separated by `-` or `.` with no leading, trailing, or repeated separator, unique across the document, and SHALL NOT be `global` or `render`. A preset MAY carry a separate optional display title with no identity role. Coquena SHALL expose exactly one reserved global preset named `global` that holds inherited default values, has no model binding and no runtime binding, and cannot be created or deleted.

#### Scenario: Define a preset
- **WHEN** an operator defines a preset named `qwen3-fast` with the title `Qwen3 Fast`
- **THEN** Coquena stores the preset under the name `qwen3-fast`
- **AND** the title does not affect its identity

#### Scenario: Reject a name that is not lowercase separated segments
- **WHEN** an operator defines a preset named `Qwen3 Fast`
- **THEN** Coquena refuses the definition with a diagnostic naming the required form
- **AND** no preset is stored

#### Scenario: Accept a dotted version number in a name
- **WHEN** an operator defines a preset named `qwen3.5-4b-fast`
- **THEN** Coquena stores the preset under that name

#### Scenario: Reject uppercase in a name
- **WHEN** an operator defines a preset named `Qwen3.5-4B`
- **THEN** Coquena refuses the definition with a diagnostic naming the required form

#### Scenario: Reject a reserved name
- **WHEN** an operator defines a preset named `global` or `render`
- **THEN** Coquena refuses the definition with a diagnostic naming the reserved word

#### Scenario: Refuse a duplicate definition
- **WHEN** an operator defines a preset whose name already exists
- **THEN** Coquena refuses the definition and names the operation that corrects the existing preset
- **AND** the existing preset and its authored values are unchanged

#### Scenario: Address the global preset
- **WHEN** an operator authors a value on `global`
- **THEN** Coquena stores it in the preset that renders as the `[*]` section

#### Scenario: Refuse to delete the global preset
- **WHEN** an operator removes `global`
- **THEN** Coquena refuses the removal
- **AND** the global values are unchanged

### Requirement: Rename a preset without losing authored state
Coquena SHALL rename a preset to another valid unused name, preserving its title, bindings, and every authored value. Renaming SHALL be refused when the target name is invalid, reserved, or already used.

#### Scenario: Rename a mistyped preset
- **WHEN** an operator renames `qwen3-fst` to `qwen3-fast`
- **THEN** the preset is addressable as `qwen3-fast`
- **AND** its bindings and authored values are unchanged
- **AND** `qwen3-fst` is no longer addressable

#### Scenario: Refuse a colliding rename
- **WHEN** an operator renames a preset to a name already in use
- **THEN** Coquena refuses the rename
- **AND** both presets are unchanged

### Requirement: Bind a preset to a model
Coquena SHALL bind a preset to a model through the `user/repo:QUANT` reference grammar already accepted for model management, storing the requested reference and, when it resolves, the resolved artifact. The requested reference SHALL record provider, repository, quant selector, and the raw string, and SHALL NOT be rewritten by resolution. The resolved artifact SHALL record the exact filename, the resolved revision, and the time of resolution, and SHALL be advisory. Binding SHALL resolve against the installed model library without contacting any network service. Binding a reference that matches no installed artifact SHALL be refused unless the operator explicitly allows a missing model.

#### Scenario: Bind an installed model
- **WHEN** an operator binds `qwen/Qwen3-4B-GGUF:Q4_K_M` and one installed artifact matches
- **THEN** Coquena stores the requested reference and the resolved exact filename and revision
- **AND** it issues no network request

#### Scenario: Refuse an unresolvable reference
- **WHEN** an operator binds a reference matching no installed artifact and does not allow a missing model
- **THEN** Coquena refuses the binding with a diagnostic naming the reference
- **AND** the preset's existing model binding is unchanged

#### Scenario: Refuse an ambiguous reference
- **WHEN** a quant selector matches more than one installed artifact
- **THEN** Coquena refuses the binding and reports how many artifacts matched

#### Scenario: Bind a model that is not installed
- **WHEN** an operator binds a reference and explicitly allows a missing model
- **THEN** Coquena stores the requested reference with no resolved artifact
- **AND** reads report the model as not installed

#### Scenario: Correct a mistaken binding
- **WHEN** an operator binds a model on a preset that already has one
- **THEN** the new binding replaces the previous one
- **AND** the preset's authored values are unchanged

#### Scenario: Report a stale resolution
- **WHEN** a preset's resolved filename no longer exists in the library
- **THEN** reads report the binding as stale with a diagnostic
- **AND** the requested reference is unchanged
- **AND** re-binding the same reference re-resolves it

### Requirement: Constrain a preset to admitting runtimes
Coquena SHALL let a preset declare the runtimes that can run it as a capability constraint of provider, backend, and a release range, and SHALL alternatively accept an explicit installed-runtime identity as a hard pin that excludes the capability constraint. A preset with no declared constraint SHALL be treated as portable. Coquena SHALL additionally compute the release range implied by the availability of the fields the preset uses, attributing each computed bound to the field that produced it. Coquena SHALL further compute the runtime capability implied by the preset's bound model — its declared quantization file type, its architecture, and its use of hybrid attention/SSM keys — and SHALL report it as a distinct computed contribution attributed to the model rather than to a field. Both the declared and the computed constraint SHALL be advisory and SHALL NOT block authoring, storing, renaming, or removing a preset, and SHALL NOT select or launch a runtime.

#### Scenario: Declare a capability constraint
- **WHEN** an operator constrains a preset to the `cuda@12.4` backend from release `b6200`
- **THEN** Coquena stores the declared constraint
- **AND** reads report it separately from the computed constraint

#### Scenario: Compute the implied release range
- **WHEN** a preset sets a field first admitted in release `b6180`
- **THEN** reads report a computed lower bound of `b6180`
- **AND** they attribute that bound to that field

#### Scenario: Compute a runtime capability implied by the bound model
- **WHEN** a preset is bound to a model declaring an exotic quantization file type or hybrid attention/SSM keys
- **THEN** reads report the implied runtime capability as a computed contribution attributed to the model
- **AND** it is reported separately from the bounds attributed to fields

#### Scenario: Report no model-implied capability for an ordinary model
- **WHEN** a preset is bound to a model declaring an ordinary quantization and no hybrid attention/SSM keys
- **THEN** reads report no model-attributed capability contribution
- **AND** the preset remains as portable as its fields allow

#### Scenario: Evaluate a preset against a runtime
- **WHEN** a preset is evaluated against an installed runtime
- **THEN** Coquena reports `admitted`, `not-admitted`, or `unknown`

#### Scenario: Author a preset no installed runtime admits
- **WHEN** an operator sets a value whose field no installed runtime admits
- **THEN** Coquena stores the value
- **AND** it reports the unmet constraint as a diagnostic

#### Scenario: Remove a constraint
- **WHEN** an operator removes a preset's runtime constraint
- **THEN** the preset is reported as portable
- **AND** its computed constraint is still reported

#### Scenario: Refuse a pin combined with a capability constraint
- **WHEN** an operator declares both an explicit runtime identity and a backend or release bound
- **THEN** Coquena refuses the constraint as contradictory

### Requirement: Validate preset values against the field catalog
Coquena SHALL validate every authored preset value against the kind of the field it names in the persisted field catalog before storing it, refusing an unknown field name and a value the field's kind does not admit. Coquena SHALL resolve a field named by a short-argument or environment-variable alias to its canonical field and store it under the canonical name. When a field declares a parent prerequisite that the preset does not satisfy, Coquena SHALL store the value and report an advisory diagnostic.

#### Scenario: Store a valid value
- **WHEN** an operator sets the integer field `context-size` to `8192`
- **THEN** Coquena stores the value under the canonical field name

#### Scenario: Reject a value of the wrong kind
- **WHEN** an operator sets an integer field to a non-integer value
- **THEN** Coquena refuses the value with a diagnostic naming the field and its kind
- **AND** no value is stored

#### Scenario: Reject an unknown field
- **WHEN** an operator sets a field absent from the catalog
- **THEN** Coquena refuses the value with a diagnostic naming the field

#### Scenario: Reject an option outside an enum
- **WHEN** an operator sets an enum field to a value outside its options
- **THEN** Coquena refuses the value and reports the admitted options

#### Scenario: Store a value through an alias
- **WHEN** an operator sets a value naming a field by its short-argument alias
- **THEN** Coquena stores it under the canonical field name

#### Scenario: Warn on an unsatisfied parent
- **WHEN** an operator sets a field whose parent prerequisite is not satisfied in that preset
- **THEN** Coquena stores the value
- **AND** it reports an advisory diagnostic naming the parent

### Requirement: Report effective values with their inheritance source
Coquena SHALL report a preset's values as the effective merge of the global preset and that preset's own layer, labeling each value as owned or inherited. Authoring a value SHALL affect only the addressed preset's own layer. Coquena SHALL support removing a value from a preset's own layer so that the global value is inherited again. Inheritance SHALL have exactly these two layers, and a field absent from both SHALL NOT be reported as a value.

#### Scenario: Inherit a global value
- **WHEN** the global preset sets `temp` and a model preset does not
- **THEN** reading that model preset reports `temp` as inherited from global

#### Scenario: Override a global value
- **WHEN** a model preset sets a field the global preset also sets
- **THEN** reading it reports the preset's own value labeled as owned
- **AND** the global value is unchanged

#### Scenario: Return to inheritance
- **WHEN** an operator removes an overriding value from a model preset
- **THEN** reading it reports the global value labeled as inherited

#### Scenario: Omit an unauthored field
- **WHEN** neither the global preset nor a model preset sets a catalog field
- **THEN** reading that preset does not report a value for that field

#### Scenario: Read the global preset alone
- **WHEN** an operator reads `global`
- **THEN** every reported value is owned
- **AND** no value is reported as inherited

### Requirement: Manage presets through the CLI
Coquena SHALL provide a `coquena preset` command family to define, rename, remove, list, and show presets, to set and unset values on a preset or on `global`, to bind a model, to bind and unbind a runtime constraint, and to render the INI for a runtime. `render` SHALL report the INI on standard output and SHALL write a file only when `--write` is given, taking an optional path. Human-readable output SHALL report each effective value with its inheritance source, and SHALL report the model binding and both the declared and computed runtime constraints without requiring JSON output. Every command SHALL also offer JSON output.

#### Scenario: Define and populate a preset
- **WHEN** an operator defines a preset with a model reference and then sets a value on it
- **THEN** both the binding and the value are persisted

#### Scenario: Show a preset
- **WHEN** an operator shows a preset
- **THEN** the output reports its name, title, model binding, declared and computed runtime constraints, and every effective value labeled owned or inherited

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

### Requirement: Manage presets through the local HTTP API
Coquena SHALL serve presets at `/api/presets` following the established `/api/<noun>` convention, with one endpoint per CLI mutator: `GET` and `POST` on the collection; `GET` and `DELETE` on `/api/presets/<name>`; `MOVE` on `/api/presets/<name>` with a destination naming the new name; `PUT` on the `title`, `model`, and `runtime` subresources; `DELETE` on the `runtime` subresource; and `PUT` and `DELETE` on `/api/presets/<name>/values/<field>`. The collection response SHALL report the global preset separately from the model presets with a kind discriminator. Coquena SHALL NOT serve a whole-entity `PUT` on `/api/presets/<name>` and SHALL NOT serve `GET` on a single value. Requests SHALL be decoded strictly, rejecting unknown members. Advisory conditions SHALL be reported as diagnostics in a successful response; only invalid requests SHALL fail.

#### Scenario: Read the collection
- **WHEN** a client requests the preset collection
- **THEN** the response reports the global preset and the model presets separately, each with its kind

#### Scenario: Read one preset
- **WHEN** a client requests a preset by name
- **THEN** the response reports its bindings and its effective values with their inheritance source

#### Scenario: Refuse a duplicate definition
- **WHEN** a client posts a preset whose name exists
- **THEN** the response reports a conflict
- **AND** the existing preset is unchanged

#### Scenario: Rename through MOVE
- **WHEN** a client sends `MOVE` for a preset with a destination naming an unused valid name
- **THEN** the preset becomes addressable at the destination with its values intact
- **AND** the response reports the new location

#### Scenario: Refuse a MOVE onto an existing preset
- **WHEN** a client sends `MOVE` with a destination naming an existing preset
- **THEN** the response reports a conflict
- **AND** both presets are unchanged

#### Scenario: Write only the addressed layer
- **WHEN** a client puts a value on a model preset
- **THEN** only that preset's own layer changes
- **AND** the global preset is unchanged

#### Scenario: Return to inheritance through DELETE
- **WHEN** a client deletes a value from a model preset
- **THEN** later reads report the global value as inherited

#### Scenario: Report an advisory condition as success
- **WHEN** a client stores a value whose field no installed runtime admits
- **THEN** the response succeeds and carries a diagnostic

#### Scenario: Reject an unsupported operation
- **WHEN** a client puts to `/api/presets/<name>` or gets a single value
- **THEN** the response reports the method as not allowed

#### Scenario: Reject a malformed request
- **WHEN** a client sends a body with unknown members
- **THEN** the response reports an invalid request
- **AND** the persisted document is unchanged

#### Scenario: Address an unknown preset
- **WHEN** a client addresses a preset that does not exist
- **THEN** the response reports it as not found

#### Scenario: Refuse an operation that cannot apply to global
- **WHEN** a client binds a model on `global` or deletes `global`
- **THEN** the response reports an invalid request
- **AND** the global values are unchanged

### Requirement: Render a deterministic models-preset INI
Coquena SHALL render a `--models-preset` INI for a named runtime from the persisted presets and the persisted field catalog. Rendering SHALL report the INI without writing any file by default. Coquena SHALL write the rendering only when the operator explicitly requests it, to an operator-supplied path when one is given and otherwise to `data/servers/<runtime-id>.ini`, and SHALL NOT write a rendering beneath `config/`. The HTTP rendering SHALL never write a file. The rendering SHALL emit the global preset as the `[*]` section and one section per preset whose runtime constraint admits that runtime, omitting non-admitted presets with a diagnostic naming the unmet constraint. Every key SHALL be the canonical long field name without leading dashes, never a short-argument or environment-variable alias. A preset with a resolved model SHALL emit the artifact path; a preset whose model is not installed SHALL emit the requested `user/repo:QUANT` reference. Coquena SHALL emit every authored value of an admitted preset and SHALL NOT emit a field that no preset authored. Rendering SHALL be a pure function of the persisted documents and the named runtime identity, SHALL issue no network request, and SHALL produce byte-identical output for identical inputs.

#### Scenario: Render global and model sections
- **WHEN** an operator renders the INI for an installed runtime
- **THEN** the output contains a `[*]` section holding the global values
- **AND** one section per admitted preset named by that preset's name

#### Scenario: Render without writing a file
- **WHEN** an operator renders the INI without requesting that it be written
- **THEN** Coquena reports the rendering
- **AND** it creates and modifies no file

#### Scenario: Write a rendering to the default path
- **WHEN** an operator renders the INI for a runtime and requests that it be written without supplying a path
- **THEN** Coquena writes it to `data/servers/<runtime-id>.ini`
- **AND** it reports the path it wrote

#### Scenario: Write a rendering to a supplied path
- **WHEN** an operator requests that a rendering be written to a path they supply
- **THEN** Coquena writes it there
- **AND** it writes nothing beneath `data/servers/`

#### Scenario: Read a rendering over HTTP without writing
- **WHEN** a client requests the rendering for a runtime over HTTP
- **THEN** the response carries the INI as text
- **AND** no file is created or modified

#### Scenario: Emit only canonical key names
- **WHEN** a preset holds a value authored through an alias
- **THEN** the rendered key is the canonical long field name without leading dashes

#### Scenario: Emit an installed model
- **WHEN** a rendered preset has a resolved model
- **THEN** its section references the artifact path

#### Scenario: Emit a model that is not installed
- **WHEN** a rendered preset has no resolved model
- **THEN** its section references the requested `user/repo:QUANT` reference

#### Scenario: Omit a preset the runtime does not admit
- **WHEN** a preset's runtime constraint excludes the named runtime
- **THEN** the rendered INI omits that preset's section
- **AND** the rendering reports a diagnostic naming the unmet constraint

#### Scenario: Flatten inherited values
- **WHEN** a preset inherits a global value
- **THEN** the rendered preset section does not repeat that value
- **AND** the value appears only in the `[*]` section

#### Scenario: Render deterministically
- **WHEN** the same presets and catalog are rendered twice for the same runtime
- **THEN** both renderings are byte-identical

#### Scenario: Render without the runtime installed
- **WHEN** an operator renders the INI for a runtime identity that is not installed
- **THEN** Coquena produces the rendering
- **AND** it reports that the runtime is not installed as a diagnostic

### Requirement: Assign a companion role within a preset
Coquena SHALL let a preset assign a companion role — `draft` or `projector` — to a model reference alongside its principal binding, storing the assignment as a criterion evaluated against installed artifacts rather than a pin to one file. Coquena SHALL refuse a companion assignment that does not satisfy the role's compatibility criterion against the preset's principal, unless the operator explicitly overrides the refusal. A companion assignment SHALL be advisory and SHALL NOT launch or configure a runtime.

#### Scenario: Assign a compatible projector
- **WHEN** an operator assigns a projector whose projection dimension matches the principal's embedding length
- **THEN** Coquena stores the companion assignment

#### Scenario: Refuse an incompatible companion
- **WHEN** an operator assigns a companion that fails its role's compatibility criterion
- **THEN** Coquena refuses the assignment with a diagnostic naming the criterion that failed
- **AND** the preset's existing companion assignment is unchanged

#### Scenario: Override a refused companion deliberately
- **WHEN** an operator assigns a companion failing its criterion and explicitly overrides the refusal
- **THEN** Coquena stores the assignment
- **AND** reads report the unmet criterion as a diagnostic

#### Scenario: Report several admissible companions
- **WHEN** more than one installed artifact satisfies a preset's companion criterion
- **THEN** reads report each admissible artifact as an option
- **AND** Coquena does not select one automatically

