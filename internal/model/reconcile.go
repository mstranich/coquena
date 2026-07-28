package model

import (
	"fmt"
	"os"
	"path/filepath"
)

// ReconcileResult reports the outcome of a successful reconciliation.
type ReconcileResult struct {
	Record Record
	// Partial is true when --online succeeded without a content hash to
	// compare against (Hugging Face reported no LFS oid for the matched
	// sibling, e.g. a small non-LFS-tracked file). It is always false for
	// ReconcileAccept, which never contacts Hugging Face.
	Partial bool
}

// ReconcileOnline reconciles the .gguf at path against Hugging Face, treating
// its path-derived repository (the two path segments above the file,
// mirroring unreconciledSource) as the candidate identity. It resolves that
// repository's "main" revision, locates the sibling whose filename matches
// path's base name exactly, hashes the local file, and compares the digest
// against the LFS content hash (oid) Hugging Face reports for that sibling,
// when one is available. On agreement — or when no oid is available to
// compare, which is not treated as a mismatch — it persists the same
// .json/.revision sidecar pair a normal download produces, with
// Artifact.SHA256 populated from the just-computed digest. On disagreement it
// refuses and writes no sidecar.
func (m *Manager) ReconcileOnline(path string) (ReconcileResult, error) {
	path = filepath.Clean(path)
	source := unreconciledSource(m.ModelsDir, path)
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
	m.logf("model reconcile online completed path=%s repository=%s filename=%s partial=%v", path, source.Repository, source.Filename, result.Partial)
	return result, nil
}

// reconcileAgainst persists the sidecar for the single artifact at path from
// the remote sibling it was matched to. It is the whole per-artifact
// publication path — hash, compare, publish — factored out of ReconcileOnline
// so repository-driven reconciliation drives the same code over a set of
// artifacts rather than reimplementing it, and so the caller that already holds
// the matched candidate does not resolve the repository once per artifact.
//
// A digest disagreement is refused, and the diagnostic says the artifact may be
// from an earlier revision rather than asserting corruption: reconciliation
// compares against the repository's current revision, so a file that was
// legitimately downloaded before the repository was updated fails this
// comparison while being perfectly intact.
func (m *Manager) reconcileAgainst(path string, candidate Candidate) (ReconcileResult, error) {
	digest, err := hashFile(path)
	if err != nil {
		return ReconcileResult{}, err
	}
	partial := candidate.LFSOID == ""
	if !partial && candidate.LFSOID != digest {
		return ReconcileResult{}, fmt.Errorf("reconcile refused for %s: the local file's digest %s disagrees with the content hash %s Hugging Face reports at revision %s; the artifact may be from an earlier revision of the repository rather than corrupt, and --accept records it as local if that is what you intend", candidate.Source.Filename, digest, candidate.LFSOID, candidate.Source.ResolvedRevision)
	}
	record := Record{SchemaVersion: RecordSchemaVersion, Source: candidate.Source, Artifact: candidate.Artifact}
	record.Artifact.SHA256 = digest
	// Take back the record PublishInPlace persisted: the classification is
	// derived inside it, so the copy passed in still carries a zero one.
	if record, err = PublishInPlace(m.ModelsDir, record); err != nil {
		return ReconcileResult{}, err
	}
	return ReconcileResult{Record: record, Partial: partial}, nil
}

// ReconcileAccept reconciles the .gguf at path with no network access,
// deriving Repository/Filename from the path the same way ReconcileOnline
// does, hashing the local file, and persisting a reduced "local"-provider
// sidecar so a later verify has a baseline. Source.Validate accepts
// "local" with only Repository and Filename required.
func (m *Manager) ReconcileAccept(path string) (ReconcileResult, error) {
	path = filepath.Clean(path)
	source := unreconciledSource(m.ModelsDir, path)
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
	record := Record{SchemaVersion: RecordSchemaVersion, Source: source, Artifact: Artifact{SizeBytes: info.Size(), SHA256: digest}}
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
		switch entry.State {
		case "installed":
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
			out.Partial = reconciled.Partial
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
