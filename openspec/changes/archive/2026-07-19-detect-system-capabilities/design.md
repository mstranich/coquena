## Context

Coquena currently exposes only `coquena init`, and `internal/system` is a reserved package. A later SDKMAN-like llama.cpp runtime manager needs a reusable host description before it can match official or external runtime variants, but this change must remain read-only and independent of installation state, GitHub releases, and runtime policy.

The detector must distinguish hardware presence from API usability. A graphics adapter can exist while CUDA or Vulkan is absent, unusable, or indeterminate. Missing optional acceleration is expected on healthy systems and therefore belongs in the result rather than the process exit status.

Windows is the first fully supported product runtime. The code structure must preserve later Linux and macOS implementations without requiring all platform probes in this change. The executable should remain easy to cross-compile and distribute, so mandatory CGO and linked vendor SDKs are undesirable.

## Goals / Non-Goals

**Goals:**

- Provide `coquena system` and `coquena system --json` over one normalized result.
- Fully inspect Windows OS, architectures, CPU, memory, display adapters, CUDA, and Vulkan capabilities.
- Represent available, unavailable, unknown, and unsupported states with evidence and stable diagnostics.
- Return a successful report for CPU-only hosts and partial optional-probe failures.
- Keep platform probes replaceable and unit-testable without requiring particular developer hardware.
- Compile on planned Go platforms and produce a useful baseline report where detailed probes are deferred.

**Non-Goals:**

- Discovering llama.cpp releases or matching release assets.
- Ranking, downloading, installing, activating, upgrading, rolling back, or removing runtimes.
- Registering official or externally supplied llama.cpp builds.
- Managing models or estimating whether a model fits in memory.
- Benchmarking CPU, GPU, or backend performance.
- Inspecting free space for a not-yet-selected installation directory.
- Adding UI or HTTP API exposure.

## Decisions

### Use one normalized report and separate renderers

`internal/system` will own exported report types, inspection orchestration, and a detector interface. The CLI will request one report and pass it to either a text renderer or JSON encoder. The text renderer will not perform additional probes or reinterpret failures differently from JSON.

The report will use `schemaVersion: 1`, byte counts as integers, stable lowercase enum values, stable diagnostic codes, and deterministic ordering of devices, capabilities, and diagnostics. Unknown optional values will be omitted or represented explicitly according to the field contract; zero will not be used to guess an unknown measurement.

Alternative considered: build human and JSON paths independently. Rejected because they would drift and make installer behavior differ from operator diagnostics.

### Separate detection, capability evaluation, and presentation

Inspection will progress through three boundaries:

1. Platform inventory gathers facts about the OS, CPU, memory, and graphics devices.
2. Backend probes evaluate CPU, CUDA, Vulkan, and platform applicability against that inventory.
3. Renderers present the normalized report.

No release asset or preferred llama.cpp variant is selected. Limited remediation text is derived from stable diagnostic codes, not from arbitrary probe error strings.

Alternative considered: return a single `bestBackend`. Rejected because selection requires release contents and user/runtime policy that belong to the next feature.

### Model optional probes as results rather than Go errors

Expected absence, unsupported platforms, access limitations, and individual optional-probe failures produce capability states and diagnostics. The orchestration method returns an error only if it cannot construct a valid report at all. CLI exit status therefore describes completion of the reporting protocol, not availability of desirable acceleration.

Probe diagnostics will retain stable codes and concise sanitized messages. They will not expose raw pointers, stack traces, or environment secrets in JSON.

Alternative considered: fail fast when a driver library cannot load. Rejected because CPU-only systems and missing optional backends are valid targets.

### Use platform files and injected probe functions

Common types and orchestration will remain in platform-neutral Go files. Implementations selected by filename suffixes or `//go:build` constraints will supply host facts and backend probes. Tests will inject deterministic probes and use platform-specific integration tests only for safe invariants.

The first implementation will contain complete Windows probes. Non-Windows implementations in this change will report facts available from the Go runtime and explicit unknown/unsupported states rather than failing or pretending detailed detection occurred.

Alternative considered: implement Windows, Linux, and macOS native discovery simultaneously. Rejected to preserve the Windows-first baby step and keep each later platform reviewable.

### Prefer native APIs loaded without mandatory CGO

Windows code will use Go runtime information plus `golang.org/x/sys/windows` or equivalent direct system calls for native architecture and memory, native graphics enumeration for adapters, and dynamically loaded driver APIs for CUDA and Vulkan. CUDA inspection will treat the driver API as authoritative for initialization, driver capability, and device enumeration. Vulkan inspection will require the loader to initialize and enumerate physical devices rather than merely checking for a DLL.

`nvidia-smi`, `vulkaninfo`, PowerShell, WMI command-line utilities, and shell pipelines will not be runtime dependencies. Optional DLLs will be loaded only by their trusted system names or locations, called through narrow wrappers, and released safely. Vendor headers or SDKs may inform constants during development but are not installed prerequisites for users.

Alternative considered: execute diagnostic commands and parse their text. Rejected because commands may be absent, localized, version-dependent, or unsuitable for a portable installer contract.

### Correlate devices with stable identifiers where available

Graphics inventory and backend probes will expose device-local identifiers such as vendor/device IDs, PCI location, LUID, or API UUID when available. Correlation will be best-effort and evidence-based; matching display names alone will not assert device identity. A backend capability may remain available with partial correlation, accompanied by a diagnostic.

Alternative considered: use display names as primary keys. Rejected because names are neither unique nor stable.

### Keep executable and native host architectures distinct

The report will contain both the architecture of the running Go executable and the native host architecture when discoverable. This preserves correct behavior under emulation and prevents a future installer from selecting an asset solely from `runtime.GOARCH`.

## Risks / Trade-offs

- **Native API wrappers add low-level complexity** → Keep wrappers narrow, isolate them behind interfaces, and test orchestration with fakes plus focused Windows integration tests.
- **Driver and Vulkan APIs evolve** → Call only stable discovery entry points, version the report rather than exposing raw native structures, and convert unknown return codes into diagnostics.
- **Graphics-memory reporting can be inaccurate or unavailable** → Report it only when a probe provides a trustworthy value and otherwise leave it unknown.
- **Device correlation may be incomplete across DXGI, CUDA, and Vulkan** → Retain source-specific evidence and never claim identity from display names alone.
- **A JSON schema can become an accidental permanent API** → Start with a narrow versioned contract, deterministic encoding, golden tests, and additive evolution within schema version rules.
- **Deferred Linux and macOS probes may appear less capable than the host** → Mark them explicitly unknown or unsupported and avoid presenting absence as proven when no probe ran.
- **Dynamic library loading can expand DLL search risk** → Use trusted system library names/locations and avoid caller-controlled library paths.

## Migration Plan

1. Introduce the normalized report, states, diagnostics, renderers, and injected detector contract without changing `coquena init` behavior.
2. Add the `system` CLI dispatch and verify both commands independently.
3. Add platform-neutral and deferred-platform detection.
4. Add Windows host inventory, graphics, CUDA, and Vulkan probes in independently tested steps.
5. Validate the JSON contract with fixtures and the real executable with optional backends both present and absent.

Rollback consists of removing the new command and `internal/system` implementation; no persisted configuration or host state requires migration.

## Open Questions

None blocking this change. Linux and macOS native probe details, runtime selection policy, and external-runtime registration intentionally belong to later changes.
