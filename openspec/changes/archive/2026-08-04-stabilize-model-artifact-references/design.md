## Context

The existing parser already has one optional selector after `:`. Selectors ending in `.gguf` take an exact-filename branch; other selectors use quantization-oriented matching. Remote parsing preserves `@revision`, but installed-library paths have historically discarded it. Preset bindings persist both the authored reference and a resolved filename/revision, yet their freshness check re-resolves only the authored selector and collapses any error, including newly introduced ambiguity, into a stale boolean.

The archived umbrella RFC at `docs/rfcs/archive/gguf-model-artifact-identity.md` settled two independent axes:

```text
identity:  repository -> quantization class -> exact artifact
time:      optional @revision applied to every identity level
```

It also rejected slash-form artifact references and variant suffix taxonomies in favor of the existing ecosystem-compatible selector slot.

Implementation audit identified the concrete loss points: `parseRef` defaulted
omitted revisions to `main` before local/remote intent diverged;
`resolveLibrary` discarded the parsed revision; installed ambiguity errors
carried filenames rather than actionable references; `FormatReference` exposed
only a quantization label; preset and companion binding stored filename and
revision but no formatted canonical reference; `StaleModel` re-resolved only
the authored selector and converted every error into `true`; and companion
principal resolution likewise ignored the already resolved artifact.

## Goals / Non-Goals

**Goals:**

- Represent parsed references once with an explicit selector kind: absent, quantization, or exact filename.
- Carry optional revision constraints through remote and installed resolution.
- Make canonical artifact identity an exact repository filename at a recorded revision.
- Preserve authored logical selectors while durable consumers bind to canonical artifacts.
- Provide structured multi-state resolution results that callers cannot accidentally collapse into staleness.

**Non-Goals:**

- Change the textual grammar, infer variant labels, or add a second selector slot.
- Redesign sidecar authority blocks or persist remote drift.
- Automatically rewrite existing preset references or rebind them.
- Change model bytes, download behavior, or WebUI workflows.

## Decisions

### 1. Keep one parser and make selector kind explicit

The parsed reference carries repository, optional requested revision, selector text, and selector kind. A selector ending in `.gguf` is an exact filename; every other non-empty selector is logical. Remote and offline resolution consume the same parsed representation.

This is preferred over separate remote/offline parsers because the current revision discrepancy came from reconstructing and partially discarding parser output. A slash alias is rejected because it adds no addressing power and conflicts with the settled grammar.

### 2. Treat cardinality as part of resolution, not parsing

Parsing determines what the operator wrote; resolution determines whether it matches zero, one, or many artifacts. Quantization selectors admit `0..N`; exact filenames admit `0..1`. APIs return structured outcomes plus candidate canonical references rather than encoding cardinality as a generic error string.

The alternative of requiring quantization selectors to be unique everywhere was rejected because they intentionally denote an equivalence class. Uniqueness is imposed only by the operation requesting a singleton.

### 3. Build canonical references only from trusted artifact provenance

A canonical artifact reference is formatted from the installed/resolved artifact's repository, recorded resolved revision, and exact filename:

```text
user/repo@resolved-revision:filename.gguf
```

The operator may omit `@revision` in authored input, but returned canonical identities include the recorded resolved revision when remote provenance exists. Local-provider artifacts without a remote revision retain an unpinned canonical filename reference. Filename-derived quantization tokens never participate in canonicalization.

### 4. Enforce revision pins during candidate filtering

An explicit revision filters candidates against their persisted resolved revision before selector matching. An omitted revision imposes no local revision constraint; it does not synthesize `main`. This keeps an unpinned local reference usable while ensuring an explicit pin is never silently ignored.

Comparing only resolved revisions is chosen for this change. Requested-branch semantics and remote drift belong to the later sidecar-authority change.

### 5. Separate authored intent, resolved binding, and current evaluation

Preset bindings retain:

- the authored reference, unchanged;
- the canonical artifact resolved at bind time;
- a computed current evaluation.

Current evaluation has distinct states for same artifact, different artifact, missing, and ambiguous. A sibling arriving in the same quantization class produces `ambiguous` for the authored selector while the canonical binding remains present and usable; it does not produce `stale`. No evaluation mutates persisted binding state.

This is preferred over rewriting authored selectors to filenames because doing so would erase operator intent and prevent useful diagnostics about a class becoming ambiguous.

### 6. Use canonical references at every singleton boundary

Model list, ambiguity diagnostics, preset principal bindings, and exact companion assignments expose the same canonical formatter. Logical selectors remain useful for discovery, filtering, and authoring, but any operation requiring one artifact either resolves exactly one candidate or refuses with canonical alternatives.

## Risks / Trade-offs

- **[Existing callers expect a boolean stale result]** -> Introduce the structured state first, migrate every caller, and remove or narrow the boolean helper only after exhaustive call-site tests.
- **[Revision strings may contain mutable branch names in authored input]** -> Compare explicit local pins only with persisted resolved provenance supported by the sidecar; do not claim remote freshness.
- **[Canonical output becomes more verbose]** -> Human output may show a logical class secondarily, but JSON and actionable diagnostics always include the exact canonical reference.
- **[Legacy bindings lack a separately stored canonical string]** -> Derive it from their existing repository, filename, and resolved revision fields during reads; do not rewrite the document merely to materialize formatting.
- **[Quantization matching remains broader than exact identity]** -> Preserve it as the established logical-selector behavior and rely on explicit ambiguity rather than silently narrowing it.

## Migration Plan

1. Introduce the shared parsed-reference and canonical-reference contracts with compatibility tests for existing syntax.
2. Carry revision through installed resolution and add structured cardinality/evaluation outcomes.
3. Migrate CLI listing and singleton diagnostics to canonical artifact references.
4. Migrate preset principal and companion consumers to canonical binding plus multi-state evaluation.
5. Run focused and full-suite validation, including persisted legacy binding fixtures.

Rollback restores the previous resolver and presentation behavior without changing model bytes or sidecars because this change performs no automatic persistence migration.
