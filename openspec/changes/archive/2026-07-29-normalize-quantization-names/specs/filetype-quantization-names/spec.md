# filetype-quantization-names

## What

Mapeo fileType→nombre legible de cuantización extraído de `enum ggml_type` en `ggml/include/ggml.h` de llama.cpp. Cada nombre de cuantización es una constante, no derivable del nombre del archivo.

## Why

El nombre físico del archivo (ej. `ornith-1.0-9b-Q4_K_M.gguf`) no es un identificador de usuario. La cuantización es una constante que debe provenir de los metadatos, no del nombre del archivo (el caso `-imatrix` lo demuestra: `model-Q4_K_M.gguf` y `model-Q4_K_M-imatrix.gguf` comparten la misma cuantización).

## Requirements

- **REQ-1**: Agregar archivo constante Go `internal/model/filetypes.go` con el mapeo fileType→nombre legible.
- **REQ-2**: Usar el mapeo para construir la columna REFERENCE en la tabla visible de `coquena model list`.
- **REQ-3**: Eliminar las columnas REPOSITORY y FILENAME de la tabla visible.
- **REQ-4**: Incluir tipos eliminados (Q4_2, Q4_3, Q4_0_4_4, Q4_0_4_8, Q4_0_8_8, IQ4_NL_4_4, IQ4_NL_4_8, IQ4_NL_8_8) con nombres descriptivos como "removed".

## Constraints

- **CON-1**: No regenerar sidecars existentes (el campo `fileType` es suficiente).
- **CON-2**: No traducir nombres de cuantización (estándar de la industria en inglés).
- **CON-3**: Usar `map[int]string` con valores descriptivos para los tipos eliminados.

## Acceptance Criteria

- **AC-1**: `internal/model/filetypes.go` existe con el mapeo fileType→nombre legible.
- **AC-2**: `cmd/coquena/model.go` usa el mapeo para construir la columna REFERENCE.
- **AC-3**: La tabla visible de `coquena model list` muestra una columna REFERENCE con el formato `<user>/<repo>:<quant>`.
- **AC-4**: Las columnas REPOSITORY y FILENAME son eliminadas de la tabla visible.
- **AC-5**: Los tipos eliminados se incluyen con nombres descriptivos como "removed".

## ADDED Requirements

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

### Requirement: Remove REPOSITORY and FILENAME columns from visible table

Coquena SHALL remove the REPOSITORY and FILENAME columns from the visible table in `coquena model list`. The identity unificada `<user>/<repo>:<quant>` SHALL provide sufficient context for the operator.

#### Scenario: Table shows single REFERENCE column

- **WHEN** Coquena displays the `coquena model list` table
- **THEN** it shows a single REFERENCE column instead of REPOSITORY and FILENAME
- **AND** the operator no longer needs to see two separate columns

## REMOVED Requirements

### Requirement: Display REPOSITORY and FILENAME as separate columns

Coquena SHALL no longer display REPOSITORY and FILENAME as separate columns in the `coquena model list` table. The identity unificada `<user>/<repo>:<quant>` replaces these two columns with a single REFERENCE column.

## Data Model

```go
// internal/model/filetypes.go
var fileTypeNames = map[int]string{
    0:  "F32",
    1:  "F16",
    2:  "Q4_0",
    3:  "Q4_1",
    4:  "Q4_2 (removed)",
    5:  "Q4_3 (removed)",
    6:  "Q5_0",
    7:  "Q5_1",
    8:  "Q8_0",
    9:  "Q8_1",
    10: "Q2_K",
    11: "Q3_K",
    12: "Q4_K",
    13: "Q5_K",
    14: "Q6_K",
    15: "Q8_K",
    16: "IQ2_XXS",
    17: "IQ2_XS",
    18: "IQ3_XXS",
    19: "IQ1_S",
    20: "IQ4_NL",
    21: "IQ3_S",
    22: "IQ2_S",
    23: "IQ4_XS",
    24: "I8",
    25: "I16",
    26: "I32",
    27: "I64",
    28: "F64",
    29: "IQ1_M",
    30: "BF16",
    31: "Q4_0_4_4 (removed)",
    32: "Q4_0_4_8 (removed)",
    33: "Q4_0_8_8 (removed)",
    34: "TQ1_0",
    35: "TQ2_0",
    36: "IQ4_NL_4_4 (removed)",
    37: "IQ4_NL_4_8 (removed)",
    38: "IQ4_NL_8_8 (removed)",
    39: "MXFP4",
    40: "NVFP4",
    41: "Q1_0",
    42: "Q2_0",
}
```
