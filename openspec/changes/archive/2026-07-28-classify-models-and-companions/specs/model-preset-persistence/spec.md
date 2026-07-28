## MODIFIED Requirements

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

## ADDED Requirements

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
