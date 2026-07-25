## MODIFIED Requirements

### Requirement: Explicit portable initialization
Coquena SHALL expose a `coquena init` command that initializes an installation in the selected application home. When `COQUENA_HOME` is unset, the selected application home SHALL be the directory containing the resolved Coquena executable and Coquena SHALL NOT use the process working directory as the installation root.

#### Scenario: Initialize from another working directory
- **WHEN** the operator invokes `coquena init` while `COQUENA_HOME` is unset and the process working directory differs from the executable directory
- **THEN** Coquena creates the installation structure in the executable directory
- **AND** Coquena creates no installation files in the process working directory

#### Scenario: Executable reached through a link
- **WHEN** the operator invokes `coquena init` through a filesystem link to the executable while `COQUENA_HOME` is unset
- **THEN** Coquena uses the directory containing the resolved executable target as the installation root

#### Scenario: Explicit application home overrides executable location
- **WHEN** the operator invokes `coquena init` with `COQUENA_HOME` set to a valid absolute path different from the executable directory
- **THEN** Coquena creates the installation structure beneath `COQUENA_HOME`
- **AND** Coquena creates no installation files beside the executable