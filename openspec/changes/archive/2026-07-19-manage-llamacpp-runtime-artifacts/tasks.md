## 1. Distribution and selection model

- [x] 1.1 Define distribution, release asset, dependency, backend qualifier, selector, compatibility, diagnostic, and schema-versioned result types with validation tests
- [x] 1.2 Implement exact public asset-ID validation and safe internal path encoding with traversal, collision, and malformed-ID tests
- [x] 1.3 Implement parsing for `latest`, exact `b<digits>` tags, `cpu`, `vulkan`, `cuda`, and `cuda@<major>[.<minor>]`
- [x] 1.4 Implement deterministic filtering and ordering, including zero/one/many candidate resolution and actionable ambiguity errors

## 2. Official llama.cpp catalog

- [x] 2.1 Capture representative official release/API fixtures for current and historical CPU, CUDA, Vulkan, Metal, HIP, SYCL, ROCm, OpenVINO, OpenCL, dependency, UI, XCFramework, and source assets
- [x] 2.2 Implement an injected bounded GitHub Releases client for latest, exact-tag, and paginated bounded-history lookup with timeout, proxy, optional conventional token, rate-limit, and failure tests
- [x] 2.3 Implement conservative versioned classification of primary, dependency, and non-runtime assets while preserving raw metadata and GitHub `digest`
- [x] 2.4 Implement Windows CUDA primary-to-`cudart` association and fail-fast missing/ambiguous dependency behavior
- [x] 2.5 Add fixture tests proving exact normalized backend versions/flavors/precision and implicit platform semantics without substring guessing

## 3. Host compatibility

- [x] 3.1 Adapt the existing normalized system report into strict OS/native-architecture and CPU/backend compatibility evidence
- [x] 3.2 Implement CPU, Vulkan, unavailable, unsupported, unknown, and unprobed-backend policies with tests
- [x] 3.3 Implement an explicit data-driven CUDA driver/toolkit compatibility policy with older-runtime/newer-driver and too-old-driver tests
- [x] 3.4 Add Windows compatibility matrix tests proving that emulated, cross-architecture, HIP, SYCL, OpenVINO, and OpenCL candidates are not downloadable in this change

## 4. Remote command

- [x] 4.1 Add failing CLI tests for `runtime` usage and `runtime remote` grammar, invalid arguments, streams, and side-effect-free behavior
- [x] 4.2 Implement latest and exact-tag compatible remote listing with optional backend selectors
- [x] 4.3 Implement bounded `--history` validation, pagination, newest-first ordering, and backend filtering
- [x] 4.4 Implement human tables exposing RELEASE, TARGET, BACKEND, ASSET, and DEPENDENCIES
- [x] 4.5 Implement strict newline-terminated JSON schema version 1 output with selection, compatibility evidence, entries, and diagnostics
- [x] 4.6 Make the unqualified human remote listing explain its latest-only default and point to `--history <count>` without affecting filtered or JSON output

## 5. Download resolution

- [x] 5.1 Add CLI tests for exact asset ID, implicit-latest backend, explicit tag/backend, malformed input, no match, and ambiguity with candidate IDs
- [x] 5.2 Implement exact primary-ID lookup against official metadata without trusting synthesized URLs or partial/dependency matches
- [x] 5.3 Implement shorthand resolution through the shared selectors and implicit host compatibility
- [x] 5.4 Guarantee that zero/multiple candidates and incomplete dependencies fail before network download or filesystem mutation

## 6. Verified staging and publication

- [x] 6.1 Implement initialized portable-installation discovery and unique owned staging beneath `downloads` and `tmp`
- [x] 6.2 Implement bounded streaming of primary/dependency assets and SHA-256 verification against each GitHub `asset.digest`
- [x] 6.3 Implement safe ZIP extraction with containment, entry-count, expanded-size, duplicate-path, link, special-file, and platform-path protections
- [x] 6.4 Implement safe TAR.GZ extraction with equivalent protections and adversarial tests
- [x] 6.5 Implement required `llama-server`/companion discovery across supported archive layouts and a bounded non-server executable probe
- [x] 6.6 Define newline-terminated `runtime.json` schema version 1 containing public ID, normalized identity, all source assets/digests, files, and timestamp
- [x] 6.7 Implement same-filesystem atomic publication, cleanup, idempotent intact reinstall, and inconsistent-destination preservation
- [x] 6.8 Wire `runtime download` progress to standard error and add end-to-end success/rollback tests for standalone and composite distributions

## 7. Local listing, removal, and locking

- [x] 7.1 Implement metadata-driven local scanning with malformed-entry diagnostics and recalculated compatibility for every valid installation
- [x] 7.2 Wire `runtime list` human/JSON output and applicable shared backend filters, including incompatible retained entries
- [x] 7.3 Implement an installation-scoped fail-fast mutation lock and apply it only to download/removal
- [x] 7.4 Implement exact-ID local selection, same-filesystem tombstone isolation, deletion, and unrelated-runtime preservation
- [x] 7.5 Wire `runtime remove` and test compatible, incompatible, absent, unsafe, busy, and partial-filesystem-failure cases

## 8. Verification and documentation

- [x] 8.1 Add an end-to-end lifecycle test covering compatible remote discovery, ambiguous shorthand, exact/composite download, idempotence, local compatibility, and exact removal
- [x] 8.2 Add fuzz tests for asset taxonomy, selectors, metadata decoding, and archive path validation
- [x] 8.3 Add cross-platform compile and path tests while preserving Windows as the first complete target
- [x] 8.4 Document the runtime-manager workflow, shorthand and exact-ID examples, compatibility boundaries, JSON output, and explicit non-goals in the root README
- [x] 8.5 Run formatting, static analysis, unit, integration, race, and repository verification and resolve every failure
- [x] 8.6 Strictly validate OpenSpec and review implementation against every scenario before archive
