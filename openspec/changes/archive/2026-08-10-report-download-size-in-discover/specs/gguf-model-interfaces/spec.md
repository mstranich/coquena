## ADDED Requirements

### Requirement: Discovery reports quantization download cost
Coquena SHALL project the GGUF artifacts discovered in a remote repository into quantization entries suitable for comparison. Each quantization entry SHALL identify every constituent file and SHALL report the sum of their published sizes when every constituent size is known. Shared companions SHALL remain separate entries and SHALL NOT be counted in each quantization total. Human-readable and JSON discovery output SHALL expose the same grouping and size knowledge without changing what a subsequent download selects.

#### Scenario: Report a single-file quantization
- **WHEN** a repository publishes one GGUF file for a quantization with a known size
- **THEN** discovery reports one quantization containing that filename and its size as the total

#### Scenario: Sum a split quantization
- **WHEN** a quantization is published as multiple shards with known sizes
- **THEN** discovery reports one quantization entry containing every shard
- **AND** its total is the sum of all shard sizes rather than one row per shard

#### Scenario: Do not duplicate a shared companion cost
- **WHEN** discovery classifies an artifact as a companion that can be shared by multiple quantizations
- **THEN** it reports that artifact separately from the quantization totals
- **AND** no quantization total includes the companion size

#### Scenario: Preserve an unknown total
- **WHEN** any constituent file in a discovered quantization has no known positive size
- **THEN** discovery identifies the quantization total as unknown
- **AND** it does not present the sum of only the known files as the total

#### Scenario: Expose grouped discovery as JSON
- **WHEN** an operator requests JSON discovery output
- **THEN** each quantization includes its logical label, constituent artifact evidence, and either a known total size in bytes or an explicit unknown-size state
- **AND** existing per-candidate evidence remains available to API consumers

### Requirement: Discovery fetches the model card only on request
The default `coquena model discover` invocation SHALL present the compact quantization comparison without fetching or rendering the repository model card. The CLI SHALL accept an explicit `--model-card` option that fetches and renders the card in addition to the discovery result.

#### Scenario: Compare quantizations without a model-card request
- **WHEN** an operator invokes `coquena model discover <reference>` without `--model-card`
- **THEN** Coquena reports the grouped quantizations, files, and sizes
- **AND** it makes no request for the model card

#### Scenario: Request the model card explicitly
- **WHEN** an operator invokes `coquena model discover <reference> --model-card`
- **THEN** Coquena fetches and renders the repository model card in addition to the grouped discovery result

