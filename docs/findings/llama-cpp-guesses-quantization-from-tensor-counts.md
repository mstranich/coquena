# llama.cpp guesses quantization from tensor counts and invents the `_M` suffix

**Kind:** finding · **State:** current · **Verified:** 2026-07-30

## The fact

When `general.file_type` is absent, `src/llama-model-loader.cpp` derives one.
It does three things worth knowing:

**It aggregates by tensor count**, not by parameters and not by bytes:

```cpp
std::map<enum ggml_type, uint32_t> n_type;
for (const auto & it : weights_map) {
    n_type[it.second.tensor->type]++;
    if (n_type_max < n_type[type]) { n_type_max = n_type[type]; type_max = type; }
}
```

**It synthesizes a suffix** that no evidence supports:

```cpp
case GGML_TYPE_Q3_K: ftype = LLAMA_FTYPE_MOSTLY_Q3_K_M;  break;
case GGML_TYPE_Q4_K: ftype = LLAMA_FTYPE_MOSTLY_Q4_K_M;  break;
case GGML_TYPE_Q5_K: ftype = LLAMA_FTYPE_MOSTLY_Q5_K_M;  break;
```

**It labels the result as a guess**: `ftype |= LLAMA_FTYPE_GUESSED`, where
`LLAMA_FTYPE_GUESSED = 1024 = 1 << 10`. That is a **bitmask flag, not a sentinel
value** — a guessed `Q4_K_M` is `15 | 1024 = 1039`.

## Why it matters

Two opposite lessons at once.

Upstream already models epistemic provenance: it distinguishes a declared recipe
from an inferred one and says so in the value. Any consumer that reads the field
must clear bit 10 before looking the recipe up, or a perfectly ordinary guessed
value reads as an unknown format.

But the suffix upstream produces is a presentational default, not evidence. It
is hardcoded for exactly three inputs and asserts a recipe nobody recorded. A
consumer that masks the bit and keeps the name launders an invention into a
claim — and does so in the one branch where it is *certain* to be invented.

## Consequences

- Clear `LLAMA_FTYPE_GUESSED` before any recipe lookup.
- Treat a value carrying that bit as **absent**, not as a weaker declaration,
  and re-infer locally rather than adopting the recorded guess.
- Expect a benign divergence: for a metadata-absent `Q4_K` file, llama.cpp will
  log `Q4_K_M (guessed)` where a consumer reporting only evidence says `Q4_K`.
- The asymmetry is why this is easy to miss: for `Q2_K`, `Q6_K`, `Q8_0`, `F16`,
  `BF16` and every `IQ*`/`TQ*`, no suffix is invented and masking is harmless.
  The defect only appears if you trace a K-quant.

## Source

`src/llama-model-loader.cpp` at the reviewed revision, plus `llama_ftype_name`
in the same file, which clears the bit and appends `(guessed)` when rendering.
