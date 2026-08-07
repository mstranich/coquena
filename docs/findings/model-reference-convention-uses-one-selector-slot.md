# The ecosystem reference convention has one selector slot

**Kind:** finding · **State:** current · **Verified:** 2026-07-30

## The fact

Hugging Face's own documentation for running GGUF models defines the reference
grammar as a repository plus **one colon-delimited selector**, which may be
either a quantization tag or a full filename:

```sh
hf.co/{username}/{repository}:{quantization}

hf.co/bartowski/Llama-3.2-3B-Instruct-GGUF:IQ3_M
hf.co/bartowski/Llama-3.2-3B-Instruct-GGUF:iq3_m     # case-insensitive

# "you can also directly use the full filename as a tag"
hf.co/bartowski/Llama-3.2-3B-Instruct-GGUF:Llama-3.2-3B-Instruct-IQ3_M.gguf
```

Source: <https://huggingface.co/docs/hub/en/ollama>

Two facts follow directly. There is **no slash form** for addressing a file, and
there is **no variant grammar** — nothing resembling `:Q4_K_M#imatrix` or
`:Q4_K_M-imatrix` exists. The documented answer to "two files share one
quantization" is exactly one thing: use the full filename as the tag.

## Why it matters

The recipe tag and the exact filename share one namespace by design, with the
filename as the precise form. Treating the filename-in-the-colon-slot as an
inconsistency to be cleaned up moves *away* from the convention rather than
toward it.

The two forms differ in cardinality, not in punctuation:

```text
user/repo:Q4_K_M                  a set: every artifact with that structural quantization
user/repo:model-…-imatrix.gguf    exactly one artifact
```

Quantization identity is independent of calibration: two files differing only in
imatrix share a quantization and are correctly grouped by the tag form. That is
not a collision to be fixed; it is the equivalence relation working, and the
filename is the discriminator when exactly one is required.

## Consequences

- Revision pinning is an **orthogonal axis**, not another selector segment. It
  answers *when*, not *which*, and composes with either form.
- Nesting selectors (`:quant:filename`) is not the convention, adds no
  addressing power once a filename is present, and creates a surface where two
  assertions about one artifact can disagree.
- Inventing a variant namespace would put an implementation out of step with the
  only convention that exists.
