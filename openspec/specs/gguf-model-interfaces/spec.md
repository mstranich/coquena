## Purpose

Define CLI and local WebUI interfaces for shared GGUF model management.
## Requirements

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

### Requirement: Expose model management through the local HTTP API
The local WebUI server SHALL expose schema-version-1 JSON APIs for resolving and inspecting GGUF artifacts, discovering remote repository candidates with their classification, discovering installed and incomplete model units, lazily reading an installed artifact's classification, serving cached publisher avatars, creating a download job, listing jobs, inspecting a job, and pausing, resuming, or cancelling a job. Inspection responses SHALL return the derived classification and scalar metadata only; the API SHALL NOT offer an option to return the raw GGUF metadata map. Accepted creation SHALL return `202` with an opaque job ID; job progress and terminal state SHALL be available through polling and an SSE endpoint. Invalid API paths, input, and methods SHALL return API errors or `405`, never an Angular document.

#### Scenario: Create and observe a download job
- **WHEN** a browser posts one valid resolved GGUF selection to the download API
- **THEN** it receives `202` with one job ID
- **AND** it can obtain that job's current progress and terminal status through the API

#### Scenario: Reject a raw-metadata request
- **WHEN** a browser requests an inspection with an option asking for raw metadata
- **THEN** the server rejects the unknown property rather than returning the raw metadata map

#### Scenario: Reject a malformed API operation
- **WHEN** a browser sends malformed JSON, an unknown property, an unknown job ID, or an unsupported method
- **THEN** the server returns a non-success JSON response or `405`
- **AND** it does not change job or model state

### Requirement: Present API-backed GGUF management in Models
The Angular Models workspace SHALL resolve public Hugging Face GGUF candidates, present responsive metadata summaries for an explicit selected artifact before download, discover installed and incomplete model units, create and observe download jobs, and provide pause, resume, and cancel actions as permitted by job state. The workspace SHALL visibly show backend diagnostics and SHALL use GGUF metadata as read-only reference rather than modifying manual fields or presets.

The inspection view SHALL present metadata as a two-column `Metadata`/`Value` table rather than a raw JSON document and SHALL not render implementation-oriented omission notices.

Each discovered candidate SHALL own independent inspection/loading state and SHALL reveal its collapsible metadata table within that candidate's entry, including the candidate's declared type. Installed-model library responses SHALL omit metadata; expanding an installed entry SHALL lazily load its classification and render the same collapsible table.

Discovery entries SHOULD display the publisher avatar from Hugging Face when available. Incomplete and installed entries SHOULD display the namespace-level avatar cached beneath the configured models directory and SHALL NOT depend on a remote image after job creation.

#### Scenario: Download a selected variant from Models
- **WHEN** an operator selects one resolved GGUF variant and requests download
- **THEN** the workspace starts only that artifact's job and renders its progress and current state
- **AND** it refreshes the model/job view after a terminal outcome

#### Scenario: Observe multiple incomplete models
- **WHEN** the configured models tree contains multiple incomplete downloads or a new download starts
- **THEN** each model appears immediately as its own library entry with independent state and progress
- **AND** pause, resume, and cancel act directly on that entry without selecting a shared download-management panel

#### Scenario: Expand an installed entry's classification
- **WHEN** an operator expands an installed model entry
- **THEN** the workspace lazily loads that artifact's classification and renders the collapsible table
- **AND** it does not request or render a raw metadata document

#### Scenario: Inspect metadata while configuring a preset
- **WHEN** an operator views a downloaded model's metadata and its manual preset controls
- **THEN** the workspace displays metadata as a reference
- **AND** it does not automatically create, change, or select a preset value

### Requirement: Delegate model download management to the global lifecycle
The model CLI SHALL keep `coquena model download` as a model-specific creation
and convenience entry point. Its `list`, `status`, `pause`, `resume`, and
`cancel` actions SHALL use the same durable job identity, status values,
progress representation, and safety semantics as `coquena download`, filtered
to `TYPE=model`.

#### Scenario: Manage a model job through either CLI entry point
- **WHEN** an operator queries or resumes a valid model job through the model-specific or global command family
- **THEN** both commands address the same job and report equivalent state
- **AND** resume preserves the model's existing adjacent partial-file and sidecar safety guarantees

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

### Requirement: Discovery reports quantization download cost
Coquena SHALL project the GGUF artifacts discovered in a remote repository into quantization entries suitable for comparison. Each quantization entry SHALL identify every constituent file and SHALL report the sum of their published sizes when every constituent size is known. Shared companions SHALL remain separate entries and SHALL NOT be counted in each quantization total. Human-readable and JSON discovery output SHALL expose the same grouping and size knowledge without changing what a subsequent download selects.

#### Scenario: Report a single-file quantization
- **WHEN** a repository publishes one GGUF file for a quantization with a known size
- **THEN** discovery reports one quantization containing that filename and its size as the total

#### Scenario: Sum a split quantization
- **WHEN** a quantization is published as multiple shards with known sizes
- **THEN** discovery reports one quantization entry containing every shard
- **AND** its total is the sum of all shard sizes rather than one row per shard

#### Scenario: Do not duplicate a shared companion cost
- **WHEN** discovery classifies an artifact as a companion that can be shared by multiple quantizations
- **THEN** it reports that artifact separately from the quantization totals
- **AND** no quantization total includes the companion size

#### Scenario: Preserve an unknown total
- **WHEN** any constituent file in a discovered quantization has no known positive size
- **THEN** discovery identifies the quantization total as unknown
- **AND** it does not present the sum of only the known files as the total

#### Scenario: Expose grouped discovery as JSON
- **WHEN** an operator requests JSON discovery output
- **THEN** each quantization includes its logical label, constituent artifact evidence, and either a known total size in bytes or an explicit unknown-size state
- **AND** existing per-candidate evidence remains available to API consumers

### Requirement: Discovery fetches the model card only on request
The default `coquena model discover` invocation SHALL present the compact quantization comparison without fetching or rendering the repository model card. The CLI SHALL accept an explicit `--model-card` option that fetches and renders the card in addition to the discovery result.

#### Scenario: Compare quantizations without a model-card request
- **WHEN** an operator invokes `coquena model discover <reference>` without `--model-card`
- **THEN** Coquena reports the grouped quantizations, files, and sizes
- **AND** it makes no request for the model card

#### Scenario: Request the model card explicitly
- **WHEN** an operator invokes `coquena model discover <reference> --model-card`
- **THEN** Coquena fetches and renders the repository model card in addition to the grouped discovery result
