# filetype-quantization-names Specification

## Purpose
Normalize quantization names in logical model references, making them independent of the physical filename. The logical name `<user>/<repo>:<quant>` serves as the primary key in the library, while the filename visualizes the specific variant (with or without imatrix, with or without calibration, etc.).

## Requirements
### Requirement: Persist fileType→nombre legible mapping in Go constant

Coquena SHALL store the fileType→nombre legible mapping in `internal/model/filetypes.go` as a `map[int]string`. The mapping SHALL be derived from `enum ggml_type` in `ggml/include/ggml.h` of llama.cpp. Each entry SHALL include types that are marked as "support has been removed" in the source, with descriptive names like "removed" (e.g., `Q4_2 (removed)`).

#### Scenario: Read fileType from sidecar JSON

- **WHEN** Coquena reads a sidecar JSON with `"fileType": 15` (integer)
- **THEN** it looks up the name in the `fileTypeNames` map
- **AND** it returns `"Q8_K"` as the nombre legible

#### Scenario: Include removed types with descriptive names

- **WHEN** Coquena encounters a fileType that was removed from llama.cpp support
- **THEN** it displays the name with "(removed)" suffix
- **AND** the operator can identify that the type is no longer supported

### Requirement: Use mapping to build REFERENCE column in model list table

Coquena SHALL use the `fileTypeNames` mapping to build the REFERENCE column in the `coquena model list` table. The REFERENCE column SHALL have the format `<user>/<repo>:<quant>`, where `<quant>` is the nombre legible obtained from the mapping.

#### Scenario: Build REFERENCE column with correct format

- **WHEN** Coquena displays the `coquena model list` table
- **THEN** each row shows a REFERENCE column with format `<user>/<repo>:<quant>`
- **AND** the example is `deepreinforce-ai/Ornith-1.0-9B-GGUF:Q4_K_M`

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

