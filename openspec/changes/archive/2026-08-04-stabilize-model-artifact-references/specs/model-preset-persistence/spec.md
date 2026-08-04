## MODIFIED Requirements

### Requirement: Bind a preset to a model
Coquena SHALL bind a preset through the shared `user/repo[@revision]:selector` grammar, where a quantization selector is a logical class and an exact `.gguf` filename is a canonical artifact selector. The persisted requested reference SHALL retain the authored raw reference and its parsed provider, repository, optional revision, and selector without being rewritten by resolution. When resolution succeeds, the binding SHALL separately persist the resolved artifact's canonical exact-filename reference, filename, resolved revision, and resolution time.

Binding SHALL resolve against the installed library without network access. A logical selector matching multiple artifacts SHALL be refused as ambiguous and name their canonical references. A missing selector SHALL be refused unless the operator explicitly allows a missing model. Reads SHALL distinguish a binding whose resolved canonical artifact is unchanged, changed, missing, or accompanied by new ambiguity; the arrival of another artifact in the same quantization class SHALL NOT by itself mark the previously resolved artifact stale. Rebinding SHALL remain an explicit operator action and SHALL NOT silently replace the resolved canonical artifact.

#### Scenario: Bind a unique logical selector
- **WHEN** an operator binds `user/repo@abc123:Q4_K_M` and exactly one installed artifact at that revision matches
- **THEN** Coquena stores the authored logical selector and the resolved canonical exact-filename reference
- **AND** it issues no network request

#### Scenario: Bind an exact filename
- **WHEN** an operator binds `user/repo@abc123:model-Q4_K_M-imatrix.gguf`
- **THEN** Coquena stores that authored reference and resolves exactly that artifact
- **AND** it does not match another filename in the same quantization class

#### Scenario: Refuse an ambiguous logical selector
- **WHEN** a quantization selector matches multiple installed artifacts
- **THEN** Coquena refuses the binding and names every matching canonical artifact reference
- **AND** the preset's existing binding remains unchanged

#### Scenario: Bind a model that is not installed
- **WHEN** an operator explicitly allows a missing model reference
- **THEN** Coquena stores the authored reference with no resolved artifact
- **AND** reads report the model as missing rather than ambiguous or stale

#### Scenario: Refuse a missing model without explicit allowance
- **WHEN** an operator binds a reference matching no installed artifact without allowing a missing model
- **THEN** Coquena refuses with a diagnostic naming the reference
- **AND** the existing binding remains unchanged

#### Scenario: Correct a binding explicitly
- **WHEN** an operator binds a different valid reference on a preset that already has one
- **THEN** the new requested and resolved layers replace the previous binding
- **AND** the preset's authored values remain unchanged

#### Scenario: Preserve a valid binding when a sibling arrives
- **WHEN** a preset resolved to `user/repo@abc123:model-Q4_K_M.gguf` and another Q4_K_M filename is later installed
- **THEN** reads report the resolved canonical artifact as still present and report the authored selector as currently ambiguous
- **AND** they do not mark the resolved artifact stale or replace it

#### Scenario: Report a changed resolved artifact
- **WHEN** the canonical artifact recorded by a binding is no longer present but the authored selector resolves uniquely to a different artifact
- **THEN** reads report a resolved-different outcome
- **AND** rebinding is required before the replacement becomes the persisted canonical artifact

#### Scenario: Report a missing resolved artifact
- **WHEN** neither the binding's canonical artifact nor any artifact matching its authored selector is installed
- **THEN** reads report the binding as missing
- **AND** the authored reference remains unchanged
