# Some recipes cannot be produced without an importance matrix

**Kind:** finding · **State:** current · **Verified:** 2026-07-30

## The fact

`llama-quantize` **aborts** rather than degrading when a very-low-bit recipe is
requested without an importance matrix:

```cpp
if (!imatrix && tm.requires_imatrix) {
    LLAMA_LOG_ERROR("Missing importance matrix for tensor %s in a very low-bit quantization\n", ...);
    LLAMA_LOG_ERROR("The result will be garbage, so bailing out\n");
    throw std::runtime_error(...);
}
```

`tensor_requires_imatrix()` returns true for `IQ3_XXS`, `IQ2_XXS`, `IQ2_XS`,
`IQ2_S`, `IQ1_M`, `IQ1_S`, and for `Q2_K` when the file type is `Q2_K_S`.

Source: `src/llama-quant.cpp` at the reviewed revision.

## Why it matters

Quantization identity is a structural property and does not depend on
calibration: an imatrix changes the *values* chosen, not the layout, the block
sizes or the declared recipe. Two files differing only in calibration share a
quantization identity. That principle holds and this finding does not weaken it.

But the dependency is **asymmetric**:

```text
identity does not depend on calibration        the principle
existence of certain identities implies it     this finding
```

For those seven recipes, an importance matrix was necessarily used, whatever the
filename says or omits — a fact about calibration derived from *structure*,
which is more reliable than the `-imatrix` token a publisher may or may not put
in a filename.

## Scope

This is a property of the reviewed quantizer at the reviewed revision, not a
universal historical claim about every producer that has ever written a GGUF.
The defensible statement is "the current llama.cpp quantizer cannot produce this
recipe without an importance matrix" — a capability and provenance statement,
which must stay out of quantization identity and out of any selector grammar.
