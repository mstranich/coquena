# GGUF tensor names are canonical, and are not the Hugging Face names

**Kind:** finding · **State:** current · **Verified:** 2026-07-30

## The fact

Tensor names inside a GGUF file are normalised during conversion. They do not
vary by architecture the way the source checkpoint's names do.
`convert_hf_to_gguf.py` maps every architecture through
`gguf-py/gguf/constants.py::TENSOR_NAMES`, which defines:

```text
token_embd            output              output_norm
blk.{bid}.*           per-layer tensors, every architecture
enc.blk.{bid}.*       encoder layers
dec.blk.{bid}.*       decoder layers
v.blk.{bid}.*         vision encoder layers   (multimodal projectors)
a.blk.{bid}.*         audio encoder layers    (multimodal projectors)
mm.*                  the projection head
```

`model.embed_tokens.weight` is a **safetensors** name. It never appears in a
GGUF file; the equivalent is `token_embd`.

## Why it matters

It is routinely asserted that reasoning over tensor names is fragile because
names differ per architecture. That is true of Hugging Face checkpoints and
false of GGUF, and the conflation was inherited across several analyses before
being checked.

Because the names are canonical, name-based reasoning is *safe* inside GGUF, and
that unlocks the only reliable way to identify a model's base encoding: restrict
the computation to the repeating block tensors and exclude `token_embd`,
`output` and other non-block tensors — precisely the ones a quantizer recipe
promotes to higher precision.

## Consequences

- Prefix matching must be **anchored** (`HasPrefix`), never substring: the name
  `v.blk.0.attn_q` *contains* `blk.`, so a `Contains` implementation silently
  collapses the vision scope into the language-model scope and returns a
  plausible wrong answer on every multimodal artifact.
- A projector has no `blk.*` tensors at all. Any canonical scope that omits
  `v.blk.` and `a.blk.` cannot describe a projector.

## Source

`gguf-py/gguf/constants.py` at the reviewed revision.
