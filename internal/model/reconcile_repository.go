package model

import (
	"fmt"
	"sort"
)

// RepositoryOutcome is what happened to one artifact of a repository during a
// repository-driven reconciliation. The set is enumerated rather than reduced
// to success/failure because the operator's next action differs for each: an
// absent candidate is downloaded, a foreign file is accepted or removed, a
// refused one is investigated.
type RepositoryOutcome string

const (
	// RepositoryReconciled reports that a full Hugging Face-provenance sidecar
	// was persisted for the artifact.
	RepositoryReconciled RepositoryOutcome = "reconciled"
	// RepositoryAlreadyInstalled reports an artifact that already carried a
	// valid sidecar. Nothing is rewritten for it.
	RepositoryAlreadyInstalled RepositoryOutcome = "installed"
	// RepositoryRefused reports an artifact whose reconciliation was refused,
	// above all one whose digest disagrees with the reported content hash. No
	// sidecar is written for it.
	RepositoryRefused RepositoryOutcome = "refused"
	// RepositoryAbsent reports a remote candidate the repository holds that is
	// not present on disk. It is reported rather than omitted so an incomplete
	// model is visible before an invocation fails.
	RepositoryAbsent RepositoryOutcome = "absent"
	// RepositoryForeign reports a local artifact matching no remote sibling. It
	// is reported, never adopted: reconciliation resolves identity against the
	// repository, and a file the repository does not contain has none.
	RepositoryForeign RepositoryOutcome = "foreign"
)

// RepositoryArtifact is one artifact's place in a repository reconciliation.
type RepositoryArtifact struct {
	Filename       string            `json:"filename"`
	Classification Classification    `json:"classification"`
	Outcome        RepositoryOutcome `json:"outcome"`
	// Partial marks an artifact reconciled without a content hash to compare
	// against, mirroring ReconcileResult.Partial.
	Partial    bool   `json:"partial,omitempty"`
	Diagnostic string `json:"diagnostic,omitempty"`
}

// RepositoryReconciliation reports one repository-driven reconciliation: every
// artifact the repository and the library between them account for, with what
// happened to it.
type RepositoryReconciliation struct {
	Repository string               `json:"repository"`
	Revision   string               `json:"revision"`
	Selector   string               `json:"selector,omitempty"`
	Artifacts  []RepositoryArtifact `json:"artifacts"`
}

// Refused reports whether any artifact was refused, so a caller can fail the
// invocation while still rendering everything the pass established.
func (r RepositoryReconciliation) Refused() []RepositoryArtifact {
	var out []RepositoryArtifact
	for _, artifact := range r.Artifacts {
		if artifact.Outcome == RepositoryRefused {
			out = append(out, artifact)
		}
	}
	return out
}

// ReconcileRepository reconciles the artifacts of one repository in a single
// action.
//
// The unit is the repository because that is what Hugging Face publishes and
// what an operator thinks in. Reconciling one artifact at a time guarantees the
// half-reconciled state this exists to prevent: a principal installed, its
// projector still foreign, and no preset able to bind the pair.
//
// It resolves the repository the reference names, enumerates its remote
// candidates with their classification exactly as Discover does — through
// bounded range access, so no complete artifact is transferred — and matches
// them against the artifacts on disk by filename, which Hugging Face guarantees
// unique within a repository, corroborated by the metadata each side declares.
//
// Ambiguity is judged on one axis only. A reference resolving to a principal
// and a projector is not ambiguous: they are parts of one logical unit and both
// are reconciled. A reference resolving to more than one principal present on
// disk is refused, because for llama-server two quantizations are genuinely
// different choices and only the operator can make them. The count is taken
// over the principals actually on disk rather than over everything the
// repository publishes, since a repository offering twenty quants of which the
// operator downloaded one poses no choice at all.
func (m *Manager) ReconcileRepository(reference string) (RepositoryReconciliation, error) {
	repository, requested, selector, err := ParseReference(reference)
	if err != nil {
		return RepositoryReconciliation{}, err
	}
	local, localNames, err := m.repositoryArtifacts(repository)
	if err != nil {
		return RepositoryReconciliation{}, err
	}
	if len(local) == 0 {
		return RepositoryReconciliation{}, &ErrNoInstalledMatch{Reference: reference, State: "unreconciled", Available: nil}
	}
	siblings, revision, err := m.repositorySiblings(repository, requested)
	if err != nil {
		return RepositoryReconciliation{}, err
	}
	principals := matchedPrincipals(siblings, local, selector)
	if len(principals) > 1 {
		return RepositoryReconciliation{}, &ErrAmbiguousPrincipals{Reference: reference, Repository: repository, Matches: principals}
	}
	targets := reconciliationTargets(siblings, local, selector, len(principals) == 1)
	if len(targets) == 0 {
		return RepositoryReconciliation{}, &ErrNoInstalledMatch{Reference: reference, State: "unreconciled", Available: localNames}
	}
	result := RepositoryReconciliation{Repository: repository, Revision: revision, Selector: selector, Artifacts: []RepositoryArtifact{}}
	remote := make(map[string]bool, len(siblings))
	for _, sibling := range siblings {
		name := sibling.candidate.Source.Filename
		remote[name] = true
		entry, present := local[name]
		if !present {
			if artifact, report := absentArtifact(sibling, selector); report {
				result.Artifacts = append(result.Artifacts, artifact)
			}
			continue
		}
		if !targets[name] {
			continue
		}
		result.Artifacts = append(result.Artifacts, m.reconcileOne(entry, sibling))
	}
	for _, name := range localNames {
		if remote[name] {
			continue
		}
		result.Artifacts = append(result.Artifacts, RepositoryArtifact{
			Filename:       name,
			Classification: entryClassification(local[name]),
			Outcome:        RepositoryForeign,
			Diagnostic:     fmt.Sprintf("no artifact of %s at revision %s carries this filename, so it was not adopted", repository, revision),
		})
	}
	m.logf("model reconcile repository completed repository=%s revision=%s artifacts=%d", repository, revision, len(result.Artifacts))
	return result, nil
}

// reconcileOne applies the per-artifact publication path to one matched pair.
// A refusal is recorded as the artifact's outcome rather than returned, because
// one artifact of a repository failing must not cost the operator the rest of
// the repository — the caller decides whether a refusal fails the invocation.
func (m *Manager) reconcileOne(entry LibraryEntry, sibling repositorySibling) RepositoryArtifact {
	name := sibling.candidate.Source.Filename
	artifact := RepositoryArtifact{Filename: name, Classification: sibling.classification}
	if entry.State == "installed" {
		artifact.Outcome = RepositoryAlreadyInstalled
		artifact.Classification = entryClassification(entry)
		return artifact
	}
	// The safety property of single-artifact reconciliation is preserved: an
	// artifact is only ever written at the location its repository and filename
	// derive to, never anywhere else beneath the models directory.
	if err := m.checkReconcilePath(Source{Repository: entry.Source.Repository, Filename: name}, entry.TargetPath); err != nil {
		artifact.Outcome = RepositoryRefused
		artifact.Diagnostic = err.Error()
		return artifact
	}
	reconciled, err := m.reconcileAgainst(entry.TargetPath, sibling.candidate)
	if err != nil {
		artifact.Outcome = RepositoryRefused
		artifact.Diagnostic = err.Error()
		m.logf("model reconcile repository refused filename=%s: %v", name, err)
		return artifact
	}
	artifact.Outcome = RepositoryReconciled
	artifact.Partial = reconciled.Partial
	artifact.Classification = reconciled.Record.Classification
	return artifact
}

// repositorySibling pairs a remote candidate with the classification read from
// its own header, which is what makes a principal distinguishable from a
// companion before anything is downloaded.
type repositorySibling struct {
	candidate      Candidate
	classification Classification
	diagnostic     string
}

// repositorySiblings enumerates a repository's GGUF candidates with their
// classification, the same bounded range read Discover performs. An artifact
// that cannot be inspected degrades to an explicit unknown classification
// rather than failing the whole reconciliation, so one unreadable file does not
// hide the repository.
func (m *Manager) repositorySiblings(repository, requested string) ([]repositorySibling, string, error) {
	candidates, err := m.ResolveRevision(repository, requested, "")
	if err != nil {
		return nil, "", err
	}
	revision := requested
	if len(candidates) > 0 {
		revision = candidates[0].Source.ResolvedRevision
	}
	siblings := make([]repositorySibling, 0, len(candidates))
	for _, candidate := range candidates {
		sibling := repositorySibling{candidate: candidate, classification: Classification{Type: TypeUnknown}}
		inspection, inspectErr := m.InspectRemote(candidate)
		if inspectErr != nil {
			sibling.diagnostic = inspectErr.Error()
		} else {
			sibling.classification = inspection.Classification
		}
		siblings = append(siblings, sibling)
	}
	return siblings, revision, nil
}

// repositoryArtifacts collects the library entries belonging to one repository,
// keyed by filename, together with their filenames in a stable order. An entry
// carrying a job is a download in flight whose target does not exist yet, so it
// is not an artifact on disk.
func (m *Manager) repositoryArtifacts(repository string) (map[string]LibraryEntry, []string, error) {
	entries, err := m.Library()
	if err != nil {
		return nil, nil, err
	}
	local := map[string]LibraryEntry{}
	var names []string
	for _, entry := range entries {
		if entry.Job != nil || entry.Source.Repository != repository {
			continue
		}
		if entry.State != "installed" && entry.State != "unreconciled" {
			continue
		}
		local[entry.Source.Filename] = entry
		names = append(names, entry.Source.Filename)
	}
	sort.Strings(names)
	return local, names, nil
}

// matchedPrincipals names the principal models on disk the reference resolves
// to. It is the only set ambiguity is judged over: a projector is never an
// alternative to a principal, so its presence can never make a reference
// ambiguous.
func matchedPrincipals(siblings []repositorySibling, local map[string]LibraryEntry, selector string) []string {
	var principals []string
	for _, sibling := range siblings {
		name := sibling.candidate.Source.Filename
		if sibling.classification.Type != TypeModel {
			continue
		}
		if _, present := local[name]; !present {
			continue
		}
		if !SelectorMatches(selector, name) {
			continue
		}
		principals = append(principals, name)
	}
	return principals
}

// reconciliationTargets names the artifacts on disk this invocation reconciles:
// every one the selector resolves to, plus every projector of the repository
// once a principal has been resolved, since a principal and its companions are
// parts of one logical unit and no quant selector would ever name the projector.
func reconciliationTargets(siblings []repositorySibling, local map[string]LibraryEntry, selector string, hasPrincipal bool) map[string]bool {
	targets := map[string]bool{}
	for _, sibling := range siblings {
		name := sibling.candidate.Source.Filename
		if _, present := local[name]; !present {
			continue
		}
		if SelectorMatches(selector, name) || (hasPrincipal && sibling.classification.Type == TypeProjector) {
			targets[name] = true
		}
	}
	return targets
}

// absentArtifact decides whether a remote candidate missing from disk is worth
// reporting, and how. A projector is always reported, because its absence is
// exactly the incomplete model reconciliation exists to make visible, as is a
// candidate the selector names, because the operator asked for it. The other
// quantizations of the repository are not: they are alternatives the operator
// declined, not something missing.
func absentArtifact(sibling repositorySibling, selector string) (RepositoryArtifact, bool) {
	name := sibling.candidate.Source.Filename
	if sibling.classification.Type != TypeProjector && !(selector != "" && SelectorMatches(selector, name)) {
		return RepositoryArtifact{}, false
	}
	return RepositoryArtifact{
		Filename:       name,
		Classification: sibling.classification,
		Outcome:        RepositoryAbsent,
		Diagnostic:     fmt.Sprintf("available in %s at revision %s but not present locally; download it to complete the model", sibling.candidate.Source.Repository, sibling.candidate.Source.ResolvedRevision),
	}, true
}

// entryClassification reports a library entry's classification, which the
// enumeration derives for every artifact but leaves nil when the entry predates
// a classification being available at all.
func entryClassification(entry LibraryEntry) Classification {
	if entry.Classification == nil {
		return Classification{Type: TypeUnknown}
	}
	return *entry.Classification
}
