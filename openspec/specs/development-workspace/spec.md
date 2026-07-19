# Development workspace

## Purpose

Define the reproducible Windows-first development baseline, initialized Go and Angular workspaces, repository conventions, and root Task orchestration used to develop Coquena.

## Requirements

### Requirement: Complete repository skeleton is tracked
The project SHALL materialize every directory in the initial repository structure documented by the README, using explicit tracked scope markers for directories whose implementation is deferred.

#### Scenario: Developer inspects a clean checkout
- **WHEN** a developer checks out the completed spike
- **THEN** the `apps/web`, `cmd/coquena`, documented `internal` packages, `docs/decisions`, and `schemas` paths are present without implying that deferred behavior is implemented

### Requirement: Windows development prerequisites are explicit
The project SHALL document Go 1.26.5, Node.js 24.18.0, npm 11.16.0, and Task 3.52.0 as the minimum supported versions required to bootstrap the Coquena development workspace on Windows.

#### Scenario: Developer reviews prerequisites
- **WHEN** a developer follows the development setup documentation from a clean Windows checkout
- **THEN** the developer can identify the required Go, Node.js, npm, and Task installations before restoring dependencies

### Requirement: Workspace dependencies are restorable through Task
The project SHALL provide a root Task command that restores the declared Go and Angular dependencies using their native package managers and committed manifests.

#### Scenario: Dependencies restore successfully
- **WHEN** a developer with the documented prerequisites runs the workspace dependency task from the repository root
- **THEN** Go module dependencies and frontend npm dependencies are restored without requiring globally installed Angular tooling

#### Scenario: A prerequisite is unavailable
- **WHEN** a required package manager or runtime is unavailable
- **THEN** the dependency task fails with a non-zero exit status and identifies the command that could not run

### Requirement: Initial backend is independently verifiable
The project SHALL contain an initialized Go module and a minimal `cmd/coquena` executable that can be formatted, tested, and built without the frontend toolchain, and whose initialization is suitable for an independent Git commit.

#### Scenario: Backend verification succeeds
- **WHEN** a developer runs the backend verification task
- **THEN** Go formatting checks, tests, and compilation complete successfully

### Requirement: Initial frontend is independently verifiable
The project SHALL contain a fully initialized Angular 22 strict standalone application under `apps/web` that uses SCSS, runs without Zone.js, uses signals for component-local reactive state, retains RxJS for asynchronous streams, can be installed, tested, and built without compiling the Go executable, and whose initialization is suitable for an independent Git commit.

#### Scenario: Frontend verification succeeds
- **WHEN** a developer restores frontend dependencies and runs the frontend verification task
- **THEN** the Angular tests and production build complete successfully using project-local tooling

#### Scenario: Application runs zoneless
- **WHEN** the initialized Angular application and its tests start
- **THEN** neither build configuration loads Zone.js and reactive template updates are driven through Angular signals

#### Scenario: Observable state is presented to a template
- **WHEN** application state originates from an RxJS Observable and is consumed by a component template
- **THEN** the Observable is converted once to a reusable signal using Angular's RxJS interoperability API

#### Scenario: Component styles use SCSS
- **WHEN** the initial application and a newly generated component are inspected
- **THEN** their component stylesheet format is SCSS

### Requirement: Entire workspace has one verification entry point
The project SHALL provide a root Task command that verifies both the Go backend and Angular frontend and returns failure if either workspace fails.

#### Scenario: Workspace verification succeeds
- **WHEN** backend and frontend checks are healthy and a developer runs the root verification task
- **THEN** both verification suites run and the command exits successfully

#### Scenario: One workspace fails verification
- **WHEN** either backend or frontend verification fails
- **THEN** the root verification task exits unsuccessfully and preserves the failing tool output

### Requirement: Generated and local artifacts remain untracked
The repository SHALL ignore dependency directories, build outputs, coverage files, local environment files, and editor artifacts produced by the initial Go and Angular workflows.

#### Scenario: Developer runs setup and builds
- **WHEN** a developer restores dependencies and builds both workspaces
- **THEN** Git does not report generated dependency caches or build outputs as source changes

### Requirement: Repository text is normalized
The project SHALL use Unix-style repository conventions on every development host by storing text files as UTF-8 with LF line endings and a final newline, expressing repository-relative paths with `/`, and avoiding host-specific shell syntax in shared Task commands.

#### Scenario: Editors use repository settings
- **WHEN** a supported editor opens or creates a repository text file
- **THEN** `.editorconfig` directs the editor to use UTF-8, LF, and a final newline

#### Scenario: Git records text content
- **WHEN** a developer stages text from Windows
- **THEN** `.gitattributes` normalizes the committed representation to LF

#### Scenario: Developer changes host operating system
- **WHEN** the repository is opened on Windows or macOS using a compatible IDE
- **THEN** text, documented paths, and shared Task commands retain the same Unix-style representation

### Requirement: Commit history follows Conventional Commits
The project SHALL use Conventional Commit messages for the Angular initialization, Go initialization, and root orchestration checkpoints.

#### Scenario: Initialization checkpoints are committed
- **WHEN** each independently verified initialization checkpoint is committed
- **THEN** its commit message uses a Conventional Commit type and an appropriate scope
