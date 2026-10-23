## Coquena 0.2.0 — M2: catalog and resource management

Las referencias Git de Coquena de este documento apuntan a sus equivalentes en el historial saneado. Los hashes de modelos y backends externos se conservan.

A little guardian that keeps your llamas organized and ready to work.

This release extends the Windows x64 CLI with:

- Backend update detection, upgrades, build retention, and external backend registration.
- External model registration, managed companions, local metadata refresh, and integrity checks.
- Model update plans and upgrades, plus explicit transitions from external to managed resources.
- Parameter definitions and support associations, preset cloning, and resource bindings.
- Configurable managed directories and resource removal with ownership and active-use checks.
- The existing Router Mode server lifecycle, metrics, and logs.

### Download and run

Download `coquena-0.2.0-win-x64.zip`, extract it, and run `coquena.exe --help`
in PowerShell. The ZIP includes the Native AOT executable, English README, and
MIT license. No .NET installation is required. Models and llama.cpp backends
are downloaded separately and retain their own licenses.

`SHA256SUMS.txt` contains the checksum of the ZIP. Verify it with:

```powershell
Get-FileHash .\coquena-0.2.0-win-x64.zip -Algorithm SHA256
```

ZIP SHA-256: `d184d47011b09f567ee59ec9881e6696a9d85adcea1228c5a73fd7fc9c32444c`.

### Validation and build provenance

The executable was built locally using the pinned .NET SDK 10.0.401 and Native AOT
for Windows x64. Its product version is
`0.2.0+ad0b61aa2bc44115e3e3959ff0279bfc67896fa4`. It was built before the release
documentation commit; runtime sources, tests, and global.json are unchanged
between that build commit and the release commit.

The complete native test suite passed against the extracted executable, whose
SHA-256 matches the published build output. The procedure used the actual path
capitalization and a UTF-8 console, as explained in the known issues below.

A real local walkthrough installed CUDA 12 b11696 and the official Ornith 1.5
9B and 35B-A3B Q4_K_M models, configured three presets including a separate MTP
companion, and exercised inference, structured tool calls, router model switching,
and metrics. Both models loaded with an explicit 262,144-token context and one
slot. Requests did not fill that context, and an external coding harness was not
connected. Some bounded performance requests exhausted their output budget.
A generated Python response initially contained a failing assertion; a subsequent
correction passed four assertions. These are small diagnostics, not a coding-quality
benchmark or proof of universally optimal settings.

### Known issues

The manual walkthrough was partially successful and exposed unresolved issues.
This release is published with those issues explicitly acknowledged:

- **Download checkpoints under contention (036, high priority):** a long-held
  home writer lock can interrupt checkpoint persistence. The partial file may then
  exceed its recorded offset, and resume rejects it as `checkpoint-inconsistent`.
  Model publication currently copies and hashes whole files inside that lock.
  Run large installations sequentially and avoid concurrent home modifications;
  an affected operation may require cancellation and a fresh download. No bypass
  of integrity checks is included.
- **MTP companion classification (037):** a separate draft named `mtpdraft` may
  be omitted, while a full model containing an `MTP` token in its name may appear
  as an auxiliary candidate. Check the exact selected file and size before
  confirming a download. A name alone does not certify inference compatibility.
- **Windows path assertions in tests (038):** some runtime test comparisons are
  case-sensitive and can reject equivalent Windows paths. The completed validation
  used the actual directory capitalization.
- **Process-output encoding in tests (039):** the log round-trip check depends
  on the console environment. Validation passed with `chcp 65001`; the precise
  producer/decoder boundary still requires investigation.

The four correction tasks remain planned; this release does not claim they are fixed.
The source tree includes their acceptance criteria and the detailed Ornith evidence
under `specs/035-m2-entrega-integrada/prueba-ornith.md`.

### Current scope

- Windows x64 only. CLI messages and detailed functional documentation are in Spanish.
- Declarative server definitions and parameter exchange remain future work.
- The 0.x series is under development; commands and data formats may change.

Coquena was conceived and designed by Maximiliano Stranich.

Copyright © 2026 Maximiliano Stranich. Licensed under MIT.
