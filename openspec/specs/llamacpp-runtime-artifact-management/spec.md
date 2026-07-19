# llama.cpp runtime artifact management

## Purpose

Define how Coquena discovers host-compatible official llama.cpp distributions and safely downloads, lists, and removes immutable local runtime installations.

## Requirements

### Requirement: Runtime command family
Coquena SHALL expose `runtime remote`, `runtime download`, `runtime list`, and `runtime remove` for official llama.cpp distributions and SHALL reject unsupported operations without modifying runtime state.

#### Scenario: Request runtime usage
- **WHEN** an operator invokes `coquena runtime` without a subcommand
- **THEN** Coquena displays supported operations
- **AND** changes no local content

### Requirement: Normalized official distribution catalog
Coquena SHALL recognize official primary llama.cpp distributions separately from dependency and non-runtime release assets and SHALL preserve release, target, OS, native architecture, backend, backend qualifiers, exact primary asset name, dependencies, URLs, sizes, and GitHub-provided digests as distinct fields.

#### Scenario: Recognize a standalone distribution
- **WHEN** a release contains a Windows x64 Vulkan primary asset
- **THEN** the catalog represents one GPU-targeted Vulkan distribution with that exact asset name

#### Scenario: Recognize composite Windows CUDA
- **WHEN** a release contains matching Windows x64 CUDA primary and `cudart` assets
- **THEN** the catalog represents one CUDA distribution whose primary ID is the `llama-...` filename
- **AND** associates the `cudart` filename as a dependency

#### Scenario: Preserve backend qualifiers
- **WHEN** assets distinguish CUDA toolkit versions, ROCm/OpenVINO versions, SYCL precision, or backend flavors
- **THEN** the normalized catalog preserves those qualifiers without collapsing distinct distributions

#### Scenario: Exclude non-runtime assets
- **WHEN** a release contains UI, XCFramework, source, dependency-only, unknown, or ambiguous assets
- **THEN** Coquena does not expose them as independently downloadable runtime distributions
- **AND** reports applicable catalog diagnostics

### Requirement: Conservative taxonomy
Coquena SHALL centralize and version upstream interpretation, combine known platform build semantics with exact filename rules, preserve raw upstream names, and SHALL NOT guess when an asset cannot be classified unambiguously.

#### Scenario: Backend is implicit in build semantics
- **WHEN** an official platform build has a known backend not encoded in its filename
- **THEN** the catalog applies the explicit platform rule
- **AND** retains evidence identifying that rule

#### Scenario: Upstream introduces an unknown pattern
- **WHEN** an asset does not match a recognized primary or dependency family
- **THEN** Coquena skips it with a diagnostic rather than deriving an identity by substring

### Requirement: Host-native compatibility
Coquena SHALL evaluate each distribution as `compatible`, `incompatible`, or `unknown` using the current `coquena system` report, SHALL require exact current OS and native-architecture matches, and SHALL NOT treat executable emulation as compatibility.

#### Scenario: Matching CPU distribution
- **WHEN** a CPU distribution matches the host OS and native architecture
- **THEN** its compatibility is `compatible`

#### Scenario: Architecture differs
- **WHEN** a distribution targets an architecture other than the host native architecture
- **THEN** its compatibility is `incompatible`
- **AND** executable architecture or OS emulation does not change that result

#### Scenario: Detected backend is available
- **WHEN** a platform-matching CUDA or Vulkan distribution has a corresponding `available` backend capability and satisfies its version policy
- **THEN** its compatibility is `compatible`
- **AND** the result includes supporting system evidence

#### Scenario: Backend is absent
- **WHEN** the corresponding backend capability is `unavailable` or `unsupported`
- **THEN** the distribution is `incompatible`

#### Scenario: Backend is not established
- **WHEN** the corresponding backend capability is `unknown` or is not evaluated by the system report
- **THEN** the distribution compatibility is `unknown`
- **AND** it cannot be downloaded

### Requirement: CUDA driver compatibility policy
Coquena SHALL evaluate CUDA runtime families through an explicit tested driver-compatibility policy that permits supported older CUDA runtimes on newer drivers and SHALL NOT require exact equality between reported driver capability and toolkit asset version.

#### Scenario: New driver supports older runtime
- **WHEN** the detected NVIDIA driver supports CUDA 13 and an official compatible-platform CUDA 12 distribution is evaluated
- **THEN** Coquena does not reject it merely because the reported and asset versions differ

#### Scenario: Driver is too old
- **WHEN** the detected driver does not meet the policy for the selected CUDA runtime family
- **THEN** the distribution is `incompatible` with an explicit version reason

### Requirement: Fast compatible remote listing
Without selectors, `coquena runtime remote` SHALL query the latest official non-draft, non-prerelease release and list only distributions compatible with the current host, including release, target, normalized backend, exact primary `ASSET`, and `DEPENDENCIES`. Human output for this unqualified invocation SHALL state that only the latest compatible release is shown and SHALL direct the operator to `--history <count>` for previous releases; the hint SHALL NOT appear for explicit selectors, explicit release/history requests, or JSON output.

#### Scenario: List current Windows choices
- **WHEN** a Windows x64 host has CPU, CUDA, and Vulkan available and the latest release publishes matching assets
- **THEN** the result contains only the compatible Windows x64 CPU, CUDA, and Vulkan rows
- **AND** excludes other systems, architectures, unavailable backends, and unknown backends

#### Scenario: Explain the unqualified latest-only default
- **WHEN** an operator invokes `coquena runtime remote` without arguments in human-output mode
- **THEN** Coquena states that only the latest compatible release is shown
- **AND** identifies `--history <count>` as the way to include previous releases

#### Scenario: Omit the latest-only hint outside the unqualified human view
- **WHEN** an operator supplies an explicit selector, release, history request, or `--json`
- **THEN** Coquena does not emit the unqualified latest-only hint

#### Scenario: Remote source fails
- **WHEN** GitHub release metadata cannot be retrieved because of network, proxy, rate-limit, authentication, or upstream failure
- **THEN** the command exits unsuccessfully with an actionable diagnostic
- **AND** changes no local state

### Requirement: Shared remote selectors
Remote listing SHALL accept an optional `latest` or exact `b<digits>` release tag and an optional backend selector from `cpu`, `vulkan`, `cuda`, or `cuda@<major>[.<minor>]`; omitted release SHALL mean `latest`, and results SHALL retain only exact compatible matches.

#### Scenario: Filter latest CUDA 13 family
- **WHEN** an operator invokes `coquena runtime remote cuda@13`
- **THEN** Coquena lists compatible CUDA 13.x distributions from the latest release

#### Scenario: Filter a concrete release
- **WHEN** an operator invokes `coquena runtime remote b10068 vulkan`
- **THEN** every result belongs to `b10068` and matches Vulkan

#### Scenario: Broad CUDA selector
- **WHEN** an operator invokes `coquena runtime remote cuda`
- **THEN** all compatible CUDA variants in the selected release are listed separately

#### Scenario: Valid selector has no matches
- **WHEN** valid selectors match no compatible distribution
- **THEN** the command succeeds with an empty result

#### Scenario: Selector is invalid
- **WHEN** a tag or backend selector is malformed or unsupported
- **THEN** the command exits unsuccessfully with usage guidance

### Requirement: Bounded remote history
`runtime remote --history <count> [<backend-selector>]` SHALL scan at most the requested bounded number of newest official releases and apply the same compatibility, filtering, ordering, and diagnostics as latest listing.

#### Scenario: Search recent Vulkan distributions
- **WHEN** an operator requests history count 20 and selector `vulkan`
- **THEN** Coquena examines no more than the 20 newest eligible releases
- **AND** returns compatible Vulkan distributions newest first

#### Scenario: History bound is invalid
- **WHEN** count is zero, negative, non-numeric, or above the documented maximum
- **THEN** Coquena rejects the command before a remote request

### Requirement: Human and JSON listing output
Remote and local listing SHALL render concise human output by default and, with `--json`, SHALL write only one newline-terminated JSON document with integer `schemaVersion` 1, applied selection, entries, and diagnostics to standard output.

#### Scenario: Automation lists remote distributions
- **WHEN** `coquena runtime remote --json` succeeds
- **THEN** standard output contains only the JSON result
- **AND** every row separates release, target, backend, primary asset, dependencies, and compatibility evidence

#### Scenario: Automation lists local distributions
- **WHEN** `coquena runtime list --json` succeeds
- **THEN** standard output contains all valid local entries and their recalculated compatibility states

### Requirement: Exact and shorthand download selection
`runtime download` SHALL accept either one exact primary asset ID, one backend selector with implicit `latest`, or an explicit release tag followed by a backend selector, and SHALL resolve exactly one compatible primary distribution before performing network download or filesystem mutation.

#### Scenario: Download exact asset ID
- **WHEN** an operator supplies `llama-b10068-bin-win-cuda-13.3-x64.zip`
- **THEN** Coquena verifies that exact primary asset against its official release and current-host compatibility
- **AND** selects no partial or similarly named asset

#### Scenario: Download latest through shorthand
- **WHEN** an operator invokes `coquena runtime download cuda@13`
- **THEN** Coquena resolves `cuda@13` against the latest eligible release and implicit host constraints

#### Scenario: Download concrete release through shorthand
- **WHEN** an operator invokes `coquena runtime download b10068 vulkan`
- **THEN** Coquena resolves the selector only within `b10068`

#### Scenario: Shorthand is ambiguous
- **WHEN** the selection produces multiple compatible primary distributions
- **THEN** Coquena exits unsuccessfully and lists their exact asset IDs as refinement choices
- **AND** performs no download or filesystem mutation

#### Scenario: Selection has no match
- **WHEN** the selection produces no compatible primary distribution
- **THEN** Coquena exits unsuccessfully with the compatibility or not-found reason
- **AND** performs no mutation

#### Scenario: Exact asset is a dependency or unsafe name
- **WHEN** the supplied ID names a dependency-only asset, contains a path separator, or does not match an official primary asset exactly
- **THEN** Coquena rejects it without constructing or trusting a download URL from the input

### Requirement: Complete dependency resolution
Before downloading bytes, Coquena SHALL resolve every required dependency from the same official release and SHALL fail without mutation when a required dependency is missing or ambiguous.

#### Scenario: Resolve Windows CUDA dependency
- **WHEN** one Windows CUDA primary distribution is selected
- **THEN** Coquena attaches the matching same-version, same-architecture `cudart` asset

#### Scenario: Dependency is absent
- **WHEN** the matching `cudart` asset is absent
- **THEN** Coquena reports an incomplete upstream distribution
- **AND** downloads nothing

### Requirement: Verified atomic publication
Download SHALL require a supported SHA-256 `digest` from official GitHub metadata for every asset, stage bounded content beneath the initialized portable installation, reject unsafe archives, require and probe `llama-server`, write versioned metadata, and atomically publish only after every check succeeds.

#### Scenario: Composite download succeeds
- **WHEN** primary and dependency assets pass digest, extraction, file, and probe validation
- **THEN** Coquena publishes one complete runtime beneath `runtimes/llama.cpp`
- **AND** records the primary public ID and metadata for every contributing asset

#### Scenario: Official digest is missing or mismatched
- **WHEN** any required asset lacks a supported digest or downloaded bytes do not match it
- **THEN** Coquena publishes no runtime and preserves prior installations

#### Scenario: Archive entry is unsafe
- **WHEN** an archive entry escapes staging, collides, exceeds limits, is a link, or has an unsupported special type
- **THEN** Coquena rejects the distribution and publishes nothing

#### Scenario: Runtime validation fails
- **WHEN** required files are absent or the bounded non-server probe fails
- **THEN** Coquena publishes nothing and preserves prior installations

#### Scenario: Installation is not initialized
- **WHEN** download runs without a valid portable installation
- **THEN** it exits unsuccessfully with initialization guidance
- **AND** writes no runtime elsewhere

### Requirement: Idempotent immutable installation
Downloading an already installed primary asset ID SHALL succeed without replacement when metadata/content remain valid and SHALL report a conflict without overwriting inconsistent content.

#### Scenario: Repeat intact installation
- **WHEN** the selected public ID is installed and validates
- **THEN** download reports it already installed and leaves bytes unchanged

#### Scenario: Existing installation conflicts
- **WHEN** its destination exists with invalid metadata or required files
- **THEN** download exits unsuccessfully and preserves the conflicting content

### Requirement: Metadata-driven complete local listing
`runtime list` SHALL list every valid managed installation regardless of current compatibility, use the exact primary asset filename as public ID, recalculate compatibility, support applicable backend filters, and report malformed entries as diagnostics.

#### Scenario: Hardware capability changed
- **WHEN** a previously compatible local CUDA runtime becomes incompatible or unknown after a driver/hardware change
- **THEN** local listing still shows it with its current compatibility state

#### Scenario: Local entry is malformed
- **WHEN** metadata is missing, invalid, or unsupported
- **THEN** listing reports a diagnostic and does not invent a valid runtime identity

### Requirement: Exact safe removal
`runtime remove <primary-asset-id>` SHALL select one valid local runtime by exact public ID, isolate it through same-filesystem rename, delete only its tombstone, and SHALL allow removal regardless of current compatibility.

#### Scenario: Remove incompatible local runtime
- **WHEN** an exact installed ID is currently incompatible
- **THEN** Coquena removes that installation and preserves every other ID

#### Scenario: ID is absent or malformed
- **WHEN** the supplied ID is absent, partial, unsafe, or malformed
- **THEN** Coquena exits unsuccessfully and removes nothing

#### Scenario: Removal cannot complete
- **WHEN** isolation or deletion fails
- **THEN** Coquena identifies the affected path and does not modify unrelated installations

### Requirement: Concurrent mutation safety
Download publication and removal SHALL use an installation-scoped exclusive fail-fast mutation lock while read-only listing remains lock-free.

#### Scenario: Another mutation owns the lock
- **WHEN** a mutation starts while another holds the installation lock
- **THEN** Coquena exits with a busy diagnostic and makes no partial mutation
