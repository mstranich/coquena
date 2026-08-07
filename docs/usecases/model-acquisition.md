# Use case: model acquisition

**Kind:** usecase · **State:** current · **Story:** `model-acquisition`

Assembles the story with the findings that constrain it, and derives the
operations Coquena owes. Everything here is traceable: a step comes from the
story, a constraint comes from a finding, and anything with neither is a
candidate for deletion rather than for a justification written afterwards.

## The unit

The story says it in the operator's own words: *"I ask Coquena for **the
quantization**. Coquena downloads one or more files, but it must be a complete
bundle."*

`quantizations-can-be-shard-directories` shows the ecosystem imposes the same
unit rather than it being a design preference: `unsloth/DeepSeek-R1-GGUF` has no
`.gguf` at its root at all, only one directory per quantization.

```text
repository     where artifacts come from, remote or local
quantization   what the operator names and manipulates; resolves to N files
shard          a mandatory part of one quantization; useless alone
companion      shared across several quantizations of one repository
file           an implementation detail the operator names only to disambiguate
```

Two relationships the word "file" hides, and they are not the same:

- A **shard** belongs to exactly one quantization and completes it.
- A **companion** — today a projector — is complementary to one *or several*
  quantizations. Download a second quantization and the companion is already
  there. Delete one quantization and the companion stays if any other still
  needs it; delete the last and it goes too.

That deletion rule needs no stored reference count: *"does any remaining
quantization in this repository require it?"* is answerable by enumerating the
repository at the moment of deletion, and a derived answer cannot drift the way
a persisted counter can. The sharing boundary is the repository.

## Naming

`model-reference-convention-uses-one-selector-slot` fixes the grammar, and it is
already the ecosystem's:

```text
{user}/{repo}[@{revision}][:{selector}]

{selector} = {quantization}     logical identity → a set of 0..N artifacts
           | {filename.gguf}    physical address → exactly one artifact
```

The story requires that the operator not think about revisions, so `@revision`
exists for Coquena to record and for the operator to omit. It answers *when*,
not *which*, which is why it is a separate axis rather than another segment.

Quantization identity is independent of calibration
(`low-bit-recipes-require-an-importance-matrix`): two files differing only in
imatrix share a quantization. The tag form grouping both is the equivalence
relation working, not a collision.

## Identity of a quantization

`general-file-type-is-llama-ftype` establishes where the declared recipe lives
and how it is misread. `llama-cpp-guesses-quantization-from-tensor-counts` adds
that an absent declaration may arrive pre-guessed, flagged, and carrying an
invented suffix. `gguf-tensor-names-are-canonical` provides the only sound
fallback: dominance over the repeating block tensors, excluding the tensors a
recipe promotes.

```text
declared recipe, guessed bit clear   →  the recipe, as evidence
guessed bit set                      →  treat as absent; re-infer locally
absent, canonical blocks present     →  base encoding, marked inferred
absent, no canonical blocks          →  unknown
```

## Operations the story requires

| Step in the story | Operation | Status |
|---|---|---|
| discover quantizations, files and size | `discover` | exists; shows the model card by default, which is noise for choosing |
| download the quantization | `download` | exists; `--with-companions` contradicts "complete bundle" |
| see what I have | `list` | exists |
| bind a quantization to a preset | `preset` | exists, outside `model` |
| check for updates | `update` | exists as `check-updates`; the verb should match `apt`/`winget` |
| download those updates | `upgrade` | **missing** |
| delete a quantization or companion | *(none)* | **missing** — `preset`, `field` and `runtime` all have `remove`; models do not |
| import files obtained elsewhere | `import` | exists as `reconcile`; must keep requiring an explicit intent flag |
| keep a private fine-tune | — | must not be treated as awaiting reconciliation with a repository that does not exist |

`resolve` and `revision` serve no step. Both look like internal helpers — of
`discover` and `verify` respectively — that ended up exposed.

Bulk forms are deliberately absent. `<ref>` stays mandatory; if library-wide
action is ever needed, `<ref>` becomes optional and no flag is introduced.

## Integrity and cost

The story requires the size **before** the download, so discovery reports the
sum over the set, never one file's size.

`sibling-quantizations-share-metadata-bytes` rules out the obvious cheap check:
a digest over the first N megabytes is identical for two quantizations of the
same model, because the ~8 MB they share is exactly the window it would read.
Any fast digest must include the file size and sample the tail, and any
re-derivation of quantization evidence must read the tensor table rather than
the header alone.

`hugging-face-publishes-content-hash-under-blobs` establishes that byte-level
attestation of an imported file is achievable today for the common case;
downloaded files are attested by construction, since their bytes came from a
revision URL.

## Open, and owned elsewhere

- **Renames.** `hugging-face-redirects-renamed-repositories` shows a repository
  can move while every operation keeps succeeding. What `update` should report,
  and what identity the sidecar asserts, belong to
  `model-artifact-attestation`.
- **Attestation strength.** What "attested" means when no remote hash exists is
  the same RFC.

## Gaps this use case exposes and nothing yet owns

Recorded here rather than silently added to the backlog:

1. **Shards and companions as one unit.** No artifact covers making a
   quantization the addressable unit: bundle-aware download, deletion with the
   sharing rule, and library enumeration that does not report one quantization
   as N rows.
2. **Deletion.** No operation exists at all.
3. **`upgrade`.** Named in the story, tracked only as a blocked backlog entry.
4. **`discover` output.** Reporting size and effective files rather than the
   model card.
5. **Command surface cleanup.** `--with-companions`, `resolve`, `revision`, and
   the `check-updates` → `update` rename.
