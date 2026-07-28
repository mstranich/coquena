## Context

This change was designed against real artifacts rather than documentation. The
findings below were produced by reading actual files — two Hugging Face
repositories read remotely through Range requests, and the operator's own
locally installed models — using Coquena's own reader. They are the empirical
basis for every decision that follows.

### Classification is declared, not inferred

`general.architecture` describes the network architecture and requires
inferring a role from it. `general.type` declares the role outright. Both sit
in the first two key-value pairs of the header, so reading either costs the
same.

| Artifact | `general.architecture` | `general.type` |
| --- | --- | --- |
| `gemma-4-12B-it-QAT-Q4_0.gguf` | `gemma4` | `model` |
| `mmproj-gemma-4-12B-it-QAT-BF16.gguf` | `clip` | `mmproj` |
| `Ternary-Bonsai-27B-Q2_0.gguf` | `qwen35` | `model` |
| `Ternary-Bonsai-27B-mmproj-BF16.gguf` | `clip` | `mmproj` |

`Bonsai-27B` shows why the architecture is not a usable proxy for identity
either: it reports `qwen35`, not `bonsai`, because it is built on Qwen 3.5.

### Type and role are independent axes

A speculative draft model reports `general.type = model`. Nothing in its header
marks it as a draft — that is a role the operator assigns in a preset. The two
axes therefore cannot be collapsed:

- **Type** (intrinsic, auto-detected): `model` or `mmproj`.
- **Role** (assigned, with a computable admission criterion): `principal`,
  `draft`, or `projector`.

`projector` is the single case where type and role always coincide, so it is
the only role this change can assign on its own.

### Compatibility criteria are computable

**Projector to principal.** The linking key is `clip.vision.projection_dim`,
not `clip.vision.embedding_length`. The gemma-4 pair alone is ambiguous — both
keys read 3840, matching `gemma4.embedding_length` — but Bonsai disambiguates
it: `projection_dim` is 5120 and matches `qwen35.embedding_length`, while
`clip.vision.embedding_length` is 1152, the vision encoder's internal width,
which matches nothing in the principal.

The parser must tolerate key-shape variance. gemma-4 exposes
`clip.vision.projector_type`; Bonsai exposes `clip.projector_type`, without the
`vision` infix. Assuming a single spelling would silently fail on real files.

**Draft to target.** Speculative decoding works because the draft proposes
token IDs the target verifies. Different tokenizers give the same IDs different
meanings, so verification becomes invalid. The criterion is therefore tokenizer
family equality: `tokenizer.ggml.model`, `tokenizer.ggml.pre`, vocabulary size,
the special token IDs, and a digest of the vocabulary itself. Cardinality is
1:N inside a family and never crosses families. Both Bonsai variants share an
identical tokenizer, so they are mutually admissible.

**Model to runtime.** Bonsai demonstrates that a model implies a minimum
runtime capability through three independent, readable signals:

- `general.file_type` outside the usual low range (Bonsai reports 40 and 41,
  ternary and 1-bit; ordinary files report 2 or 32) — the build must support
  that quantization.
- `general.architecture` — the build must know the architecture.
- the presence of `<arch>.ssm.*` keys (Bonsai carries `conv_kernel`,
  `state_size`, `time_step_rank`, and others) — the build must support hybrid
  attention/SSM inference.

### The sidecar caches the wrong thing

Measured on a real installed artifact: a 5.3 GB `.gguf` beside a 13.2 MB
`.json`. Of the 8.0 MB of GGUF content inside it:

| Key | Bytes | Share |
| --- | --- | --- |
| `tokenizer.ggml.merges` | 3,856,004 | 48.3% |
| `tokenizer.ggml.tokens` | 3,619,676 | 45.4% |
| `tokenizer.ggml.token_type` | 496,641 | 6.2% |
| `tokenizer.chat_template` | 8,406 | 0.1% |
| all other 34 keys combined | 150 | 0.00% |

Every key that identifies, classifies, or constrains a model fits in 150 bytes.

## Goals / Non-Goals

**Goals.** A catalog where every artifact's type, role admissibility, and
companion relations are known and consistent, whether it arrived through a
Coquena download or was reconciled from disk after another application wrote
it. Classification must work identically for remote candidates and local files,
and must never require downloading a full artifact.

**Non-Goals.** Launching a runtime, applying metadata to preset fields,
rendering styled model cards, reconciling automatically, or migrating existing
sidecars.

## Decisions

### Compatibility is a criterion, not a pin

A companion binding stores the *criterion* a companion must satisfy, not a path
to a specific file. This mirrors `EvaluateRuntime`, which is a pure function
evaluating a pattern against candidates rather than a stored assignment.

The reason is cardinality: several installed artifacts can satisfy the same
criterion simultaneously, and the operator described companions as "perfectly
interchangeable" — correct, within their compatibility class. A hard pin would
also orphan the binding whenever the file is replaced.

The resulting UX follows the existing shape: a suggested-options selector with
a manual-value escape in the WebUI and TUI, and a CLI escape hatch flag to pass
validation deliberately, consistent with the existing `--allow-missing`.

### Raw metadata leaves the system

The sidecar stores provenance, artifact facts, and a compact classification
digest. It does not store the raw GGUF map, and no command or API returns raw
metadata as a bulk payload. When a value is needed it is read from the `.gguf`
on demand through the existing reader, which is what makes this safe: the file
itself is the source of truth and it is always available.

This removes `Record.GGUF` as persisted state and, with it, the entire
"summarize arrays longer than 256 entries" projection — that logic existed only
to hide data the sidecar should never have held. The existing requirement that
the inspection view render a two-column table rather than a raw JSON document
is unaffected and in fact reinforced.

The digest shape is:

```json
{
  "type": "model",
  "architecture": "gemma4",
  "sizeLabel": "12B",
  "fileType": 2,
  "contextLength": 262144,
  "embeddingLength": 3840,
  "ssm": false,
  "tokenizer": {
    "model": "gemma4",
    "pre": "",
    "vocabSize": 262144,
    "bos": 2, "eos": 1, "pad": 0, "unk": 3,
    "digest": "sha256:…"
  },
  "sampling": { "temp": 1, "topK": 64, "topP": 0.95 }
}
```

An `mmproj` digest carries `projectionDim`, `hasVisionEncoder`, and
`hasAudioEncoder` in place of the principal-only fields.

The tokenizer digest is the point where two problems collapse into one. It is
simultaneously what makes the sidecar small — 32 bytes replacing 8 MB of
vocabulary arrays — and exactly the evidence the draft-to-target criterion
needs. Comparing digests is an exact comparison, strictly better than comparing
248,320 tokens element by element.

### Schema version bump instead of a migration

Raising the sidecar schema version makes every previously written sidecar
invalid. That is deliberate. `Manager.Library()` already reports a `.gguf`
whose sidecar fails to read as `unreconciled`, and the specification already
states that an unreconciled entry is never resolvable for binding. A model
carrying an old sidecar therefore lands in exactly the state the catalog
already has a defined recovery path for, and `reconcile --all` restores it.

This reuses one mechanism instead of adding a migration path the project does
not need. The project is pre-release, so no compatibility guarantee is owed.

### Bulk reconciliation is where classification happens

Reconciling in bulk necessarily means reading every header to analyse
relations, so classification is not a separate step layered on reconciliation —
it is what bulk reconciliation *does*. One pass over the directory reads each
header, classifies each artifact, computes the relations among them, and
persists each digest.

Adoption of unreconciled files without reconciliation was considered and
rejected by definition and by design. `ResolveInstalled` keeps excluding any
state other than `installed`; an unreconciled file is not a catalog citizen.
Mandatory reconciliation is not introduced here — it is already specified — and
this change only extends it to a bulk mode.

### Inspection splits by locality

`inspect` becomes local-only and finally calls `Manager.InspectLocal`, which
has existed since the model manager was written and has never had a caller
anywhere in the product. Remote exploration moves to `discover`, which always
lists every candidate in the repository with its classification — the selector
only marks which one *would* be chosen — and prints the model card so the
operator can read what the publisher documented before downloading.

This is an observable behavior change to an existing command, accepted for the
same pre-release reason as the schema bump.

### Diagnostics carry candidates, not counts

Both `resolveRepository` and `ResolveInstalled` currently report how many
artifacts matched and discard which ones. `ErrAmbiguousInstalledMatch` stores a
`Count` field and nothing more. The CLI cannot list filenames it was never
given, so the fix belongs in the error types, which gain the matching
candidates; the CLI then prints them.

## Risks / Trade-offs

**This change is larger than the project's usual baby step.** The project rules
prefer the smallest independently reviewable behavior, and this change spans
classification, compatibility, the sidecar format, four CLI commands, and the
HTTP API. The scope was chosen deliberately: the pieces are coupled through a
single artifact — the classification digest is simultaneously the sidecar fix,
the draft compatibility criterion, and the catalog's unit of knowledge — so
splitting them would mean writing the same structure two or three times and
breaking the sidecar format more than once. The task breakdown keeps each step
independently verifiable to compensate.

**The runtime constraint sits close to an existing prohibition.** The library
specification states that Coquena "SHALL NOT infer llama.cpp compatibility",
and deriving a runtime constraint from model metadata reads as exactly that.
The distinction this change draws, and writes into the specification, is
between inferring compatibility in order to *assign* values to runtime or
preset fields — which stays prohibited — and computing an advisory constraint
*displayed* to the operator, which is what a control plane for several runtimes
requires in order to answer whether an installed build can load a given model.
Nothing is applied, selected, or launched.

**Publisher sampling recommendations stay advisory.** Real files carry them —
gemma-4 declares `top_k` 64, Bonsai declares 20 — and they are genuinely
useful, which is precisely why the temptation to apply them must be refused.
They are displayed as suggestions and never written to a preset.

**Key-shape variance is a durable hazard.** Two repositories already disagree
on where `projector_type` lives. The parser tolerates both known shapes and
must treat an unrecognised shape as unknown rather than guessing, so an
unclassifiable artifact degrades to an explicit unknown classification instead
of a wrong one.

## Migration Plan

No migration code is written. After upgrading, previously installed models
report as `unreconciled`; a single `coquena model reconcile --all` restores the
catalog, classifying every artifact in the same pass. Model files themselves are
never rewritten, so nothing needs re-downloading.

## Open Questions

- The exact flag name for companion-aware download is left to implementation;
  the behavior is specified, the spelling is not.
- Whether the runtime constraint derived from a model should ever be surfaced
  as a hard admission failure rather than advisory information is deferred
  until presets are evaluated against installed runtimes.
