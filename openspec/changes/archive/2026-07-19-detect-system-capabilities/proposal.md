## Why

Coquena needs a reliable, reusable description of the host before a later runtime manager can choose among CPU, CUDA, Vulkan, Metal, or other llama.cpp distributions. Operators and installers need the same detection result without treating absent optional acceleration as a command failure.

## What Changes

- Add a `coquena system` command that always produces a host-capability report when inspection completes.
- Render a concise human-readable report by default and a stable, versioned JSON document with `--json`.
- Detect the operating system, native and executable architectures, CPU, total memory, graphics adapters, reliable graphics memory, and backend availability with evidence and diagnostics.
- Implement complete Windows inspection first behind a multiplatform Go abstraction; other compiled platforms return explicit `unknown` or `unsupported` capability states until their probes are implemented.
- Treat missing CUDA, Vulkan, GPUs, drivers, or optional dependencies as successful report content rather than command errors.
- Prefer Go and dynamically loaded native operating-system or driver APIs, avoiding mandatory CGO and external diagnostic commands.
- Keep release discovery, asset ranking, downloads, runtime activation, model management, and external runtime registration out of this change.

## Capabilities

### New Capabilities
- `system-capability-inspection`: Inspect and report normalized host hardware, operating-system, memory, graphics, and compute-backend capabilities for people and automated consumers.

### Modified Capabilities

None.

## Impact

- Extends the Go CLI under `cmd/coquena` with a read-only `system` command.
- Introduces normalized capability and diagnostic types plus platform-specific probes under the Go backend.
- Adds Windows-native discovery and dynamically loaded CUDA/Vulkan probes without requiring those optional facilities to be installed.
- Establishes a versioned JSON contract that future installers and the later llama.cpp runtime manager can consume.
- Adds tests with injected probes or fixtures so supported, absent, unknown, and unsupported states are deterministic and do not depend on developer hardware.
