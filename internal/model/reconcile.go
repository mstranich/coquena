package model

import (
	"fmt"
	"os"
	"path/filepath"
	"time"
)

// ReconcileResult reports the outcome of a successful reconciliation.
type ReconcileResult struct {
	Record Record
	// Diagnostic explains a partial result to the operator: what disagreed, or
	// that a recorded integrity digest was kept because the change to the local
	// bytes could not be proven. It is empty when a match was established.
	Diagnostic string
}

// Partial reports that the run established no attestation. It is read back off
// the record rather than carried as its own flag, so the answer cannot drift
// from the evidence: an artifact is partial exactly when its standing says so.
func (r ReconcileResult) Partial() bool { return r.Record.Standing() != StandingVerified }

// ReconcileOnline reconciles the .gguf at path against Hugging Face, treating
// its path-derived repository (the two path segments above the file,
// mirroring untrackedSource) as the candidate identity. It resolves that
// repository's "main" revision, locates the sibling whose filename matches
// path's base name exactly, hashes the local file, and compares the digest
// against the content SHA-256 Hugging Face reports for that sibling, when one
// is available. It persists the same .json/.revision sidecar pair a normal
// download produces in every case — agreement, disagreement, or no comparable
// evidence — because the artifact is usable in all three and the operator is
// better served by a working model than by an error. Only agreement establishes
// an attestation.
func (m *Manager) ReconcileOnline(path string) (ReconcileResult, error) {
	path = filepath.Clean(path)
	source := untrackedSource(m.ModelsDir, path)
	if source.Repository == "" || source.Filename == "" {
		return ReconcileResult{}, fmt.Errorf("cannot derive a Hugging Face repository and filename from path %q", path)
	}
	if err := m.checkReconcilePath(source, path); err != nil {
		return ReconcileResult{}, err
	}
	candidates, err := m.Resolver.ResolveRevision(source.Repository, "main", source.Filename)
	if err != nil {
		return ReconcileResult{}, err
	}
	if len(candidates) != 1 {
		return ReconcileResult{}, fmt.Errorf("reconcile: expected exactly one Hugging Face artifact matching %q in %s, got %d", source.Filename, source.Repository, len(candidates))
	}
	result, err := m.reconcileAgainst(path, candidates[0])
	if err != nil {
		return ReconcileResult{}, err
	}
	m.logf("model reconcile online completed path=%s repository=%s filename=%s partial=%v", path, source.Repository, source.Filename, result.Partial())
	return result, nil
}

// reconcileAgainst persists the sidecar for the single artifact at path from
// the remote sibling it was matched to. It is the whole per-artifact
// publication path — hash, compare, publish — factored out of ReconcileOnline
// so repository-driven reconciliation drives the same code over a set of
// artifacts rather than reimplementing it, and so the caller that already holds
// the matched candidate does not resolve the repository once per artifact.
//
// A disagreement is recorded rather than refused. The three ways an artifact
// can fail to match — the repository could not be consulted, the bytes belong to
// a revision that no longer exists, or they are not from that repository — are
// indistinguishable from the artifact, so treating one of them as decisive
// enough to discard the observation and hand back an error costs the operator a
// working model for no gain. The observation is written, attestation is left
// unset, and nothing an operator relies on is overwritten.
func (m *Manager) reconcileAgainst(path string, candidate Candidate) (ReconcileResult, error) {
	digest, err := hashFile(path)
	if err != nil {
		return ReconcileResult{}, err
	}
	now := time.Now().UTC().Format(time.RFC3339)
	record := Record{SchemaVersion: RecordSchemaVersion, Source: candidate.Source, Artifact: candidate.Artifact}
	published := publishedDigestOf(candidate)
	record.Observation = &Observation{
		Repository:        candidate.Source.Repository,
		RequestedRevision: candidate.Source.RequestedRevision,
		ResolvedCommit:    candidate.Source.ResolvedRevision,
		ArtifactPath:      candidate.Source.Filename,
		PublishedDigest:   published,
		ObservedAt:        now,
	}
	// Evidence is accepted when Coquena can reproduce it from the complete
	// local bytes, which is what makes the comparison meaningful at all.
	matched := false
	var diagnostic string
	if published != nil && published.Reproducible() {
		reproduced, reproduceErr := ReproduceDigest(path, *published)
		if reproduceErr != nil {
			return ReconcileResult{}, reproduceErr
		}
		if reproduced == published.Value {
			matched = true
			record.Artifact.SetDigest(*published)
			record.Attestation = &Attestation{
				Repository:     candidate.Source.Repository,
				ResolvedCommit: candidate.Source.ResolvedRevision,
				ArtifactPath:   candidate.Source.Filename,
				Digest:         *published,
				Method:         MethodRemoteComparison,
				VerifiedAt:     now,
			}
		} else {
			diagnostic = fmt.Sprintf("the local file reproduces %s where %s publishes %s at revision %s; the artifact may belong to an earlier or destroyed revision rather than being corrupt", reproduced, candidate.Source.Repository, published.Value, candidate.Source.ResolvedRevision)
		}
	}
	// Whether to re-baseline the integrity digest is a question about evidence,
	// not about freshness. Replacing a recorded digest with a newly computed one
	// that disagrees, on a run that established nothing, would erase the only
	// record that the bytes ever changed — and leave a sidecar that agrees with
	// whatever is on disk now, however it got there.
	recorded, hasRecorded := recordedIntegrity(path)
	switch {
	case !hasRecorded || recorded.Value == digest || matched:
		// Nothing recorded yet, nothing changed, or the new bytes are proven
		// against the repository. In all three the computed digest is the
		// honest one to keep.
		record.SetIntegrity(digest, now)
	default:
		record.SetIntegrity(recorded.Value, recorded.ComputedAt)
		changed := fmt.Sprintf("the local file now hashes to %s but the recorded integrity digest is %s; the recorded digest is kept so the change stays visible, and verify will report the artifact as altered", digest, recorded.Value)
		if diagnostic == "" {
			diagnostic = changed
		} else {
			diagnostic += ". " + changed
		}
	}
	if diagnostic != "" {
		m.logf("model reconcile recorded a partial result path=%s: %s", path, diagnostic)
	}
	// Take back the record PublishInPlace persisted: the classification is
	// derived inside it, so the copy passed in still carries a zero one.
	if record, err = PublishInPlace(m.ModelsDir, record); err != nil {
		return ReconcileResult{}, err
	}
	return ReconcileResult{Record: record, Diagnostic: diagnostic}, nil
}

// recordedIntegrity reads the integrity digest an artifact's existing sidecar
// carries, if it has a readable one. An unreadable sidecar records nothing,
// which is the same position as never having had one.
func recordedIntegrity(path string) (Digest, bool) {
	existing, err := ReadRecord(path)
	if err != nil {
		return Digest{}, false
	}
	return existing.Artifact.Integrity()
}

// publishedDigestOf reads whatever identifier the provider supplied for a
// candidate, with the domain it belongs to. A repository that publishes no
// identifier at all does not exist — Git hashes everything it stores — so this
// returns nil only when the resolver did not carry one through.
func publishedDigestOf(candidate Candidate) *Digest {
	if candidate.ContentSHA256 == "" {
		return nil
	}
	published := PublishedDigest(AlgorithmSHA256, DomainRawFile, candidate.ContentSHA256)
	return &published
}

// ReconcileAccept reconciles the .gguf at path with no network access,
// deriving Repository/Filename from the path the same way ReconcileOnline
// does, hashing the local file, and persisting a reduced "local"-provider
// sidecar so a later verify has a baseline. Source.Validate accepts
// "local" with only Repository and Filename required.
func (m *Manager) ReconcileAccept(path string) (ReconcileResult, error) {
	path = filepath.Clean(path)
	source := untrackedSource(m.ModelsDir, path)
	source.Provider = "local"
	if source.Repository == "" || source.Filename == "" {
		return ReconcileResult{}, fmt.Errorf("cannot derive a repository and filename from path %q", path)
	}
	if err := m.checkReconcilePath(source, path); err != nil {
		return ReconcileResult{}, err
	}
	info, err := os.Stat(path)
	if err != nil {
		return ReconcileResult{}, err
	}
	digest, err := hashFile(path)
	if err != nil {
		return ReconcileResult{}, err
	}
	now := time.Now().UTC().Format(time.RFC3339)
	record := Record{SchemaVersion: RecordSchemaVersion, Source: source, Artifact: Artifact{SizeBytes: info.Size()}}
	record.SetIntegrity(digest, now)
	// Acceptance is the operator's assertion, and it is recorded on its own
	// axis so a later online reconciliation cannot retract a decision a person
	// made. No observation is written: never having consulted a repository is
	// a different thing from having consulted one and established nothing.
	record.Ownership = &Ownership{Accepted: true, AcceptedAt: now}
	if record, err = PublishInPlace(m.ModelsDir, record); err != nil {
		return ReconcileResult{}, err
	}
	m.logf("model reconcile accept completed path=%s repository=%s filename=%s", path, source.Repository, source.Filename)
	return ReconcileResult{Record: record}, nil
}

// ReconcileMode names which of the two single-path reconciliation routes a bulk
// pass applies to every artifact it finds. Bulk mode does not relax the rule
// that exactly one of --online/--accept is required; it threads that single
// choice through the whole pass.
type ReconcileMode string

const (
	// ReconcileModeOnline applies ReconcileOnline to each artifact.
	ReconcileModeOnline ReconcileMode = "online"
	// ReconcileModeAccept applies ReconcileAccept to each artifact.
	ReconcileModeAccept ReconcileMode = "accept"
)

// BulkOutcome is what happened to one artifact during a bulk pass.
type BulkOutcome string

const (
	// OutcomeReconciled reports that a sidecar was persisted for the artifact.
	OutcomeReconciled BulkOutcome = "reconciled"
	// OutcomeSkipped reports an artifact that already had a valid sidecar. It
	// still contributes its classification to the computed relations.
	OutcomeSkipped BulkOutcome = "skipped"
	// OutcomeFailed reports an artifact the pass could not reconcile. The pass
	// continues past it.
	OutcomeFailed BulkOutcome = "failed"
)

// BulkReconcileEntry is one artifact's outcome in a bulk pass. Path is the
// stable identity relations refer back to; Repository and Filename are carried
// so a renderer never has to re-derive them from the path.
type BulkReconcileEntry struct {
	Path           string         `json:"path"`
	Repository     string         `json:"repository,omitempty"`
	Filename       string         `json:"filename"`
	Outcome        BulkOutcome    `json:"outcome"`
	Partial        bool           `json:"partial,omitempty"`
	Classification Classification `json:"classification"`
	Diagnostic     string         `json:"diagnostic,omitempty"`
}

// CompanionRelation is one computed companion admissibility between two
// artifacts seen in the same pass, identified by their paths. Only RoleProjector
// relations are computed: a draft is a role the operator assigns, and a draft
// model's header declares general.type "model" exactly like its target, so
// deriving draft relations here would pair every same-tokenizer model with
// every other one as if the operator had chosen them.
type CompanionRelation struct {
	Role          CompanionRole `json:"role"`
	PrincipalPath string        `json:"principalPath"`
	CompanionPath string        `json:"companionPath"`
}

// BulkReconcileResult reports a whole pass: every artifact's outcome, and the
// companion relations computed across the set.
type BulkReconcileResult struct {
	Entries   []BulkReconcileEntry `json:"entries"`
	Relations []CompanionRelation  `json:"relations"`
}

// ReconcileAll reconciles every unreconciled .gguf beneath the configured
// models directory in one pass, applying mode to each, and reports the
// companion relations among every artifact it saw.
//
// Reading each header is not an extra step layered on reconciliation: bulk
// reconciliation classifies as it goes, so the relations fall out of the same
// pass that persists the sidecars. An artifact that already carries a valid
// sidecar is not rewritten, but its persisted classification still participates
// in the relations, so a projector reconciled now can be matched against a
// principal installed earlier.
//
// A per-artifact failure is recorded as a diagnostic and the pass continues:
// one unreadable or mismatched file must not cost the operator the rest of the
// directory. The error return is reserved for a failure of the pass itself.
//
// No caller reaches this today: `coquena model reconcile --all` was turned off
// because a library-wide pass conflates artifacts that need an operator
// decision with artifacts that do not. It is kept, unreferenced and
// deliberately, together with BulkReconcileResult, BulkReconcileEntry,
// ReconcileMode and companionRelations, for the later change that reconsiders
// library-wide reconciliation — unreachable here is a decision, not an
// abandoned implementation. Its tests are kept running for the same reason.
func (m *Manager) ReconcileAll(mode ReconcileMode) (BulkReconcileResult, error) {
	if mode != ReconcileModeOnline && mode != ReconcileModeAccept {
		return BulkReconcileResult{}, fmt.Errorf("reconcile: exactly one of --online/--accept is required, got mode %q", mode)
	}
	entries, err := m.Library()
	if err != nil {
		return BulkReconcileResult{}, err
	}
	result := BulkReconcileResult{Entries: []BulkReconcileEntry{}, Relations: []CompanionRelation{}}
	for _, entry := range entries {
		// A library entry carrying a job is a download in flight; its target
		// file does not exist yet, so there is nothing to reconcile.
		if entry.Job != nil {
			continue
		}
		path := filepath.Clean(entry.TargetPath)
		out := BulkReconcileEntry{Path: path, Repository: entry.Source.Repository, Filename: entry.Source.Filename}
		switch {
		// An artifact that already has a sidecar is managed; the bulk pass exists
		// to adopt the ones that do not.
		case entry.State != StateUntracked:
			out.Outcome = OutcomeSkipped
			record, readErr := ReadRecord(path)
			if readErr != nil {
				out.Outcome = OutcomeFailed
				out.Diagnostic = readErr.Error()
				break
			}
			out.Classification = record.Classification
		default:
			var reconciled ReconcileResult
			var reconcileErr error
			if mode == ReconcileModeOnline {
				reconciled, reconcileErr = m.ReconcileOnline(path)
			} else {
				reconciled, reconcileErr = m.ReconcileAccept(path)
			}
			if reconcileErr != nil {
				out.Outcome = OutcomeFailed
				out.Diagnostic = reconcileErr.Error()
				m.logf("model reconcile all artifact failed path=%s: %v", path, reconcileErr)
				break
			}
			out.Outcome = OutcomeReconciled
			out.Partial = reconciled.Partial()
			out.Classification = reconciled.Record.Classification
		}
		result.Entries = append(result.Entries, out)
	}
	result.Relations = companionRelations(result.Entries)
	m.logf("model reconcile all completed mode=%s artifacts=%d relations=%d", mode, len(result.Entries), len(result.Relations))
	return result, nil
}

// companionRelations computes the projector-to-principal admissibility across
// the artifacts of one pass. It reuses EvaluateCompanions, which returns every
// admissible candidate rather than selecting one, because compatible companions
// are interchangeable within their class and the choice belongs to the operator.
func companionRelations(entries []BulkReconcileEntry) []CompanionRelation {
	candidates := make([]Companion, 0, len(entries))
	for _, entry := range entries {
		if entry.Outcome == OutcomeFailed {
			continue
		}
		candidates = append(candidates, Companion{Name: entry.Path, Classification: entry.Classification})
	}
	relations := []CompanionRelation{}
	for _, entry := range entries {
		if entry.Outcome == OutcomeFailed || entry.Classification.Type != TypeModel {
			continue
		}
		for _, admissible := range EvaluateCompanions(ProjectorCriterion(entry.Classification), candidates) {
			if admissible.Name == entry.Path {
				continue
			}
			relations = append(relations, CompanionRelation{Role: RoleProjector, PrincipalPath: entry.Path, CompanionPath: admissible.Name})
		}
	}
	return relations
}

// checkReconcilePath confirms path is exactly the on-disk location that
// Repository/Filename derive to, so reconcile never operates on, or writes
// sidecars for, a file outside modelsDir or at an unexpected location.
func (m *Manager) checkReconcilePath(source Source, path string) error {
	target, err := PathFor(m.ModelsDir, source.Repository, source.Filename)
	if err != nil {
		return err
	}
	if filepath.Clean(target) != path {
		return fmt.Errorf("path %q is not at its expected library location %q", path, target)
	}
	return nil
}
