## ADDED Requirements

### Requirement: Unified development hot-reload command
The repository SHALL provide a documented root Task command that starts the Angular development server and the Coquena WebUI backend concurrently. It SHALL configure the backend with a persistent repository `home/` application home and SHALL keep watching after a backend source compilation failure.

#### Scenario: Start both development services
- **WHEN** a developer runs the documented development task in a restored workspace with required tools available
- **THEN** Angular serves the WebUI in development mode
- **AND** the Go WebUI backend starts with the development application home selected
- **AND** both process outputs remain observable until the developer interrupts the task
- **AND** the supervisor reports its PID plus the Air and Angular root PIDs

#### Scenario: Interrupt development once
- **WHEN** a developer presses Ctrl+C once while the unified development task is active
- **THEN** the supervisor allows a brief graceful-shutdown interval
- **AND** it terminates any remaining supervised descendants
- **AND** ports 4200 and 8080 are released

#### Scenario: Supervisor terminates abruptly
- **WHEN** the compiled development supervisor is forcibly terminated
- **THEN** the operating-system process ownership mechanism terminates Air, Angular, the backend, and their supervised descendants
- **AND** ports 4200 and 8080 are released

#### Scenario: Backend source changes
- **WHEN** a developer changes a watched Go source file while the development task is running
- **THEN** the watcher rebuilds and restarts the backend
- **AND** the restarted backend uses the same development application home

#### Scenario: Backend compilation fails
- **WHEN** a watched Go change does not compile
- **THEN** the watcher reports the compilation error
- **AND** it continues watching for a later corrected change
- **AND** it does not initialize a different application home

### Requirement: Repository development home
The repository SHALL retain an empty `home/` directory through a committed marker and SHALL ignore every other entry beneath it. The unified development task SHALL select that directory as `COQUENA_HOME`.

#### Scenario: Fresh checkout starts development
- **WHEN** a developer restores a checkout and runs the unified development task
- **THEN** the repository `home/` directory exists before initialization
- **AND** Coquena initializes and reuses state beneath that directory
- **AND** mutable development state is not reported as an untracked Git change

### Requirement: Disposable development build directory
The repository SHALL place development executables and watcher artifacts beneath ignored `.tmp/dev/`, separate from persistent application state in `home/`.

#### Scenario: Backend rebuilds
- **WHEN** Air rebuilds the development backend
- **THEN** the executable is written beneath `.tmp/dev/`
- **AND** no executable or watcher temporary file is written beneath `home/`

### Requirement: Clean mutable development state
The repository SHALL provide `task clean` to remove mutable contents of `home/` while preserving `home/.gitkeep`, and to remove `.tmp/dev/` completely.

#### Scenario: Clean an initialized development home
- **WHEN** a developer runs `task clean` after stopping development processes
- **THEN** downloaded runtimes, configuration, logs, temporary files, and development executables beneath `home/` are removed
- **AND** `home/.gitkeep` remains present
- **AND** `.tmp/dev/` no longer exists

#### Scenario: Clean encounters a locked artifact
- **WHEN** a development process still locks a file selected for cleanup
- **THEN** `task clean` exits unsuccessfully with the filesystem error visible
- **AND** it does not retry, kill processes, or hide the failure

### Requirement: Development API proxy
The Angular development server SHALL proxy relative `/api` requests to the local Coquena WebUI backend without requiring browser CORS configuration or environment-specific API URLs.

#### Scenario: Browser requests backend API during development
- **WHEN** the development-served WebUI requests a relative `/api` endpoint
- **THEN** the Angular development server forwards that request to the configured local backend
- **AND** the browser observes it as same-origin traffic
