# System capability inspection

## Purpose

Define how Coquena inspects and reports normalized host operating-system, hardware, memory, graphics, and compute-backend capabilities for operators and automated consumers.

## Requirements

### Requirement: Read-only system inspection command
Coquena SHALL expose a `coquena system` command that inspects the current host without requiring a portable installation and without changing system or installation state.

#### Scenario: Inspect before initialization
- **WHEN** an operator invokes `coquena system` before `coquena init`
- **THEN** Coquena produces a system-capability report
- **AND** creates, modifies, downloads, installs, activates, or removes no content

### Requirement: Normalized capability report
The system inspection SHALL produce one normalized report containing integer `schemaVersion` value `1`, operating-system identity and version when known, native host architecture, executable architecture, CPU identity and logical processor count, total system memory when known, zero or more graphics devices, backend capabilities, and diagnostics.

#### Scenario: Inspect a fully discoverable Windows host
- **WHEN** Coquena completes every Windows probe successfully
- **THEN** the report identifies Windows, the native and executable architectures, CPU, logical processors, total memory, discovered graphics devices, and evaluated backend capabilities

#### Scenario: Optional value cannot be determined
- **WHEN** a probe cannot determine an optional value such as graphics memory or an operating-system version detail
- **THEN** the report remains valid
- **AND** does not invent a value
- **AND** records an applicable diagnostic or unknown state

### Requirement: Explicit capability states
Each evaluated optional backend SHALL report exactly one state from `available`, `unavailable`, `unknown`, or `unsupported` and SHALL include evidence or a stable diagnostic reason sufficient to explain that state.

#### Scenario: CUDA driver and device are usable
- **WHEN** the CUDA driver API loads and enumerates a compatible NVIDIA device
- **THEN** the CUDA capability is `available`
- **AND** its evidence identifies the device and detected driver capability

#### Scenario: CUDA is absent
- **WHEN** no usable CUDA driver API or CUDA-capable device is present
- **THEN** the CUDA capability is `unavailable`
- **AND** the report contains a stable reason code describing the absence

#### Scenario: Probe result is inconclusive
- **WHEN** an unexpected probe failure prevents Coquena from establishing whether a backend is present
- **THEN** the backend capability is `unknown`
- **AND** the report contains a diagnostic for the failed probe

#### Scenario: Backend does not apply to the platform
- **WHEN** a backend cannot apply to the detected operating system or architecture
- **THEN** the backend capability is `unsupported`
- **AND** the report identifies the platform constraint

### Requirement: Missing acceleration is a successful result
Coquena SHALL treat absent GPUs, CUDA, Vulkan, Metal, drivers, loaders, and other optional acceleration dependencies as normal report data and SHALL exit successfully whenever it emits a complete valid report.

#### Scenario: CPU-only host
- **WHEN** Coquena inspects a host with no usable GPU backend
- **THEN** the report marks the CPU capability as available
- **AND** reports optional GPU backends as unavailable or unsupported as applicable
- **AND** the command exits with status `0`

#### Scenario: One optional probe fails
- **WHEN** one optional backend probe fails but Coquena can complete the report
- **THEN** the report represents that backend as unknown with a diagnostic
- **AND** the command exits with status `0`

### Requirement: Human-readable default output
Without output-format flags, `coquena system` SHALL render a concise human-readable report from the normalized capability result, including detected system characteristics, backend states, and limited remediation guidance for missing optional dependencies when guidance is known.

#### Scenario: Operator requests the default report
- **WHEN** an operator invokes `coquena system`
- **THEN** standard output presents the detected operating system, architecture, CPU, memory, graphics devices, and backend states in human-readable form
- **AND** does not expose an internal error as though an optional backend were required

### Requirement: Machine-readable JSON output
With `--json`, `coquena system` SHALL write only one valid JSON representation of the normalized report followed by a newline to standard output, SHALL emit no ANSI formatting or progress text to standard output, and SHALL reserve standard error for command-level diagnostics.

#### Scenario: Installer requests JSON
- **WHEN** an application invokes `coquena system --json`
- **THEN** standard output contains a JSON document with `schemaVersion` equal to `1`
- **AND** parsing standard output requires no removal of headings, logs, colors, or progress text

#### Scenario: JSON describes missing backends
- **WHEN** CUDA and Vulkan are unavailable and an application invokes `coquena system --json`
- **THEN** standard output remains valid JSON describing both absences
- **AND** the command exits with status `0`

### Requirement: Windows-first multiplatform behavior
The inspection architecture SHALL compile through platform-specific Go implementations, SHALL provide complete Windows inspection in this change, and SHALL still emit a valid report with explicit unknown or unsupported states on a compiled platform whose detailed probes are not yet implemented.

#### Scenario: Run on supported Windows
- **WHEN** `coquena system` runs on Windows
- **THEN** Coquena executes Windows-specific operating-system, memory, graphics, CUDA, and Vulkan probes without invoking external diagnostic commands

#### Scenario: Run on a platform with deferred detailed probes
- **WHEN** `coquena system` runs on a compiled platform whose detailed system probes are deferred
- **THEN** Coquena reports the known Go runtime operating system and executable architecture
- **AND** marks unavailable details as unknown or unsupported
- **AND** exits successfully after emitting the valid report

### Requirement: Command-level failures remain distinguishable
Coquena SHALL return a non-zero exit status only when command usage is invalid or it cannot emit the promised report because of an unrecoverable command, encoding, or output failure.

#### Scenario: Invalid system command flag
- **WHEN** an operator invokes `coquena system` with an unsupported flag
- **THEN** Coquena writes usage guidance to standard error
- **AND** exits unsuccessfully

#### Scenario: Output cannot be written
- **WHEN** Coquena cannot write the selected representation to standard output
- **THEN** Coquena reports the command-level failure on standard error when possible
- **AND** exits unsuccessfully
