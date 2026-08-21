## ADDED Requirements

### Requirement: Import a model without consulting a repository

Coquena SHALL provide `coquena model import <reference>`, which adopts an
artifact already on disk from its path-derived identity alone. It SHALL issue no
network request, SHALL persist a `local`-provider sidecar, and SHALL leave the
artifact in state `local`.

`import` SHALL accept no mode flag, SHALL NOT fall back to consulting a
repository, and SHALL NOT accept an option importing the whole configured models
directory. It SHALL refuse to import an artifact it cannot classify, writing no
sidecar. It SHALL NOT accept `--json`: it writes rather than answers, and what an
operator would want to read back afterwards is available from `list`.

#### Scenario: Import an artifact offline
- **WHEN** an operator imports a single referenced `.gguf`
- **THEN** Coquena persists a `local`-provider sidecar from the derived repository and filename
- **AND** it issues no network request
- **AND** the artifact is subsequently reported in state `local`

#### Scenario: Reject importing the whole library
- **WHEN** an operator asks to import every artifact beneath the configured models directory in one invocation
- **THEN** Coquena reports that the option is not accepted
- **AND** it writes no sidecar

#### Scenario: Refuse to import an unclassifiable artifact
- **WHEN** an operator imports a file Coquena cannot classify
- **THEN** Coquena reports the failure and writes no sidecar
- **AND** the artifact remains in state `untracked`

### Requirement: Reconcile a model against its repository

Coquena SHALL provide `coquena model reconcile <reference>`, which reconciles the
artifacts of one repository against that repository in a single action. It SHALL
resolve the repository named by the `user/repo[@revision]:QUANT` reference
grammar, enumerate that repository's remote candidates with their classification
without downloading any complete artifact, and match them against the artifacts
present on disk by filename, corroborated by metadata.

A reference naming a repository that holds a principal and its companions SHALL
reconcile all of them, since they are parts of one logical unit; a reference
matching more than one principal SHALL be refused as ambiguous, naming the
candidates, since choosing among quantizations is the operator's decision.

Reconciliation SHALL persist a full Hugging Face-provenance sidecar for each
artifact whose locally computed SHA-256 digest agrees with the content hash
Hugging Face reports, SHALL refuse any artifact whose digest disagrees, and SHALL
report a remote candidate that is absent locally rather than omitting it. An
artifact for which a repository was consulted and no match was established SHALL
be left in state `detached` rather than silently adopted as `local`.

`reconcile` SHALL accept no mode flag and SHALL NOT fall back to adopting local
bytes. It SHALL operate on one repository per invocation, SHALL NOT accept an
option reconciling the whole configured models directory, SHALL remain an
explicit operator action, and SHALL NOT run as a side effect of any listing
command. It SHALL NOT accept `--json`: it writes rather than answers, and it
prompts, so it could not emit exactly one document even if it did.

#### Scenario: Reconcile a listed artifact by its reference
- **WHEN** an operator reconciles an artifact using the reference form the listing reports for it
- **THEN** Coquena resolves the repository that reference names and reconciles the artifacts it holds
- **AND** the operator does not have to supply a filesystem path

#### Scenario: Reconcile online against Hugging Face
- **WHEN** an operator reconciles a referenced repository and its artifacts match the corresponding Hugging Face artifacts
- **THEN** Coquena persists a full sidecar for each as if they had been downloaded by Coquena
- **AND** the artifacts are subsequently reported in state `synced`

#### Scenario: Leave an unmatched artifact detached rather than local
- **WHEN** a repository is consulted for an artifact and no match can be established
- **THEN** Coquena reports the artifact in state `detached`
- **AND** it does not adopt the artifact as `local` on the operator's behalf

#### Scenario: Reconcile a repository holding a principal and its companion
- **WHEN** an operator reconciles a reference naming a repository that holds a principal model and a compatible projector
- **THEN** Coquena reconciles both artifacts in one action
- **AND** neither is left untracked

#### Scenario: Refuse a reference matching several principals
- **WHEN** a reference matches more than one principal model in the repository
- **THEN** Coquena refuses as ambiguous and names the matching artifacts
- **AND** it does not choose a quantization on the operator's behalf

#### Scenario: Report a companion missing from disk
- **WHEN** a repository's remote candidates include a companion that is not present locally
- **THEN** Coquena reports the companion as available remotely and absent locally
- **AND** it reconciles the artifacts that are present

#### Scenario: Refuse a reconciliation on content mismatch
- **WHEN** a local artifact's computed digest disagrees with the content hash Hugging Face reports for the matched sibling
- **THEN** Coquena refuses that artifact and writes no sidecar for it
- **AND** the diagnostic reports that the artifact may be from an earlier revision rather than asserting corruption

#### Scenario: Fail when a candidate's remote inspection fails
- **WHEN** the remote inspection of one of the repository's candidates fails
- **THEN** Coquena reports the failure for that artifact and writes no sidecar for it
- **AND** it does not drop that artifact from the reconciliation target set

#### Scenario: Reject reconciling the whole library
- **WHEN** an operator asks to reconcile every artifact beneath the configured models directory in one invocation
- **THEN** Coquena reports that the option is not accepted
- **AND** it writes no sidecar

#### Scenario: Reject a machine-readable reconciliation
- **WHEN** an operator invokes reconciliation with `--json`
- **THEN** Coquena reports that the option does not apply to this command
- **AND** it does not silently ignore the option

### Requirement: List the model library through the CLI

Coquena SHALL provide a CLI command that lists every model present in the
configured model directory at the artifact level. For each artifact it SHALL
report its canonical `user/repo[@revision]:filename.gguf` reference, exact
filename, resolved revision where one is recorded, and any logical
`user/repo[@revision]:QUANT` selector that describes its quantization class. The
logical selector SHALL be presented as a potentially non-unique class and SHALL
NOT replace the canonical artifact reference.

Each entry SHALL report its state using the seven-state vocabulary, its declared
type, and whether it is usable. The listing SHALL NOT report a usability reason,
because the state is the reason. It SHALL report an `untracked` artifact with its
path-derived identity, distinctly from a canonical managed identity, and SHALL
NOT fabricate revision provenance for it.

The listing SHALL NOT nest companions beneath principals, because doing so
asserts a relation that does not hold across all quantizations. It SHALL issue no
network request and SHALL report an empty list rather than an error when no model
is present.

Human-readable and JSON output SHALL use the same state vocabulary.

#### Scenario: List canonical and logical references
- **WHEN** an operator lists the library
- **THEN** each managed entry reports its canonical exact-filename reference
- **AND** it may additionally report the logical quantization selector without claiming that selector is unique

#### Scenario: List two variants in one quantization class
- **WHEN** two managed filenames share the same declared quantization
- **THEN** the listing reports two different canonical artifact references and the shared logical selector
- **AND** neither artifact is hidden or selected as the preferred variant

#### Scenario: Report a companion at the same level as any other artifact
- **WHEN** the library holds a principal model and a projector compatible with it
- **THEN** the listing reports both as independent entries with canonical references and declared types
- **AND** it does not nest either beneath the other

#### Scenario: Report an untracked artifact
- **WHEN** the configured models directory contains a `.gguf` without a valid matching sidecar
- **THEN** the listing reports it in state `untracked` with its path-derived repository and filename
- **AND** it does not fabricate revision provenance

#### Scenario: Report an unmatched companion as usable
- **WHEN** the library holds a projector in a usable state compatible with no installed principal
- **THEN** the listing reports it with its canonical reference and type
- **AND** it reports the projector as usable
- **AND** it may report the absent compatible principal as advice

#### Scenario: Report usability without a separate reason
- **WHEN** the listing reports an artifact that is not usable
- **THEN** it reports the artifact's state and that the artifact is not usable
- **AND** it reports no usability reason distinct from the state

#### Scenario: List an empty library
- **WHEN** the configured models directory contains no model artifacts
- **THEN** the command reports an empty list and succeeds
- **AND** it issues no network request

#### Scenario: List models as JSON
- **WHEN** an operator requests JSON output
- **THEN** it reports the same canonical references, logical selectors, types, revisions, states and usability as machine-readable fields
- **AND** the state values are the same seven names the human output uses

### Requirement: Query a repository for updates on demand

Coquena SHALL provide `coquena model update <ref>`, which queries Hugging Face
using only the artifact's recorded repository, requested revision, and filename,
without reading or hashing the local file. It SHALL report whether Hugging Face's
current content for that path differs from what is recorded locally, degrading to
a commit-revision-only comparison when Hugging Face reports no content hash for
the artifact. It SHALL NOT run automatically from any listing command.

#### Scenario: Report an up-to-date model
- **WHEN** an operator queries updates for a model whose recorded revision and content hash still match Hugging Face's current state
- **THEN** Coquena reports the model as up to date

#### Scenario: Detect remote content replacement under the same path
- **WHEN** Hugging Face now serves different content at the same repository path and requested revision than what is recorded locally
- **THEN** Coquena reports that an update is available
- **AND** it names the newly observed revision

#### Scenario: Refuse to query updates for a local artifact
- **WHEN** an operator queries updates for an artifact in state `local`
- **THEN** Coquena refuses with a diagnostic explaining no Hugging Face identity is recorded for it

### Requirement: Verify and repair an artifact's recorded coherence

Coquena SHALL provide `coquena model verify <ref>`, which re-hashes the resolved
artifact and reports to the operator whether its bytes still match the digest
recorded for them. Verification SHALL make no network request and SHALL address
one artifact per invocation.

`verify` is an acting command, not a reading one: it exists to repair the
coherence between an artifact and the evidence recorded about it. What it
persists, and the rule that it is the sole writer of that evidence, belong to the
`gguf-model-library` capability and SHALL NOT be restated here.

Coquena SHALL accept `--repair-derived`, which recalculates the artifact's
locally derived GGUF evidence. Repair SHALL proceed only after a digest match,
SHALL be refused after a mismatch, and SHALL fail without changing the derived
evidence when the artifact can no longer be classified. A failed repair SHALL NOT
retract the verification that ran before it: the hash comparison happened, and
discarding its outcome would lose an observation Coquena actually made. The
command SHALL NOT accept a bulk repair mode.

`verify` SHALL NOT accept `--json`.

#### Scenario: Verify an intact artifact
- **WHEN** an operator verifies an artifact whose current digest matches the one recorded for it
- **THEN** Coquena reports the artifact as verified
- **AND** it does not modify the artifact's bytes

#### Scenario: Detect local corruption
- **WHEN** an operator verifies an artifact whose current digest disagrees with the one recorded for it
- **THEN** Coquena reports verification failure and an appropriate recovery direction
- **AND** it does not modify the artifact's bytes

#### Scenario: Repair stale derived evidence
- **WHEN** an operator invokes `verify <ref> --repair-derived` for an artifact whose digest matches and whose derivation is stale
- **THEN** Coquena reports the refreshed classification and quantization evidence
- **AND** it preserves the model bytes

#### Scenario: Refuse repair after a digest mismatch
- **WHEN** `verify <ref> --repair-derived` computes a digest that differs from the recorded one
- **THEN** Coquena refuses the repair
- **AND** the model bytes remain unchanged

#### Scenario: Refuse repair that cannot be recomputed
- **WHEN** `verify <ref> --repair-derived` cannot classify the artifact
- **THEN** Coquena reports the failure
- **AND** the recorded derived evidence is left as it was
- **AND** the verification outcome that ran before the repair is still recorded

#### Scenario: Reject bulk repair
- **WHEN** an operator requests `verify --all` or an equivalent library-wide repair
- **THEN** Coquena rejects the invocation
- **AND** it does not repair any artifact

#### Scenario: Reject machine-readable verification
- **WHEN** an operator invokes `verify` with `--json`
- **THEN** Coquena reports that the option does not apply to this command
- **AND** it does not silently ignore the option

### Requirement: Expose the model command surface through the CLI

Coquena SHALL expose `coquena model discover <repository-or-url[:selector]>`,
`coquena model download <repository-or-url[:selector]>`, `coquena model list`,
`coquena model import <reference>`, `coquena model reconcile <reference>`,
`coquena model verify <reference>`, `coquena model update <reference>`, and the
download-job subcommands. The CLI SHALL NOT expose `coquena model inspect`,
`coquena model resolve`, or `coquena model revision`; local byte evidence and
recorded evidence SHALL be compared through `verify`, while the internal
resolution, revision and inspection projections remain available to non-CLI
workflows.

Each command SHALL correspond to the HTTP endpoint it would be were the REST API
built, and `--json` SHALL follow that correspondence: a command that reads state
and returns a representation of it accepts `--json`; a command that acts on state
does not. `discover`, `list`, `update` and the download-job query subcommands
SHALL accept `--json`. `download`, `import`, `reconcile` and `verify` SHALL NOT:
each exists to change what is recorded, and reports an outcome rather than a
resource.

`verify` belongs with the acting commands. It repairs the coherence between an
artifact's bytes and the evidence recorded about them, and writing that repair is
its purpose rather than a side effect. What it establishes is read back through
`list`, which reports each artifact's verification outcome.

Where a command accepts `--json` it SHALL emit exactly one schema-versioned JSON
document on standard output. Where a command does not, passing `--json` SHALL be
a usage error rather than a silently ignored option. Commands addressing one
managed artifact SHALL use the currently supported model reference grammar,
shall refuse ambiguity safely, and SHALL NOT require a filesystem path.

Coquena has no released version, so a removed or renamed command SHALL NOT be
retained as an alias, a deprecation shim, or a transitional error naming its
replacement. The usage text SHALL be the sole record of the surface.

#### Scenario: Reject the removed inspect command

- **WHEN** an operator invokes `coquena model inspect`
- **THEN** the CLI reports usage that does not advertise the removed command
- **AND** no local or remote state changes

#### Scenario: Reject the removed resolve and revision commands

- **WHEN** an operator invokes `coquena model resolve` or `coquena model revision`
- **THEN** the CLI reports usage that does not advertise either command
- **AND** the usage text does not name a replacement for them

#### Scenario: Reject the removed check-updates command

- **WHEN** an operator invokes `coquena model check-updates`
- **THEN** the CLI reports usage that does not advertise the removed command
- **AND** no alias forwards it to `coquena model update`

#### Scenario: Preserve internal inspection for download

- **WHEN** a managed download completes after its GGUF header has been inspected
- **THEN** the download workflow may use the internal inspection projection to
  classify the artifact without a second read

#### Scenario: Automate a reading model command

- **WHEN** an operator invokes `discover`, `list` or `update` with `--json`
- **THEN** standard output contains only one newline-terminated,
  schema-versioned JSON document
- **AND** diagnostics do not contaminate that document

#### Scenario: Reject machine-readable output from an acting command

- **WHEN** an operator passes `--json` to `download`, `import`, `reconcile` or
  `verify`
- **THEN** Coquena reports that the option does not apply to that command
- **AND** it does not silently ignore the option
- **AND** it creates no job and writes no sidecar

#### Scenario: Report created job identities without a machine-readable option

- **WHEN** an operator downloads a quantization whose bundle holds several artifacts
- **THEN** standard output carries every created job identity, one per line
- **AND** each job remains addressable afterwards through the download-job query subcommands

#### Scenario: Reject invalid model command input

- **WHEN** an operator supplies malformed, ambiguous, unsupported, or missing
  command input
- **THEN** the CLI exits unsuccessfully with usage or a safe diagnostic
- **AND** it creates no job or model artifact

#### Scenario: Address a managed artifact by its logical name

- **WHEN** an operator verifies or queries updates for a managed artifact
- **THEN** Coquena accepts the same currently supported reference family
- **AND** it does not require a filesystem path

#### Scenario: Name candidates behind an ambiguous selection

- **WHEN** a command requiring one artifact matches zero or several GGUF
  artifacts
- **THEN** the CLI reports the available candidate filenames
- **AND** it does not choose one implicitly

#### Scenario: Manage a download below the model command

- **WHEN** an operator invokes `coquena model download status <job-id>` or
  another supported job action
- **THEN** Coquena applies that action to the named durable model job
- **AND** it does not interpret the action name as a repository reference

### Requirement: Download a quantization as a complete bundle

Coquena SHALL download a selected principal artifact together with every
companion identified for it in the same repository, creating one independent
durable job per artifact and reporting every created job ID. The complete set
SHALL NOT be optional: Coquena SHALL NOT accept a flag limiting a download to the
principal alone, because the operator asks for a quantization and a quantization
is not usable in pieces.

`download` SHALL NOT accept `--json`. It acts rather than reads, and the job
identities it creates are reported on standard output one per line and remain
addressable through the download-job query subcommands.

#### Scenario: Enqueue companions with the principal
- **WHEN** an operator downloads a principal from a repository that holds a compatible projector
- **THEN** Coquena creates one job per artifact and reports every job ID
- **AND** each job remains independently pausable, resumable, and cancellable

#### Scenario: Download from a repository with no companion
- **WHEN** an operator downloads a principal from a repository holding no companion
- **THEN** Coquena creates the principal's job alone and reports that no companion was identified

#### Scenario: Reject the removed companion opt-in flag
- **WHEN** an operator passes the former companion opt-in flag to `coquena model download`
- **THEN** Coquena reports a usage error naming the flag as not accepted
- **AND** it does not silently ignore the flag

## REMOVED Requirements

### Requirement: Expose shared model management through the CLI

**Reason**: The requirement enumerated a command surface that this change
re-partitions: `reconcile` splits, `check-updates` is renamed, and `resolve` and
`revision` are withdrawn. It also carried no rule against retaining removed
spellings as aliases, which this change needs stated.

**Migration**: Replaced by "Expose the model command surface through the CLI",
which lists the new surface and forbids aliases and transitional shims.

### Requirement: Download a principal artifact with its companions

**Reason**: The opt-in companion flag made the bundle optional, contradicting the
unit the operator actually asks for. A quantization is not usable in pieces, so
there is nothing to opt into.

**Migration**: Replaced by "Download a quantization as a complete bundle".
`coquena model download <ref> --with-companions` becomes `coquena model download
<ref>`; the flag is rejected as a usage error rather than ignored.

### Requirement: Verify local model integrity and repair derived evidence on demand

**Reason**: The requirement still described the sidecar `establish-sidecar-authority`
replaced. It required default verification to make no local change, which
contradicts the library capability's rule that `verify` is the only writer of the
verification axis, and it carried a case for an artifact with no recorded digest,
which a schema-4 sidecar cannot express. It also predates the legacy sidecar
shapes it names.

**Migration**: Replaced by "Verify and repair an artifact's recorded coherence".
The command, its `--repair-derived` flag and its refusal of bulk repair are
unchanged; verification now records its outcome, has no baseline-absence case,
and does not accept `--json`.

### Requirement: Query installed revision through the CLI

**Reason**: `revision` serves no step of the model acquisition story. It is an
internal projection of `verify` that was exposed as a subcommand.

**Migration**: None. `coquena model list` already reports each artifact's
resolved revision, and the internal projection remains available to non-CLI
workflows.

### Requirement: Reconcile a model without a valid sidecar

**Reason**: One command with `--accept` and `--online` modes let a flag decide
between two different acts — adopting local bytes, and establishing a repository
match. Splitting the verb makes the operator name the act, so neither command can
silently choose the other.

**Migration**: `coquena model reconcile <ref> --accept` becomes `coquena model
import <ref>`. `coquena model reconcile <ref> --online` becomes `coquena model
reconcile <ref>`. No alias or transitional error is retained.

### Requirement: List installed models through the CLI

**Reason**: The requirement was written in the retired `installed` /
`unreconciled` vocabulary, which conflated presence, local adoption and remote
provenance, and it required a usability reason that the state now carries.

**Migration**: Replaced by "List the model library through the CLI", which
reports the same references and selectors using the seven-state vocabulary.

### Requirement: Check for remote model updates on demand

**Reason**: `check-updates` was an invented verb. The operator's daily tools use
the pair `update` and `upgrade`, and the bespoke name charged every user the cost
of learning it.

**Migration**: Replaced by "Query a repository for updates on demand", exposing
the same behaviour as `coquena model update`. No alias is retained.
