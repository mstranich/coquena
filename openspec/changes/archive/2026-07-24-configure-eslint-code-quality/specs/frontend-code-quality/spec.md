## ADDED Requirements

### Requirement: Configurable frontend lint baseline
The repository SHALL provide an ESLint 10 flat configuration for Angular frontend TypeScript and HTML template sources. The configuration SHALL use supported Angular and TypeScript integrations and SHALL exclude generated and dependency directories.

#### Scenario: Linting supported frontend sources
- **WHEN** a developer runs the frontend lint check in a restored workspace
- **THEN** ESLint analyzes the configured TypeScript and Angular template sources using the committed flat configuration

#### Scenario: Ignoring generated inputs
- **WHEN** the frontend lint check is run
- **THEN** dependency, build-output, and Angular cache directories are not analyzed

### Requirement: Review-oriented style enforcement
The frontend lint baseline SHALL use ESLint Stylistic rules for explicitly configured, auto-fixable readability conventions that do not conflict with dprint. dprint SHALL enforce the repository's two-space indentation, LF line endings, and final newline; the lint baseline SHALL prohibit multiple statements on one line for linted TypeScript sources.

#### Scenario: Detecting compacted TypeScript
- **WHEN** a linted TypeScript file contains multiple statements on one line or violates a configured layout rule
- **THEN** the frontend lint check reports a lint error identifying the rule and source location

#### Scenario: Applying a safe style correction
- **WHEN** a developer runs the explicit frontend lint fix command for an auto-fixable configured violation
- **THEN** ESLint applies the available safe fix to the affected source file

### Requirement: Explicit lint workflow
The frontend workspace SHALL expose separate npm commands for non-mutating lint verification and explicit lint fixing. Workspace verification SHALL run the non-mutating frontend format check and lint command before frontend tests and build, and SHALL fail when either check reports errors.

#### Scenario: Verification succeeds for compliant sources
- **WHEN** all configured frontend lint rules pass
- **THEN** the frontend verification task continues to tests and production build without modifying source files after the format check also passes

#### Scenario: Verification fails for a lint violation
- **WHEN** the frontend format check or lint check reports an error
- **THEN** the frontend verification task fails before running frontend tests or build and does not modify tracked source files

### Requirement: Configurable Angular source formatting
The frontend workspace SHALL provide a committed dprint configuration that formats TypeScript and external Angular HTML templates without Prettier. The formatter configuration SHALL use the project's review-oriented conventions, including two-space indentation, LF line endings, a 160-column line width, and the configured TypeScript quote, semicolon, and trailing-comma policy.

#### Scenario: Formatting a compacted Angular template
- **WHEN** a developer runs the explicit frontend formatting command on an external Angular template containing compacted sibling elements
- **THEN** dprint rewrites the template into consistently indented, multi-line markup according to the committed configuration

#### Scenario: Checking formatting without mutation
- **WHEN** a developer runs the non-mutating frontend format check against an unformatted TypeScript or Angular HTML source
- **THEN** the command reports the formatting violation and does not modify the source file

### Requirement: No Prettier workflow
The frontend development workflow SHALL not require Prettier or expose Prettier-based scripts.

#### Scenario: Restoring frontend dependencies
- **WHEN** a developer restores dependencies from the committed frontend lockfile
- **THEN** the restored development dependencies do not include Prettier as a direct project dependency and the formatter workflow uses dprint
