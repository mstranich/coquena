## MODIFIED Requirements

### Requirement: List installed models through the CLI
Coquena SHALL provide a CLI command that lists every model present in the configured model directory at the artifact level. For each installed artifact it SHALL report its canonical `user/repo[@revision]:filename.gguf` reference, exact filename, resolved revision, and any logical `user/repo[@revision]:QUANT` selector that describes its quantization class. The logical selector SHALL be presented as a potentially non-unique class and SHALL NOT replace the canonical artifact reference.

The listing SHALL report each unreconciled `.gguf` separately with its path-derived identity and a status distinguishing it from an installed model. It SHALL NOT nest companions beneath principals, because doing so asserts a relation that does not hold across all quantizations. Each entry SHALL report the artifact's declared type and whether it is usable, with a reason when it is not. The command SHALL issue no network request and SHALL report an empty list rather than an error when no model is present.

#### Scenario: List canonical and logical references
- **WHEN** an operator lists installed artifacts
- **THEN** each installed entry reports its canonical exact-filename reference
- **AND** it may additionally report the logical quantization selector without claiming that selector is unique

#### Scenario: List two variants in one quantization class
- **WHEN** two installed filenames share the same declared quantization
- **THEN** the listing reports two different canonical artifact references and the shared logical selector
- **AND** neither artifact is hidden or selected as the preferred variant

#### Scenario: Report a companion at the same level as any other artifact
- **WHEN** the library holds a principal model and a projector compatible with it
- **THEN** the listing reports both as independent entries with canonical references and declared types
- **AND** it does not nest either beneath the other

#### Scenario: Nest a companion beneath its principal
- **WHEN** the library holds a principal model and a projector compatible with it
- **THEN** the listing does not nest the projector beneath that principal
- **AND** the nesting this scenario previously required remains withdrawn

#### Scenario: List an unmatched companion
- **WHEN** the library holds a projector compatible with no installed principal
- **THEN** the listing reports it with its canonical reference and type
- **AND** it reports the projector as unusable, naming the absent compatible principal as the reason

#### Scenario: Report an unreconciled artifact
- **WHEN** the configured models directory contains a `.gguf` without a valid matching sidecar
- **THEN** the listing reports its path-derived repository and filename distinctly from a canonical installed identity
- **AND** it does not fabricate revision provenance

#### Scenario: Report the type and reason for an unusable artifact
- **WHEN** the listing reports a sidecar-less artifact or an orphaned projector
- **THEN** it reports the artifact's declared type read from its own header
- **AND** it names a usability reason distinguishing unreconciled from orphaned

#### Scenario: List an empty library
- **WHEN** the configured models directory contains no model artifacts
- **THEN** the command reports an empty list and succeeds
- **AND** it issues no network request

#### Scenario: List models as JSON
- **WHEN** an operator requests JSON output
- **THEN** it reports the same canonical references, logical selectors, types, revisions, and usability as machine-readable fields
