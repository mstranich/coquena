## 1. CLI Contract

- [x] 1.1 Refactor the Go entry point so commands can return errors and write independently testable standard output and standard error.
- [x] 1.2 Add `coquena init` dispatch while preserving a clear diagnostic and non-zero exit for unsupported arguments.
- [x] 1.3 Add CLI tests for successful dispatch, success output, failure diagnostics, and exit behavior.

## 2. Installation Root

- [x] 2.1 Implement executable-path discovery, filesystem-link resolution, absolute path cleanup, and parent-directory selection behind an injectable test boundary.
- [x] 2.2 Add tests proving initialization uses the resolved executable directory rather than the process working directory, including linked-executable behavior where supported.

## 3. Portable Layout

- [x] 3.1 Define the required `config`, `data`, `downloads`, `logs`, `runtimes`, and `tmp` installation paths without introducing location overrides.
- [x] 3.2 Implement non-destructive required-directory creation and detect non-directory path conflicts.
- [x] 3.3 Add tests for new layout creation, safely completing missing directories, preserving existing content, non-directory conflicts, and unwritable-path errors.

## 4. JSON Configuration

- [x] 4.1 Define the minimal typed configuration with `schemaVersion` value `1` and deterministic two-space-indented JSON serialization with a final newline.
- [x] 4.2 Implement same-directory temporary writing, validation, atomic publication, and temporary-file cleanup for `config/coquena.json`.
- [x] 4.3 Implement existing-configuration validation that accepts only valid JSON with the supported schema version and never overwrites the file.
- [x] 4.4 Add tests for exact initial JSON, atomic publication failure, invalid JSON, unsupported schema versions, repeated initialization, and byte-for-byte configuration preservation.

## 5. Integration and Documentation

- [x] 5.1 Integrate root resolution, directory initialization, configuration publication, and new-versus-existing results into the `init` command.
- [x] 5.2 Add an end-to-end CLI test that initializes from a different working directory and verifies every effect remains under the executable root.
- [x] 5.3 Document the portable `coquena init` workflow, generated layout, JSON-only application configuration, permissions expectations, and explicit non-goals.
- [x] 5.4 Run Go formatting and tests, the repository verification task, and `openspec validate --all`; resolve every failure.
