# `general.file_type` is `llama_ftype`, not `ggml_type`

**Kind:** finding · **State:** current · **Verified:** 2026-07-30

## The fact

The GGUF metadata key `general.file_type` holds a value from `enum llama_ftype`
(`include/llama.h`). Each tensor descriptor separately holds a value from
`enum ggml_type` (`ggml/include/ggml.h`). They are different enumerations that
both use small integers, so the same number means different things:

```text
(llama_ftype) 15 -> Q4_K_M      a quantizer recipe
(ggml_type)   15 -> Q8_K        a tensor encoding
```

## Proof that needs no version comparison

`ggml_type` has never contained an enumerator with an `_S`, `_M` or `_L` suffix,
in any revision. Those suffixes name quantizer *recipes*, and a recipe is not a
tensor encoding. The documented value list for `general.file_type` contains
`MOSTLY_Q3_K_S = 11` and `MOSTLY_Q4_K_M = 15`. A field whose own value list
holds identifiers absent from `ggml_type` by construction cannot be in the
`ggml_type` domain.

The naming corroborates it: `ggml_type` enumerators are prefixed `GGML_TYPE_`,
`llama_ftype` enumerators `LLAMA_FTYPE_MOSTLY_`. The spec's list uses `MOSTLY_`.

## A structural check that needs no upstream access

`Q8_K`, `Q8_1`, `I8`–`I64` and `F64` are not file-level recipes: no
`llama_ftype` enumerator names them and no `llama-quantize` invocation produces
them. A table indexed by `general.file_type` that contains them was built from
the tensor-encoding enum. That is a one-pass check on any implementation.

## Sources

- `include/llama.h` — `enum llama_ftype`, normative for the table
- `ggml/docs/gguf.md` — establishes the domain; **stale** for the values: it
  ends at `MOSTLY_Q6_K = 18` and omits every `IQ*`, `TQ*`, `MXFP4`, `NVFP4`,
  `Q1_0` and `Q2_0`

Use the specification to establish which domain applies and the pinned header
for the table itself.

## Why it matters

Reading this field through the wrong enum produces plausible, confidently wrong
names — `Q4_K_M` rendered as `Q8_K` — that survive review because nothing looks
broken. This was the defect that started the investigation, and it had been
recorded as settled knowledge in the opposite direction before being corrected.

## Consequences

- The two domains need distinct types in code, not a shared integer.
- Removed enumerators (`4`, `5`, `6`, `33`, `34`, `35` in the recipe domain) are
  still decodable and should be treated consistently.

## Considered and rejected

Completing the removed range — adding `4`, `Q4_1_SOME_F16`, which a decoder
omits while carrying `5`, `6`, `33`, `34` and `35` — was proposed and **rejected
on 2026-08-05**. Whether any real artifact declares it was never established,
and `Q4_1_SOME_F16` belongs to the GGML era that preceded the GGUF format, so a
GGUF file declaring it is unlikely to exist. The inconsistency is cosmetic and
serves no step of the `model-acquisition` story.

Revisit with evidence: a real artifact whose `general.file_type` is 4.
