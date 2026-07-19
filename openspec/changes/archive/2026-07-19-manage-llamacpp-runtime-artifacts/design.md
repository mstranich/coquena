## Context

Coquena has a portable installation and a read-only normalized `system` report. Official llama.cpp releases contain platform/backend distributions, non-runtime assets, and sometimes dependencies. Release `b10068` demonstrates Windows CPU, Vulkan, CUDA 12.4/13.3, HIP, SYCL, OpenVINO, and OpenCL Adreno; Ubuntu adds ROCm and precision-qualified SYCL; macOS arm64 enables Metal without encoding it in the filename. Windows CUDA consists of a runnable primary archive plus a matching `cudart` archive. GitHub's Releases API supplies a `digest` for each uploaded asset.

This change establishes discovery and local artifact lifecycle only. The normal experience must resemble a runtime/SDK manager: simple selectors for common use and an exact immutable identifier for reproducible operations.

## Goals / Non-Goals

**Goals:**

- Show a small, useful remote catalog compatible with the current host by default.
- Support convenient CPU, Vulkan, and CUDA selectors on the first complete Windows target.
- Preserve and display the exact primary asset filename as the public runtime ID.
- Require one unambiguous compatible candidate before download.
- Verify, stage, validate, publish, list, and remove managed distributions safely.

**Non-Goals:**

- Downloading another OS or native architecture, including through emulation.
- Force-installing candidates whose compatibility is false or unknown.
- Recommending the best backend, activation/default selection, shims, execution, upgrade aliases, or rollback.
- Expanding system inspection to HIP, SYCL, OpenVINO, or OpenCL in this change.
- External builds, models, other providers, Web UI, or an unbounded persistent remote cache.

## Decisions

### Separate upstream taxonomy, compatibility, and presentation

The llama.cpp adapter recognizes primary distributions and dependency assets across the known upstream taxonomy. A `Distribution` contains release tag, target (`cpu` or `gpu`), OS, native architecture, backend family, optional backend version/flavor/precision, one primary `ReleaseAsset`, and zero or more dependency assets. UI, XCFramework, source archives, standalone `cudart` archives, and unknown assets are not primary candidates.

Recognition does not imply compatibility. Compatibility is evaluated from the current normalized system report with states `compatible`, `incompatible`, or `unknown` and stable evidence/reason codes. Remote output excludes non-compatible candidates by default; local output includes every valid installation with its recalculated current state so obsolete installations remain removable.

### Use strict host-native compatibility

OS and native architecture are implicit and never user-selectable in this change. Exact native-architecture matching is required; executable architecture and OS emulation do not expand the candidate set. CPU is compatible on a matching platform. CUDA and Vulkan require their backend capability to be `available`. Backends not evaluated by `coquena system` are `unknown` and cannot be downloaded.

CUDA toolkit compatibility uses an explicit tested policy rather than string equality: a sufficiently new NVIDIA driver can run older CUDA runtimes according to NVIDIA's backward/minor compatibility rules. Backend policy is isolated from upstream filename parsing so evidence and rules can evolve independently.

### Make remote lookup fast and bounded

`runtime remote` with no selector reads the official latest non-draft, non-prerelease release and shows all compatible candidates. The grammar is:

```text
runtime remote [<tag>] [<backend-selector>]
runtime remote --history <count> [<backend-selector>]
```

`<tag>` is `latest` or an exact `b<digits>` tag. Initial backend selectors are `cpu`, `vulkan`, `cuda`, and `cuda@<major>[.<minor>]`. `cuda` intentionally may return multiple rows. Bounded history scans newest releases through paginated API results and applies the same compatibility/filter policy. A future `--all` may expose incompatible platforms, but it is not part of this change.

Because the latest-only default can otherwise be mistaken for a complete remote catalog, the unqualified human command appends a short notice that it is showing only the latest compatible release and points to `--history <count>`. Explicit selectors, explicit release/history requests, and JSON output omit this notice so filtered views remain concise and machine output remains pure.

Every row exposes release, target, normalized backend, exact primary `ASSET`, and `DEPENDENCIES`. Results have deterministic ordering and strict schema-versioned JSON equivalents.

### Support exact and convenient download resolution

The grammar is:

```text
runtime download <primary-asset-id>
runtime download <backend-selector>
runtime download <tag> <backend-selector>
```

Resolution first treats a single argument matching a recognized primary asset name as an exact ID. Otherwise it parses the backend selector and uses `latest` when the tag is omitted. After implicit platform and compatibility filtering, zero matches produce not-found, one match proceeds, and multiple matches produce an ambiguity error listing exact asset IDs. Every failure occurs before download or filesystem mutation.

An exact ID is still verified against official release metadata; Coquena never trusts a URL synthesized from user input. Current primary names contain their release tag, enabling a targeted tag lookup. Path separators, partial matches, dependency-only assets, and incompatible or unknown candidates are rejected.

### Treat the primary asset filename as the public local ID

Download, local list, and remove expose the exact primary filename, such as `llama-b10068-bin-win-cuda-13.3-x64.zip`. Internal paths use a safe canonical encoding rather than trusting the filename as a path. Per-runtime `runtime.json` records schema version, public ID, normalized identity, compatibility facts at installation, primary and dependency source metadata/digests, installed files, and timestamp. `config/coquena.json` is unchanged because no runtime becomes active.

### Resolve composite distributions before mutation

Once exactly one primary distribution is selected, the adapter associates dependencies. Windows CUDA requires the `cudart` asset with matching CUDA version and architecture from the same release. A missing or ambiguous dependency fails resolution before staging.

The Releases API `asset.digest` is the official checksum source; no separate checksum manifest is assumed. Every primary and dependency asset must provide a supported SHA-256 digest and pass verification.

### Publish through a staged transaction

Download streams bounded assets into unique owned staging below the portable `downloads`/`tmp` trees, verifies digests, extracts into a fresh directory with path-containment, entry-count, expanded-size, link, special-file, and duplicate-path protections, validates required files, and runs a bounded non-server `llama-server` probe. It then writes newline-terminated metadata and atomically renames the staging directory to the final same-filesystem destination.

An installation-scoped fail-fast mutation lock serializes download and removal. Reinstalling an intact immutable ID succeeds without replacement; inconsistent existing content is preserved and reported. Removal selects one exact local ID, renames it to an owned tombstone, and deletes only that tombstone.

## Risks / Trade-offs

- **Upstream names and matrices change** → Centralize conservative recognition and cover current and historical fixtures; skip unknown primary candidates with diagnostics.
- **System inspection does not cover every published backend** → Mark them unknown and exclude them until their detector exists.
- **CUDA compatibility rules evolve** → Isolate a data-driven policy with official-source tests and explicit evidence.
- **GitHub rate limits/history volume** → Latest/tag lookups use targeted endpoints; history is explicitly bounded; optional conventional authentication is never persisted.
- **Archives or probes are hostile or excessive** → Bound network, extraction, process duration, and captured output before publication.
- **Crash leaves owned staging/tombstone content** → Use recognizable unique paths, best-effort cleanup, and diagnostics without touching unrelated content.

## Migration Plan

This is additive. Existing initialized installations already have the required directories and need no configuration migration. Rollback removes the commands/code; installed directories remain inert and manually removable.

## Open Questions

None. Expanding backend detection and exposing an informational all-platform catalog require separate changes.
