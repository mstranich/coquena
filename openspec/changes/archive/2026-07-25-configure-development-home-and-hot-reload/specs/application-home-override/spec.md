## ADDED Requirements

### Requirement: Explicit application-home environment override
Coquena SHALL use the absolute cleaned path in a non-empty `COQUENA_HOME` environment variable as its application home. When `COQUENA_HOME` is unset, it SHALL use the directory containing the resolved executable as its application home.

#### Scenario: Override is supplied
- **WHEN** an operator invokes `coquena init` with `COQUENA_HOME` set to an absolute path
- **THEN** Coquena initializes only beneath that path
- **AND** it does not create portable state beside the executable

#### Scenario: Override is absent
- **WHEN** an operator invokes Coquena with `COQUENA_HOME` unset
- **THEN** Coquena selects the directory containing the resolved executable as its application home

#### Scenario: Override is relative or empty
- **WHEN** `COQUENA_HOME` is set to a relative path or an empty value
- **THEN** a command that requires the application home exits unsuccessfully
- **AND** its diagnostic identifies `COQUENA_HOME`
- **AND** it creates or modifies no application state

### Requirement: Consistent application-home consumers
Coquena SHALL apply the same application-home resolution to initialization and every local runtime-store operation exposed through the CLI or WebUI.

#### Scenario: Runtime operation uses an initialized override home
- **WHEN** `COQUENA_HOME` names a valid initialized installation and an operator invokes a local runtime operation through the CLI or WebUI
- **THEN** the operation reads or mutates only that installation's runtime store
- **AND** it does not use state beside the executable