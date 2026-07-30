# filetype-quantization-names Specification

## Purpose
Normalize quantization names in logical model references, making them independent of the physical filename. The logical name `<user>/<repo>:<quant>` serves as the primary key in the library, while the filename visualizes the specific variant (with or without imatrix, with or without calibration, etc.).

## Requirements
### Requirement: Persist fileType→nombre legible mapping in Go constant

Coquena SHALL store the fileType→nombre legible mapping in `internal/model/filetypes.go` as two maps: `FileTypeNames` for types actively supported by llama.cpp, and `DeprecatedFileTypeNames` for types whose support has been removed from active development. `FileTypeNames` SHALL contain only currently supported quantization types with their clean names. `DeprecatedFileTypeNames` SHALL contain types removed from llama.cpp source with their clean names, WITHOUT a "(removed)" suffix. A package-level lookup function SHALL resolve a fileType integer by checking `FileTypeNames` first, then `DeprecatedFileTypeNames`, returning "unknown" if neither map has an entry.

#### Scenario: Read fileType from sidecar JSON

- **WHEN** Coquena reads a sidecar JSON with `"fileType": 15` (integer)
- **THEN** it looks up the name via the package-level lookup function
- **AND** it returns `"Q8_K"` as the nombre legible

#### Scenario: Look up deprecated fileType without removed suffix

- **WHEN** Coquena encounters a fileType that was removed from llama.cpp active development (e.g., fileType 4)
- **THEN** it returns the clean name `"Q4_2"` via the lookup function
- **AND** the operator sees the quantization name without "(removed)" suffix

### Requirement: Use mapping to build QUANT column in model list table

Coquena SHALL use the package-level lookup function (checking `FileTypeNames` then `DeprecatedFileTypeNames`) to build the QUANT column in the `coquena model list` table. The QUANT column SHALL have the format `<quant>` for model artifacts and `<architecture>:<quant>` for projector companions, where `<quant>` is the nombre legible obtained from the lookup function. The lookup function SHALL return clean names without "(removed)" suffix for deprecated types.

#### Scenario: Build QUANT column with correct format

- **WHEN** Coquena displays the `coquena model list` table
- **THEN** each row shows a QUANT column with the quantization name
- **AND** the example is `Q4_K_M` for a model or `gemma4:Q4_0` for a projector

#### Scenario: Display deprecated quantization without removed suffix

- **WHEN** Coquena displays the `coquena model list` table for an artifact with a deprecated fileType
- **THEN** the QUANT column shows the clean quantization name (e.g., `Q4_2`)
- **AND** the operator sees the quantization identity without "(removed)" suffix

### Requirement: Keep FILENAME column for variant visualization

Coquena SHALL keep the FILENAME column to visualize the specific variant (with or without imatrix, with or without calibration, etc.). The REFERENCE column serves as the primary key, while FILENAME provides visual context for the specific artifact.

#### Scenario: Table shows REFERENCE and FILENAME columns

- **WHEN** Coquena displays the `coquena model list` table
- **THEN** it shows REFERENCE and FILENAME columns
- **AND** the operator can see both the logical identity and the specific variant

### Requirement: Remove REPOSITORY column from visible table

Coquena SHALL remove the REPOSITORY column from the visible table, as it is redundant with the REFERENCE column.

#### Scenario: Table removes REPOSITORY column

- **WHEN** Coquena displays the `coquena model list` table
- **THEN** it no longer shows a REPOSITORY column
- **AND** the REFERENCE column provides sufficient context

