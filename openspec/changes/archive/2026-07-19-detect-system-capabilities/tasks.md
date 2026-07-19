## 1. Normalized capability contract

- [x] 1.1 Replace the `internal/system` scope marker with report, operating-system, CPU, memory, graphics-device, backend-capability, evidence, and diagnostic types using the specified schema and state values.
- [x] 1.2 Add validation and deterministic normalization tests that reject invalid states while preserving unknown optional values without inventing zero values.
- [x] 1.3 Define injectable inventory and backend-probe interfaces and test orchestration of successful, absent, unsupported, and inconclusive probes.

## 2. Output representations

- [x] 2.1 Implement the version-1 JSON encoder and golden tests for available, unavailable, unknown, unsupported, and CPU-only reports.
- [x] 2.2 Verify JSON output is deterministic, contains exactly one newline-terminated document, and emits no presentation or progress text.
- [x] 2.3 Implement the concise human renderer from the same normalized report and test detected facts, backend states, diagnostics, and known remediation guidance.

## 3. CLI command boundary

- [x] 3.1 Refactor command dispatch so existing `coquena init` behavior remains unchanged while `coquena system` invokes an injected inspector.
- [x] 3.2 Add `coquena system` default-output tests that prove inspection works before portable initialization and performs no installation operations.
- [x] 3.3 Add `coquena system --json` tests that keep stdout strictly parseable and return status `0` for missing or inconclusive optional backends.
- [x] 3.4 Add tests for unsupported flags and output-write failures as the command-level non-zero cases.

## 4. Platform-neutral and deferred-platform inspection

- [x] 4.1 Implement common Go runtime detection for operating-system family, executable architecture, logical processor count, and the always-available CPU backend.
- [x] 4.2 Add build-constrained detector factories so every compiled target has one platform implementation without runtime branching across native APIs.
- [x] 4.3 Implement the deferred non-Windows detector that emits a valid baseline report with explicit unknown or unsupported detailed capabilities.
- [x] 4.4 Cross-compile or compile-check the supported Windows, Linux, and macOS Go targets and test that deferred platforms preserve the JSON contract.

## 5. Windows host inventory

- [x] 5.1 Add the minimal `golang.org/x/sys/windows` dependency and a narrow injectable wrapper for trusted Windows system-library calls without CGO.
- [x] 5.2 Detect native Windows architecture separately from executable architecture and cover native and emulated-architecture cases with tests.
- [x] 5.3 Detect Windows identity/version, CPU identity, and total physical memory, preserving unknown fields and diagnostics when individual calls are inconclusive.
- [x] 5.4 Add safe Windows integration tests that assert report validity and basic host invariants without depending on a particular CPU model or memory size.

## 6. Windows graphics inventory

- [x] 6.1 Implement native Windows graphics-adapter enumeration with stable source identifiers, vendor/device data, adapter type, and graphics memory only when reliable.
- [x] 6.2 Normalize and deterministically order multiple hardware adapters while excluding or explicitly identifying software adapters.
- [x] 6.3 Add wrapper-level fixtures for zero, one, multiple, software, and partially unreadable adapters plus a hardware-independent Windows integration test.

## 7. Windows CUDA capability

- [x] 7.1 Add a narrow dynamic CUDA driver-library wrapper that loads only the trusted system library and exposes initialization, driver-version, device enumeration, and stable device evidence without requiring the CUDA Toolkit.
- [x] 7.2 Map absent libraries, no devices, supported devices, unsupported platform, and unexpected API failures to the specified capability states and stable diagnostic codes.
- [x] 7.3 Correlate CUDA devices with graphics inventory only from stable identifiers when available and preserve partial evidence when correlation is inconclusive.
- [x] 7.4 Add deterministic CUDA wrapper tests and an optional real-host integration test that passes whether CUDA is available or unavailable.

## 8. Windows Vulkan capability

- [x] 8.1 Add a narrow dynamic Vulkan loader wrapper that loads only the trusted system library and enumerates instance capability and physical devices without requiring `vulkaninfo`.
- [x] 8.2 Map absent loader, zero physical devices, supported devices, unsupported platform, and unexpected API failures to the specified capability states and stable diagnostic codes.
- [x] 8.3 Correlate Vulkan physical devices with graphics inventory only from stable identifiers when available and retain source-specific evidence otherwise.
- [x] 8.4 Add deterministic Vulkan wrapper tests and an optional real-host integration test that passes whether Vulkan is available or unavailable.

## 9. End-to-end verification

- [x] 9.1 Run the real Windows `coquena system` and `coquena system --json` commands and verify that human and JSON representations describe the same normalized capabilities.
- [x] 9.2 Verify the command exits successfully on fixtures for CPU-only, missing-driver, missing-loader, multi-GPU, and individual-probe-failure hosts.
- [x] 9.3 Run Go formatting, unit tests, Windows integration tests, cross-platform compile checks, and the repository backend verification task.
- [x] 9.4 Document the new command and its versioned JSON/exit-status contract without documenting deferred runtime installation or selection as implemented behavior.
