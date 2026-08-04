## MODIFIED Requirements

### Requirement: Persist domain-correct quantization mappings

Coquena SHALL represent GGUF model recipe values and tensor encoding values as
distinct Go types. The mapping used for `general.file_type` SHALL be indexed by
the `llama_ftype` recipe domain, while tensor descriptor types SHALL use a
separate `ggml_type` mapping. Domain-named lookup functions SHALL accept only
their corresponding type and SHALL return an explicit unknown result for an
unrecognized value. Deprecated recipe names MAY remain available as clean names
without a `(removed)` suffix, but a tensor-encoding value SHALL NOT be resolved
through the model-recipe mapping or vice versa.

#### Scenario: Decode a declared Q4_K_M recipe

- **WHEN** a GGUF declares `general.file_type = 15`
- **THEN** Coquena resolves the value in the model-recipe domain as `Q4_K_M`
- **AND** it does not render the overlapping tensor-encoding name `Q8_K`

#### Scenario: Decode a tensor type independently

- **WHEN** a tensor descriptor declares physical type value `15`
- **THEN** Coquena resolves the value in the tensor-encoding domain
- **AND** that result does not become the artifact's declared model recipe

#### Scenario: Reject an implicit cross-domain lookup

- **WHEN** code needs to render a recipe or tensor encoding
- **THEN** it must call the lookup for the corresponding typed domain
- **AND** no generic integer file-type lookup silently chooses a domain

### Requirement: Use declared recipe evidence to build the QUANT column

Coquena SHALL build the model `QUANT` display from the recognized recipe
declared by `general.file_type` when that declaration is available. Physical
tensor composition SHALL remain separate diagnostic evidence and SHALL NOT be
used to invent recipe suffixes such as `_M`, `_S`, or `_L`. A missing or
unrecognized declaration SHALL produce an explicit unknown or inferred-physical
presentation rather than asserting a declared recipe.

#### Scenario: Display the declared model recipe

- **WHEN** an installed model's derived evidence records a recognized declared
  recipe of `Q4_K_M`
- **THEN** `coquena model list` shows `Q4_K_M` in the model's QUANT column
- **AND** a physically dominant `Q4_K` tensor encoding does not replace it

#### Scenario: Avoid fabricating a suffix

- **WHEN** a model has no usable recipe declaration and its structural tensors
  are physically dominated by `Q4_K`
- **THEN** Coquena may report `Q4_K` as inferred physical evidence
- **AND** it does not report `Q4_K_M`, `Q4_K_S`, or another recipe suffix

#### Scenario: Display a projector quantization

- **WHEN** a projector has architecture `gemma4` and a recognized declared or
  explicitly physical quantization of `Q4_0`
- **THEN** the QUANT presentation remains `gemma4:Q4_0`
- **AND** the architecture prefix does not change the quantization evidence
