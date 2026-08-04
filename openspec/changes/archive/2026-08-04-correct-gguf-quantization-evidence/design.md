## Context

GGUF exposes two independent integer domains that happen to overlap:
`general.file_type` uses the model-recipe domain represented upstream by
`llama_ftype`, while each tensor descriptor uses the physical encoding domain
represented by `ggml_type`. Coquena currently stores `general.file_type` as an
untyped integer and resolves it through a tensor-type table. Value `15`, for
example, can therefore be rendered as `Q8_K` even though the model recipe value
means `Q4_K_M`.

The local parser compounds that error. Metadata and tensor-table traversal are
implemented more than once, and existing loops have started from fixed offsets,
failed to consume metadata, or decoded tensor fields with the wrong order or
width. These paths feed classification, parameter count, list rendering,
capability inference, projector defaults, download sidecars, and verification.

Current sidecar schema version 2 stores a compact `Classification` directly.
The reader rejects every other schema version. That was appropriate for the
v1-to-v2 semantic replacement, but it would discard valid source and artifact
attestation merely to recalculate local derived values in the next migration.

The project owner closed RFC `gguf-model-artifact-identity` (archived at
`docs/rfcs/archive/gguf-model-artifact-identity.md`) by separating three kinds
of evidence:

- declared model recipe from `general.file_type`;
- physical tensor composition from tensor descriptors;
- cached derivation provenance describing how local evidence was computed.

The current product milestone is a minimally functional CLI. All WebUI work is
postponed until the project owner explicitly resumes it, so browser presentation
and browser API evolution are outside this change even when they consume related
model data today.

The working tree already contains uncommitted experiments in the affected
parser, CLI, and tests, plus unrelated frontend edits. Implementation must
evaluate the in-scope edits against this design and leave the frontend edits
untouched.

## Goals / Non-Goals

**Goals:**

- Make recipe and tensor-encoding domains distinct in Go APIs and persisted
  evidence.
- Decode GGUF metadata and tensor descriptors through one shared traversal.
- Preserve declared recipe identity without inventing recipe suffixes from
  physical composition.
- Produce deterministic parameter counts and bounded physical-dominance
  evidence over supported canonical structural scopes.
- Make stale derived evidence identifiable from the sidecar alone.
- Repair stale derived evidence for one integrity-matched artifact without
  changing any other authority's facts.
- Remove the misleading `model inspect` CLI while retaining the internal
  inspection projection.

**Non-Goals:**

- Redesign logical or physical artifact references.
- Decide how multiple calibration variants are addressed.
- Add persisted drift, ownership, or generalized attestation axes.
- Add `upgrade`, automatic repair, startup repair, or any `--all` operation.
- Infer `_M`, `_S`, `_L`, calibration, or imatrix provenance from tensor types
  or filenames.
- Return raw GGUF metadata through the CLI or API.
- Modify Angular components, browser-facing workflows, or browser API contracts.
  Existing WebUI behavior is deliberately left as-is for a later milestone.

## Decisions

### 1. Use domain-specific integer types and lookup APIs

Define separate types such as `LlamaFileType` and `GGMLType`. Functions that
decode or render them must name their domain and accept only the corresponding
type. `general.file_type` is decoded as `LlamaFileType`; descriptor `type` fields
are decoded as `GGMLType`.

This is preferred over retaining `int64` plus comments because the current bug
compiled precisely because comments and overlapping values offered no boundary.
Explicit conversion is allowed only at the binary/JSON boundary.

The recipe lookup follows the supported upstream `llama_ftype` values, including
defensive handling of the guessed flag. A guessed value does not promote an
upstream presentational default such as `_M` into a declared suffix: it is
reported as guessed physical evidence, not as a declaration.

### 2. Use one stream-oriented GGUF traversal

Introduce one scanner over the existing random-access abstraction. It validates
the magic/version, reads 64-bit tensor and metadata counts, consumes every
metadata value including arrays without materializing payloads that are not
needed, and then yields tensor descriptors with name, dimensions, physical type,
and offset.

Classification, parameter count, physical composition, local inspection, and
download record creation consume projections of this traversal. They do not
implement their own offset arithmetic.

Keeping the random-access abstraction preserves bounded remote header reads for
existing classification workflows. A second independent parser was rejected
because it would recreate the defect class this change exists to remove.

### 3. Treat declared recipe and physical composition as separate evidence

When `general.file_type` contains a recognized recipe, the declared recipe is
the model-level quantization identity reported to operators. Tensor descriptors
produce a physical histogram keyed by `GGMLType`; that histogram can corroborate
or diagnose the declaration but cannot reconstruct recipe suffixes.

For a metadata-absent artifact, physical dominance is computed by scalar
parameter count, not tensor count or encoded byte size. Only canonical
structural tensor prefixes are eligible:

- `blk.`
- `enc.blk.`
- `dec.blk.`
- `v.blk.`
- `a.blk.`

If no supported structural tensor exists, physical dominance is unavailable.
Coquena does not fall back to all tensors, because embeddings and output tensors
can dominate small or vocabulary-heavy models and bias the result toward an
exception encoding.

Declared-versus-physical disagreement is diagnostic information, not by itself
an integrity failure. Integrity answers whether bytes match a baseline;
derivation answers what those bytes contain.

### 4. Store derived evidence in a separately versioned block

Sidecar schema version 3 introduces a `derivation` block with at least:

```text
version
computedAt
classification
countParameters
physicalQuantization (when available)
```

Derivation algorithm version starts at `1`. The top-level schema version governs
document shape; `derivation.version` governs the interpretation of recomputable
local evidence. `model list` compares the stored derivation version with the
current algorithm version and may report `derived-stale` without opening the
GGUF or using the network.

One persisted representation is preferred over duplicating classification both
inside and outside the derivation block. In-memory compatibility adapters may
ease migration, but writers emit only schema 3.

### 5. Read schema 2 tolerantly, but never rewrite it implicitly

The reader accepts schema 2 only as a legacy record whose `Source`, `Artifact`,
and existing integrity baseline remain valid. Its classification/count are
marked stale because they lack derivation provenance. Ordinary reads remain
non-mutating. Schema 1 remains rejected because its meaning and raw-metadata
shape require the already-established reconciliation path.

Managed download and reconciliation write schema 3 directly. A schema-2 record
becomes schema 3 only after an explicit successful repair or another operation
that already has authority to publish a current sidecar.

### 6. Make repair an integrity-gated patch

`model verify <ref>` retains its current read-only matched/mismatched/no-baseline
behavior and adds separate declared/physical diagnostics where available.

`model verify <ref> --repair-derived` performs one full artifact pass sufficient
to compute SHA-256 and GGUF evidence. It writes the new derivation block only
when the computed digest matches the stored baseline. Mismatch and no-baseline
outcomes refuse repair.

The patch preserves, byte for byte where representation permits:

- model content;
- `Artifact.SHA256` and other artifact attestation;
- source provider, repository, revisions, filename, and URL;
- remote drift information;
- ownership/operator acceptance;
- fields unknown to the derivation writer that belong to another authority.

The operation is per artifact and has no bulk form. A temporary file plus atomic
replace preserves the existing sidecar publication safety model.

### 7. Remove only the `inspect` CLI surface

The CLI dispatcher, usage text, README examples, and CLI-specific tests for
`coquena model inspect` are removed. The internal `Inspection` type and
`Inspection.Record()` projection remain because download uses them to publish a
sidecar without parsing the header twice. The local/API metadata view is not
removed or revised by this change.

`verify` becomes the operator-facing command that compares current bytes-derived
facts with recorded facts. `list` remains the cheap way to detect stale cached
derivation without paying for a full digest.

### 8. Re-derive downstream consumers instead of renaming the old table

Capability requirements and projector defaults must use recipe-domain sets or
physical-domain evidence according to the question they answer. Numeric ranges
calibrated against the old mixed table are replaced with explicit typed sets.
Every `FileTypeDisplayName` call site is audited; no compatibility wrapper may
silently preserve the cross-domain lookup.

## Risks / Trade-offs

- **[A full verification pass over large artifacts is expensive]** → Repair is
  explicit and per artifact; `list` uses only the derivation version for cheap
  targeting.
- **[Schema-2 tolerance could accidentally treat stale classification as
  current]** → Legacy records carry an explicit stale derivation state and are
  never rewritten during an ordinary read.
- **[Remote bounded reads may require more ranges with a shared scanner]** →
  Preserve the current random-access abstraction and test bounded behavior;
  correctness takes priority over duplicated fast paths.
- **[Canonical tensor prefixes may not cover a future architecture]** → Return
  unavailable rather than widening to all tensors; add a prefix only with an
  upstream GGUF naming fixture.
- **[Removing `inspect` reduces cheap fresh-header access]** → `list` cheaply
  reports derivation staleness, while `verify` intentionally pays the full cost
  when fresh byte evidence is required.
- **[Existing uncommitted edits may encode assumptions rejected by the RFC]** →
  Audit each affected hunk and require the new typed/scanner tests before reuse.

## Migration Plan

1. Add typed enum-domain tables and scanner tests without changing persisted
   schema.
2. Switch classification, counting, capability, and projector consumers to the
   shared traversal and typed evidence.
3. Add schema-3 writing plus schema-2 tolerant reading and stale derivation
   reporting.
4. Add integrity-gated `verify --repair-derived` and migration tests proving
   non-derived fields survive.
5. Remove the `inspect` CLI surface and update CLI operator documentation,
   without changing WebUI or browser API behavior.
6. Validate current fixtures, synthetic prefix cases, schema compatibility, CLI
   JSON/human output, and the available real Ornith artifact if it can be used
   without adding a large repository fixture.

Rollback before sidecar migration can revert code directly. After schema-3
sidecars are written, rollback requires restoring the previous executable only
after preserving those sidecars, because schema-2 code will reject schema 3;
model bytes remain untouched throughout.

## Open Questions

None block implementation. Whether an external Ornith artifact is exercised in
the default test suite or in a documented acceptance command depends on fixture
availability and repository-size constraints; synthetic fixtures remain
mandatory either way.
