## Context

Coquena currently has an initialized portable home, a configurable `models/` directory, a Go CLI, a local HTTP server, and an Angular Models workspace. It has no model catalog, GGUF parser, or resumable model-transfer service. Runtime artifact downloads already demonstrate the principle that Coquena owns local artifacts, but their archive-oriented, in-memory operation model is not suitable for model-sized resumable transfers.

The supplied functional specification settles the principal architectural choices: models are Coquena-owned artifacts, llama.cpp is only a replaceable runtime, metadata originates in GGUF, metadata never maps automatically to runtime arguments, persistence is filesystem plus JSON, and each GGUF has individual JSON and `.revision` sidecars.

## Goals / Non-Goals

**Goals:**
- Resolve public Hugging Face repository IDs and canonical Hugging Face file URLs, list only GGUF candidates, and retain their immutable revision and remote identity.
- Inspect a GGUF header and metadata over HTTP Range or from disk without reading tensor data or requiring a registered runtime.
- Make download jobs durable, cancellable, resumable, validated, atomically published, and observable from shared backend behavior.
- Keep a portable model artifact, JSON sidecar, and commit-SHA `.revision` sidecar together beneath the existing configured models directory.
- Expose the same operations through CLI, local API, and Models WebUI, including structured JSON responses.

**Non-Goals:**
- Private/gated Hugging Face access, credential storage, providers other than Hugging Face, arbitrary download URLs, or peer-to-peer transfers.
- Tensor loading, model conversion, full-file hashing when no upstream digest is available, checksum generation as a completion prerequisite, or automatic cleanup of completed models.
- Automatic argument discovery, parsing `llama-server --help`, mapping GGUF metadata to presets, changing the existing manual field catalog, or launching a runtime.
- A database, distributed job scheduler, download prioritization, parallel-segment downloads, retaining completed job records, or checking/downloading updates for an installed model.

## Decisions

### Treat a resolved Hugging Face artifact as an immutable download source

The resolver accepts
amespace/repository`, an optional `:filename-or-variant` selector, and canonical `huggingface.co/.../resolve/...` file URLs. It queries the Hugging Face API to list repository siblings, filters `.gguf` names case-insensitively, and resolves a selector to exactly one candidate. Repository input without a selector returns candidates; an ambiguous selector is an error. Before inspection or download it resolves the selected revision to a commit SHA and records requested revision, resolved commit SHA, filename, canonical commit-pinned resolve URL, size, ETag, and Last-Modified when supplied. All inspection, download, and resume requests use that commit-pinned URL, never a mutable branch URL.

This gives repeatable identity and safe resume validation. The initial public-only decision keeps the API and credential boundary small; auth can be added later without changing the stored source identity.

### Use one bounded random-access GGUF reader contract

A `RandomAccessReader` exposes `ReadAt` and `Size`. Local files implement it directly; the HTTP implementation obtains the remote size and serves bounded Range requests. A Go GGUF reader parses only the magic/header, key-value metadata, and value encodings needed to reach the tensor-info boundary; it never reads tensor payloads. Metadata is represented as JSON-compatible raw values keyed by original GGUF key, preserving unknown keys and nested arrays.

Using one reader prevents remote and local parsers from drifting. A mature external Go GGUF library may be adopted only if it satisfies this contract, preserves unknown metadata faithfully, supports random access, and has a compatible license; otherwise a small in-tree reader is preferred over a runtime dependency.

### Persist each incomplete transfer beside its model target

A new `internal/model` service owns resolution, inspection, jobs, discovery, and library publication. Each incomplete model uses `<model>.gguf.partial` and `<model>.gguf.download.json` in the validated repository/filename tree beneath the configured models directory. This avoids copying multi-gigabyte artifacts across locations and makes the models directory the sole durable source of truth. Cold-start discovery scans only this tree and recovers interrupted `downloading` jobs as `paused`; model management neither reads nor writes `<home>/downloads`.

State transitions are persisted atomically after creation, progress checkpoints, pause, failure, and publication. Resume reconciles a partial that is ahead of its last checkpoint because bytes are written before the checkpoint is committed; a partial smaller than the checkpoint fails safely. Resume sends `Range` and `If-Range` against the persisted commit-pinned URL and validator. Cancel removes only the incomplete `.partial` and `.download.json` unit.

### Persist the resolved revision redundantly

Every completed artifact has `<base>.revision` containing only the resolved remote commit SHA plus a trailing newline. Its JSON sidecar records both `requestedRevision` (normally `main`) and `resolvedRevision` from the same service value. Reading a library artifact validates that JSON and `.revision` agree; disagreement is an inconsistency error and Coquena never silently selects either source. `.revision` is remote provenance, not a local checksum; a future checksum uses the distinct `.sha256` extension.

### Validate before atomically publishing a model and its sidecars

Completion requires the downloaded byte count to equal known remote size, then local GGUF header/metadata inspection to succeed. The service creates the destination tree from validated repository/filename path components and rejects traversal or collisions. It atomically renames the same-directory partial to the final GGUF, writes versioned JSON and `.revision` sidecars through same-directory temporary files and atomic renames, then removes the `.download.json` record. If either sidecar publication fails after the GGUF rename, the service restores the partial and removes any new sidecar so no incomplete logical model unit is exposed. Existing same-identity models are idempotent only after the GGUF, valid matching JSON sidecar, and matching `.revision` all exist; otherwise the operation fails rather than overwriting. Checking or downloading replacements for installed models is deferred to a future change.

Sidecars contain source identity, artifact size, optional available digest, download timestamp, and the complete raw GGUF metadata. Paths remain an implementation detail; API and CLI expose records instead of trusting caller paths.

### Put all user interfaces behind the service boundary

The CLI receives `coquena model resolve|inspect|download|revision` and `coquena download list|status|pause|resume|cancel`, accepting `--json` once in any valid position. Its adapters call the shared model service rather than HTTP or duplicate transfer logic. The local HTTP server exposes resolve, inspect, download creation, job listing/status, and job-control endpoints; accepted download jobs return `202`, and job state/progress is available by polling and SSE. All API endpoints are routed before the Angular fallback and use schema-version-1 JSON envelopes.

The Models workspace uses an Angular service with RxJS HTTP/EventSource boundaries and signals for template state. It discovers installed and incomplete model units, allows resolving/selecting GGUF candidates, renders bounded metadata summaries before download, starts one job, shows progress/status, and offers pause/resume/cancel. Complete raw inspection remains available explicitly through CLI/API without rendering very large tokenizer arrays into the DOM. It treats metadata as view-only reference alongside the existing manual preset controls.

### Keep metadata and runtime configuration deliberately separate

The parser and completed sidecar do not attach semantic meaning to metadata keys beyond lossless JSON representation. The Models UI renders useful values and bounded summaries for very large arrays, but it cannot create, fill, or mutate field-catalog entries or presets from GGUF values. The service never executes or interrogates llama.cpp.

### Present metadata and publisher identity per artifact

Each discovered GGUF candidate owns independent inspection state. Its Inspect action fetches only that artifact, replaces itself with a collapsible Metadata/Value table, and does not affect sibling candidates. Installed metadata is not included in the library listing; expanding an installed model lazily reads its validated JSON sidecar through a dedicated API and displays the same reusable table component.

The Hugging Face namespace is treated as the publisher. When the first download for a publisher begins, Coquena best-effort downloads `/api/avatars/<publisher>`, validates a bounded PNG, JPEG, or WebP payload, and stores it once as `<models>/<publisher>/publisher-avatar.<format>`. Discovery displays the remote avatar; incomplete and installed entries use only the local cached avatar. Avatar failure never blocks model creation or publication, and generated social thumbnails are not used.

## Risks / Trade-offs

- [Hugging Face may omit strong validators or Range support] → Record every available validator, validate size and local GGUF structure, and fail unsafe resume instead of silently appending.
- [GGUF value types and future extensions vary] → Preserve original keys and decode the supported specification types with explicit unsupported-type errors; test representative headers and unknown keys.
- [An atomic rename cannot span filesystems] → Stage the final partial in the target model directory before final publication, or reject a cross-device move; model and both sidecars use same-directory replacements.
- [Application exits during download] → Persist recovery state frequently enough to reconcile partial length on next use; recover active jobs to paused.
- [Large tokenizer arrays can freeze the browser] → Default WebUI inspection summarizes arrays above a bounded count; explicit CLI/API raw inspection remains available without rendering that payload into the DOM.
- [Concurrent requests can race model paths] → Apply a process-local job/model mutation lock and re-check target identity immediately before publish.

## Migration Plan

1. Add the service and interfaces without changing the current models-directory contract or runtime management.
2. Create `.gguf.partial` and `.gguf.download.json` units beneath the configured models directory and discover jobs exclusively from that tree.
3. Publish new models only as GGUF-plus-JSON-plus-`.revision` units under the current effective models directory.
4. If a release must roll back, leave completed model units readable; unsupported incomplete job directories can be removed by the newer version's cancel/cleanup path and do not affect runtime installations.

## Open Questions

No blocking gaps remain for this public Hugging Face first slice. The following are deliberately deferred: Hugging Face authentication/gated models, policy for a user-requested SHA-256 calculation, and any broader provider abstraction.
