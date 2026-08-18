package model

import (
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
)

type RandomAccessReader interface {
	ReadAt([]byte, int64) (int, error)
	Size() int64
}

type LocalReader struct {
	file *os.File
	size int64
}

func OpenLocal(path string) (*LocalReader, error) {
	f, err := os.Open(path)
	if err != nil {
		return nil, err
	}
	info, err := f.Stat()
	if err != nil {
		_ = f.Close()
		return nil, err
	}
	return &LocalReader{f, info.Size()}, nil
}
func (r *LocalReader) ReadAt(p []byte, off int64) (int, error) { return r.file.ReadAt(p, off) }
func (r *LocalReader) Size() int64                             { return r.size }
func (r *LocalReader) Close() error                            { return r.file.Close() }

type Source struct {
	Provider          string `json:"provider"`
	Repository        string `json:"repository"`
	RequestedRevision string `json:"requestedRevision"`
	ResolvedRevision  string `json:"resolvedRevision"`
	Filename          string `json:"filename"`
	URL               string `json:"url"`
}

// Artifact is the persisted evidence about one artifact's bytes. It carries no
// transport metadata: an entity tag's meaning depends on which hop answered, so
// it lives on Candidate and Job, where it is live state rather than a record.
type Artifact struct {
	SizeBytes    int64    `json:"sizeBytes"`
	LastModified string   `json:"lastModified,omitempty"`
	Digests      []Digest `json:"digests,omitempty"`
	DownloadedAt string   `json:"downloadedAt,omitempty"`
}

// Digest returns the single digest carrying the given tuple. Callers ask for
// what they need rather than iterating, so the at-most-one rule has exactly one
// place it can be violated and exactly one place it is checked.
func (a Artifact) Digest(role DigestRole, algorithm DigestAlgorithm, domain DigestDomain) (Digest, bool) {
	for _, d := range a.Digests {
		if d.Role == role && d.Algorithm == algorithm && d.Domain == domain {
			return d, true
		}
	}
	return Digest{}, false
}

// Integrity returns the digest Coquena computed from the local file. A valid
// sidecar always carries one, so a false return means the record is invalid
// rather than that the artifact's integrity is merely unknown.
func (a Artifact) Integrity() (Digest, bool) {
	return a.Digest(RoleIntegrity, AlgorithmSHA256, DomainRawFile)
}

// RepositoryMatch returns the published digest that was accepted as evidence,
// if one was. A digest in a domain Coquena cannot reproduce is recorded but
// never returned here, so no caller can mistake it for something compared.
func (a Artifact) RepositoryMatch() (Digest, bool) {
	for _, d := range a.Digests {
		if d.Role == RoleRepositoryMatch && d.Reproducible() {
			return d, true
		}
	}
	return Digest{}, false
}

// SetDigest records d, replacing any digest already carrying its tuple. It is
// how a writer avoids creating the duplicate that Validate rejects.
func (a *Artifact) SetDigest(d Digest) {
	for i, existing := range a.Digests {
		if existing.Role == d.Role && existing.Algorithm == d.Algorithm && existing.Domain == d.Domain {
			a.Digests[i] = d
			return
		}
	}
	a.Digests = append(a.Digests, d)
}

// Validate reports whether the recorded digests are unambiguous. Sidecars are
// deliberately hand-editable plain text, so two digests sharing a tuple is a
// state a person can reach; treating it as invalid is safer than silently
// preferring whichever was written first.
func (a Artifact) Validate() error {
	seen := make(map[Digest]struct{}, len(a.Digests))
	for _, d := range a.Digests {
		if d.Value == "" {
			return fmt.Errorf("digest with role %q records no value", d.Role)
		}
		key := Digest{Role: d.Role, Algorithm: d.Algorithm, Domain: d.Domain}
		if _, duplicate := seen[key]; duplicate {
			return fmt.Errorf("two digests recorded for role %q, algorithm %q, domain %q", d.Role, d.Algorithm, d.Domain)
		}
		seen[key] = struct{}{}
	}
	return nil
}

const (
	RecordSchemaVersion = 4
	DerivationVersion   = 1
)

// Local is the local evidence axis: the digest Coquena computed from the file
// and when it computed it.
//
// It is a projection, not storage. The integrity digest lives in the digest
// collection like every other digest, and carries its own ComputedAt; Local
// gives the axis a name at the point of use without giving the same fact a
// second home in the document.
type Local struct {
	Digest     Digest
	ComputedAt string
}

// Verification is what the last integrity check found, and when.
//
// AgainstDigest names the integrity digest value the check compared. A record
// naming a digest the sidecar no longer carries does not apply, which is how
// replacing an artifact's evidence retires the reading of it without any other
// command having to reach into this axis and clear it.
type Verification struct {
	Outcome       VerifyOutcome `json:"outcome"`
	CheckedAt     string        `json:"checkedAt"`
	AgainstDigest string        `json:"againstDigest"`
}

// Observation is the latest thing a repository said. Only the latest is kept:
// the sidecar records current evidence, not an event log.
//
// It answers where the followed channel points now, which is not the same
// question as whether the local bytes belong there. Recording one never
// establishes, strengthens or weakens an Attestation.
type Observation struct {
	Repository        string  `json:"repository"`
	RequestedRevision string  `json:"requestedRevision,omitempty"`
	ResolvedCommit    string  `json:"resolvedCommit"`
	ArtifactPath      string  `json:"artifactPath"`
	PublishedDigest   *Digest `json:"publishedDigest,omitempty"`
	ObservedAt        string  `json:"observedAt"`
}

// AttestationMethod records how a binding was established, because the two
// sources differ in character and an operator should be able to tell them
// apart.
type AttestationMethod string

const (
	// MethodDownload is provenance that follows from the transfer: the bytes
	// came from a revision-pinned URL, so where they came from is known
	// without comparing anything.
	MethodDownload AttestationMethod = "download"
	// MethodRemoteComparison is provenance reconstructed after the fact, by
	// reproducing the repository's published identifier from bytes already on
	// disk.
	MethodRemoteComparison AttestationMethod = "remote-comparison"
)

// Attestation binds an artifact's bytes to a repository, a resolved revision
// and a path within it. It is established once and is never retracted by a
// later observation.
type Attestation struct {
	Repository     string            `json:"repository"`
	ResolvedCommit string            `json:"resolvedCommit"`
	ArtifactPath   string            `json:"artifactPath"`
	Digest         Digest            `json:"digest"`
	Method         AttestationMethod `json:"method"`
	VerifiedAt     string            `json:"verifiedAt"`
}

// Ownership is the operator's assertion that they accepted this artifact. It
// survives later online reconciliation: no network call can retract a decision
// a person made.
type Ownership struct {
	Accepted   bool   `json:"accepted"`
	AcceptedAt string `json:"acceptedAt,omitempty"`
}

type PhysicalQuantization struct {
	Type            GGMLType `json:"type"`
	Name            string   `json:"name"`
	CountParameters uint64   `json:"countParameters"`
}

type Derivation struct {
	Version              int                   `json:"version"`
	ComputedAt           string                `json:"computedAt"`
	Classification       Classification        `json:"classification"`
	CountParameters      int64                 `json:"countParameters"`
	PhysicalQuantization *PhysicalQuantization `json:"physicalQuantization,omitempty"`
}

// Record is one artifact's sidecar: what Coquena knows, on axes that do not
// derive from one another. Each of the four optional axes is a pointer because
// its absence is itself evidence — never verified, never observed, unattested,
// never accepted — and a zero value could not say that.
type Record struct {
	SchemaVersion int           `json:"schemaVersion"`
	Source        Source        `json:"source"`
	Artifact      Artifact      `json:"artifact"`
	Derivation    Derivation    `json:"derivation"`
	Verification  *Verification `json:"verification,omitempty"`
	Observation   *Observation  `json:"observation,omitempty"`
	Attestation   *Attestation  `json:"attestation,omitempty"`
	Ownership     *Ownership    `json:"ownership,omitempty"`
	// Local and Classification are in-memory projections, derived on read and
	// never persisted on their own. Local names the local evidence axis over
	// the integrity digest; Classification restates what Derivation holds.
	Local          Local                      `json:"-"`
	Classification Classification             `json:"-"`
	Extra          map[string]json.RawMessage `json:"-"`
}

// Standing is an artifact's attestation standing, derived from the axes and
// never persisted. It answers only "what is known about these bytes' origin",
// which is one input to the artifact's state rather than the state itself.
type Standing string

const (
	// StandingVerified means an attestation is recorded.
	StandingVerified Standing = "verified"
	// StandingPartial means a repository was consulted and no attestation
	// could be established. It is deliberately not a stored attestation kind:
	// durable attestation is either established or absent, and this describes
	// the outcome of an operation.
	StandingPartial Standing = "partial"
	// StandingUnattested means no repository has been consulted. A privately
	// obtained model rests here permanently and is not thereby deficient.
	StandingUnattested Standing = "unattested"
)

// Standing derives the artifact's attestation standing. None of the three is a
// failure, and none of them gates usability.
//
// It reads the attestation through AppliedAttestation, so an attestation about
// bytes the artifact no longer carries reports as unattested rather than as
// provenance nobody established.
func (r Record) Standing() Standing {
	switch {
	case r.attested():
		return StandingVerified
	case r.Observation != nil:
		return StandingPartial
	default:
		return StandingUnattested
	}
}

func (r Record) attested() bool {
	_, applies := r.AppliedAttestation()
	return applies
}

// AppliedAttestation returns the attestation if it still describes the
// artifact's current bytes.
//
// An attestation binds a digest. If the record no longer carries that digest,
// the attestation is about bytes that are no longer there, and applying it would
// report provenance nobody established for what is on disk. This is the same
// guard AppliedVerification applies, for the same reason: both axes make a claim
// about specific bytes.
//
// The guard lives at the reader deliberately. An attestation can be carried
// forward by an operation that does not own the axis — preserveUnownedAxes does
// exactly that — so no amount of care in individual writers would be enough.
func (r Record) AppliedAttestation() (Attestation, bool) {
	if r.Attestation == nil {
		return Attestation{}, false
	}
	bound := r.Attestation.Digest
	current, ok := r.Artifact.Digest(bound.Role, bound.Algorithm, bound.Domain)
	if !ok || current.Value != bound.Value {
		return Attestation{}, false
	}
	return *r.Attestation, true
}

// Altered reports whether the last verification found the bytes disagreeing
// with the digest it checked. A record naming a digest the artifact no longer
// carries is not applied, so replacing evidence reads as unverified rather than
// as a verdict about bytes that are no longer the recorded ones.
func (r Record) Altered() bool {
	verification, applies := r.AppliedVerification()
	return applies && verification.Outcome == VerifyMismatched
}

// AppliedVerification returns the verification record if it still describes the
// artifact's current integrity digest.
func (r Record) AppliedVerification() (Verification, bool) {
	if r.Verification == nil {
		return Verification{}, false
	}
	integrity, ok := r.Artifact.Integrity()
	if !ok || integrity.Value != r.Verification.AgainstDigest {
		return Verification{}, false
	}
	return *r.Verification, true
}

type Status string

const (
	Pending     Status = "pending"
	Downloading Status = "downloading"
	Paused      Status = "paused"
	Completed   Status = "completed"
	Failed      Status = "failed"
	Cancelled   Status = "cancelled"
)

type Job struct {
	SchemaVersion     int    `json:"schemaVersion"`
	ID                string `json:"id"`
	Source            Source `json:"source"`
	PartialPath       string `json:"partialPath"`
	TargetPath        string `json:"targetPath"`
	DownloadedBytes   int64  `json:"downloadedBytes"`
	ExpectedSizeBytes int64  `json:"expectedSizeBytes"`
	ETag              string `json:"etag,omitempty"`
	LastModified      string `json:"lastModified,omitempty"`
	// PublishedDigest is the identifier the repository published for this
	// artifact, carried from the candidate that started the job so the
	// completed download can record what the repository said. It is observation
	// evidence, not attestation: nothing here has been compared.
	PublishedDigest *Digest `json:"publishedDigest,omitempty"`
	Status          Status  `json:"status"`
	Error           string  `json:"error,omitempty"`
}

// LibraryEntry is one artifact the configured models directory holds.
//
// Classification is read from the sidecar when one records it, and from the
// artifact's own header when none does, so an artifact's declared type is
// reported before anything is written about it. Classifying an unreconciled
// artifact is an observation only: it stays unreconciled and unresolvable for
// binding. Classification stays a pointer so an incomplete entry — a download
// still in flight, which has no artifact to read — reports its absence rather
// than an all-zero digest.
//
// Usability is derived from the enumerated library as a whole and is never
// persisted, since a projector's usability changes when a principal beside it
// is installed or removed.
type LibraryEntry struct {
	Source               Source                `json:"source"`
	TargetPath           string                `json:"targetPath"`
	State                string                `json:"state"`
	Classification       *Classification       `json:"classification,omitempty"`
	DerivationStale      bool                  `json:"derivationStale,omitempty"`
	PhysicalQuantization *PhysicalQuantization `json:"physicalQuantization,omitempty"`
	Usability            Usability             `json:"usability"`
	Job                  *Job                  `json:"job,omitempty"`
	// Standing is the artifact's attestation standing, derived from the record's
	// axes. An unreconciled artifact has no record and reads as unattested.
	Standing Standing `json:"standing"`
	// AttestationMethod names how the attestation was established, when there is
	// one that applies. It is what distinguishes provenance that came from the
	// transfer from provenance reconstructed by comparison.
	AttestationMethod AttestationMethod `json:"attestationMethod,omitempty"`
	// Verification is the last recorded integrity check, carried only when it
	// still describes the artifact's current digest. Enumeration never hashes,
	// so this record is the only thing that can report altered bytes.
	Verification *Verification `json:"verification,omitempty"`
	// VerificationStale reports that the file was modified more recently than
	// the verification was performed. It weakens the claim and never changes
	// the recorded outcome: mtime is advisory, and is used only to say "this
	// result may be old", never to establish one.
	VerificationStale bool `json:"verificationStale,omitempty"`
}

// Altered reports that the last applicable verification found the bytes
// disagreeing with the digest recorded for them. It is read from the record,
// never from a fresh hash: enumeration would otherwise have to re-read every
// artifact in the library on every listing.
func (e LibraryEntry) Altered() bool {
	return e.Verification != nil && e.Verification.Outcome == VerifyMismatched
}

func (j Job) Validate() error {
	if j.ID == "" || j.Source.ResolvedRevision == "" || j.Source.URL == "" {
		return errors.New("download job is missing immutable source identity")
	}
	if j.Status == "" {
		return errors.New("download job has no status")
	}
	if j.DownloadedBytes < 0 || j.ExpectedSizeBytes < 0 {
		return errors.New("download job has negative byte count")
	}
	return nil
}
func (s Source) Validate() error {
	switch s.Provider {
	case "huggingface":
		if s.Repository == "" || s.Filename == "" || s.ResolvedRevision == "" || s.URL == "" {
			return fmt.Errorf("invalid Hugging Face source")
		}
		return nil
	case "local":
		if s.Repository == "" || s.Filename == "" {
			return fmt.Errorf("invalid local source")
		}
		return nil
	default:
		return fmt.Errorf("invalid Hugging Face source")
	}
}

var ErrInconsistentRevision = errors.New("model revision sidecars are inconsistent")
var _ io.ReaderAt = (*os.File)(nil)
