## MODIFIED Requirements

### Requirement: Discover a remote repository's classified candidates
Coquena SHALL provide remote discovery of a Hugging Face repository that reports every GGUF candidate it contains together with each candidate's classification, read through bounded range access without downloading any complete artifact. When the reference carries a selector, discovery SHALL still report every candidate and SHALL mark which one the selector would resolve to, rather than reducing the result to that candidate. Discovery SHALL NOT print the repository's model card, whose length buries the candidate list discovery exists to present; reporting it SHALL be reintroduced behind an explicit option in a later change.

#### Scenario: Discover every candidate in a repository
- **WHEN** an operator discovers a repository containing a principal model and a projector
- **THEN** Coquena reports both candidates with their declared types
- **AND** it downloads neither artifact in full

#### Scenario: Mark the candidate a selector would choose
- **WHEN** an operator discovers a repository using a reference carrying a selector that matches one candidate
- **THEN** Coquena still reports every candidate in the repository
- **AND** it marks the one the selector resolves to

#### Scenario: Present the candidates without the model card
- **WHEN** an operator discovers a repository that publishes a long model card
- **THEN** the output reports the candidates and their classification only
- **AND** the model card does not appear

#### Scenario: Discover a repository without a model card
- **WHEN** the repository publishes no model card
- **THEN** Coquena reports the candidates
- **AND** it does not fail discovery
