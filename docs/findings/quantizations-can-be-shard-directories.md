# A quantization can be a directory of shards, not a file

**Kind:** finding · **State:** current · **Verified:** 2026-08-05

## The fact

Large models are published as split GGUFs — `llama-gguf-split` produces
`<name>-00001-of-000NN.gguf` — and repositories organise them **by directory,
one per quantization**.

Verified against `unsloth/DeepSeek-R1-GGUF`: it contains **zero `.gguf` files at
the repository root**. Every quantization is a folder:

```text
DeepSeek-R1-BF16/        DeepSeek-R1-Q2_K/       DeepSeek-R1-Q2_K_L/
DeepSeek-R1-Q3_K_M/      DeepSeek-R1-Q4_K_M/     DeepSeek-R1-Q5_K_M/
DeepSeek-R1-Q6_K/        DeepSeek-R1-Q8_0/       DeepSeek-R1-UD-IQ1_S/
```

`DeepSeek-R1-Q4_K_M/` alone holds nine shards.

## Why it matters

This is not an occasional edge case; it is how the ecosystem publishes anything
large. Any design that treats "a quantization" as "a file" is wrong for an
entire class of models, and the ecosystem — not a design preference — is what
imposes the bundle as the unit.

It also separates two relationships that the word "file" hides:

```text
shard      a mandatory part of one quantization; useless alone
companion  shared across several quantizations of the same repository
```

A projector is complementary to one *or several* quantizations. A shard belongs
to exactly one and completes it.

## Consequences

- The addressable unit is the quantization, which may resolve to N files.
- Metadata lives in the first shard. Shards 2..N carry mostly tensor data, so
  classifying each shard independently yields N−1 unclassifiable artifacts and
  one real one.
- Repository paths are not flat: a sibling filename can contain `/`, and any
  path handling that assumes `<repo>/<filename>` misparses it.
- Size, and therefore the "how many GB" the story requires, is the sum over the
  set — never one file's size.
