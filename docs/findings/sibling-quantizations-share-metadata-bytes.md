# Two quantizations of one model share their metadata bytes

**Kind:** finding · **State:** current · **Verified:** 2026-07-28

## The fact

Quantizations built from the same source model carry an **identical key-value
metadata section**: the same `tokenizer.ggml.tokens`, the same merges, the same
architecture keys. Only the tensor data that follows differs.

On the Ornith artifact that section measured roughly **8 MB**.

## Why it matters

It rules out the obvious cheap integrity check. A digest computed over the first
N megabytes of a GGUF would be **identical for `Q4_0` and `Q8_0` of the same
model**, because the bytes that distinguish them are not in the window.

That is the exact opposite of what such a digest is for: it would report two
different quantizations as the same file.

It applies equally to any "has this changed?" shortcut that reads only the
header. The header is the part sibling artifacts have in common.

## Consequences

- A fast digest must include the **file size** and sample the **tail**, not only
  a leading window.
- Any re-derivation of quantization evidence must read the **tensor table**,
  never the header alone — the tensor descriptors are where the quantization
  actually differs.
- Remote comparison is unaffected and cannot use a partial digest anyway:
  Hugging Face publishes a hash of the complete file, so there is no partial
  remote value to compare against.

## Considered and rejected

A two-stage digest — a cheap sampled one beside the full SHA-256 — was proposed
to make `model verify` affordable to run routinely, and **rejected on
2026-08-05 as premature**. It optimises a command that appears at no step of the
`model-acquisition` story, and no evidence was gathered that verification cost
is an actual obstacle.

The constraint above is what made the optimisation dangerous and is why it is
recorded here rather than left implicit. Revisit only when an operator reports
verification cost as a real obstacle, and honour the sampling requirement above
if so.
