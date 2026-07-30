# Spec: CountParameters — classification field, always computed

## Context

The GGUF format stores a tensor table (names, types, shapes, offsets) separate from the KV metadata. The current `Classification` struct doesn't include the parameter count, which is needed for sorting in the model list output.

## Current Behavior

`Classification` has fields like `FileType`, `Architecture`, `SizeLabel`, but no `CountParameters`. The parameter count is not computed or stored.

## Desired Behavior

Add `CountParameters` as a field in `Classification`, always computed during GGUF reading (download/reconcile) and stored in the sidecar.

## Requirements

### R1: Add CountParameters to Classification

```go
type Classification struct {
    Type             ArtifactType    `json:"type"`
    Architecture     string          `json:"architecture,omitempty"`
    SizeLabel        string          `json:"sizeLabel,omitempty"`
    FileType         *int64          `json:"fileType,omitempty"`
    CountParameters  int64           `json:"countParameters,omitempty"`  // NEW
    ContextLength    int64           `json:"contextLength,omitempty"`
    EmbeddingLength  int64           `json:"embeddingLength,omitempty"`
    ProjectionDim    int64           `json:"projectionDim,omitempty"`
    ProjectorType    string          `json:"projectorType,omitempty"`
    HasVisionEncoder bool            `json:"hasVisionEncoder,omitempty"`
    HasAudioEncoder  bool            `json:"hasAudioEncoder,omitempty"`
    SSM              bool            `json:"ssm"`
    Tokenizer        TokenizerDigest `json:"tokenizer"`
    Sampling         Sampling        `json:"sampling"`
}
```

### R2: Implement CountParameters function

- Read the tensor table from the GGUF binary
- Map tensor types to element sizes (Q4_0 = 0.5 bytes, Q8_0 = 1 byte, etc.)
- Calculate elements = product of shape dimensions
- Sum total bytes across all tensors
- Return `int64`

```go
func CountParameters(r RandomAccessReader) (int64, error) {
    // 1. Read n_tensors from header
    // 2. For each tensor:
    //    a. Read name, type, shape, offset
    //    b. Map type → element_size
    //    c. Calculate elements = product of shape
    //    d. Add to total
    // 3. Return total
}
```

### R3: Compute during classifyFile

- In `classifyFile` (called during `Publish` and `PublishInPlace`), compute `CountParameters`
- Store in `Classification.CountParameters`
- Even if computation fails, set to 0

### R4: Store in sidecar

- `CountParameters` is stored in the `.json` sidecar
- Read during listing from the sidecar (no need to re-read binary)

### R5: ggufTypeSize mapping

```go
var ggufTypeSize = map[uint32]float64{
    0: 4,    // F32
    1: 2,    // F16
    2: 0.5,  // Q4_0
    3: 0.625, // Q4_1
    4: 0.5625, // Q4_2
    5: 0.5,  // Q4_3
    6: 1,    // Q5_0
    7: 1,    // Q5_1
    8: 1.125, // Q8_0
    9: 1,    // Q8_1
    10: 1.25, // Q2_K
    11: 1.5, // Q3_K
    12: 1.59375, // Q4_K
    13: 2,   // Q5_K
    14: 2.5, // Q6_K
    15: 2,   // Q8_K
    16: 0.5, // IQ2_XXS
    17: 0.625, // IQ2_XS
    18: 0.75, // IQ3_XXS
    19: 0.875, // IQ1_S
    20: 1,   // IQ4_NL
    21: 1,   // IQ3_S
    22: 1,   // IQ2_S
    23: 1,   // IQ4_XS
    24: 4,   // I8
    25: 2,   // I16
    26: 4,   // I32
    27: 8,   // I64
    28: 8,   // F64
    29: 1,   // IQ1_M
    30: 2,   // BF16
    34: 1,   // TQ1_0
    35: 1,   // TQ2_0
    39: 1,   // MXFP4
    40: 1,   // NVFP4
    41: 0.5, // Q1_0
    42: 1,   // Q2_0
}
```

## Acceptance Criteria

- [ ] `Classification` has `CountParameters` field
- [ ] `CountParameters` is always computed during GGUF reading
- [ ] `CountParameters` is stored in sidecar
- [ ] `CountParameters` is read during listing
- [ ] Sorting uses `CountParameters` as tertiary criterion
