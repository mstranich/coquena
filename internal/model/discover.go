package model

// DiscoveredCandidate is one GGUF artifact a repository holds, together with
// the classification read from its own header. The header is read through
// bounded range access, so a candidate is classified without any part of its
// tensor payload being transferred.
//
// Diagnostic carries the reason a candidate could not be classified. A
// repository holding one unreadable artifact must still report the others, so
// an inspection failure degrades that candidate to an explicit unknown
// classification rather than failing the whole discovery.
type DiscoveredCandidate struct {
	Source         Source         `json:"source"`
	Artifact       Artifact       `json:"artifact"`
	Classification Classification `json:"classification"`
	// Selected marks the candidate the reference's selector resolves to. It is
	// a mark on a complete list, never a filter: discovery reports every
	// candidate whether or not a selector was supplied.
	Selected   bool   `json:"selected"`
	Diagnostic string `json:"diagnostic,omitempty"`
}

// Discovery is the result of exploring a remote repository: every GGUF
// candidate it contains with its classification.
type Discovery struct {
	Repository string `json:"repository"`
	Revision   string `json:"revision"`
	// Selector is the selector carried by the reference, empty when none was
	// supplied.
	Selector   string                `json:"selector,omitempty"`
	Candidates []DiscoveredCandidate `json:"candidates"`
	// ModelCard is the repository README as plain text. It is deliberately left
	// unpopulated: reporting the model card was turned off because its length
	// buries the candidate list discovery exists to present, and a later change
	// reintroduces it behind an explicit option. The field and Resolver.ModelCard
	// are kept so that change reconnects them rather than reimplementing them —
	// until then discovery issues no request whose result is never shown.
	ModelCard string `json:"modelCard,omitempty"`
}

// Discover reports every GGUF candidate in a repository with its
// classification, marking the one a selector would resolve to rather than
// reducing the result to it.
//
// The repository is resolved with no selector on purpose: an operator exploring
// a repository needs to see what else is in it, above all the projector that
// makes the principal multimodal, which no quant selector would ever name.
//
// The repository's model card is not read. Its output was turned off because it
// buries the candidate list, so fetching it would spend a request on a result
// nothing reports; see the comment on Discovery.ModelCard.
func (m *Manager) Discover(reference string) (Discovery, error) {
	repository, revision, selector, err := ParseReference(reference)
	if err != nil {
		return Discovery{}, err
	}
	candidates, err := m.ResolveRevision(repository, revision, "")
	if err != nil {
		return Discovery{}, err
	}
	result := Discovery{Repository: repository, Revision: revision, Selector: selector, Candidates: make([]DiscoveredCandidate, 0, len(candidates))}
	for _, candidate := range candidates {
		discovered := DiscoveredCandidate{
			Source:         candidate.Source,
			Artifact:       candidate.Artifact,
			Classification: Classification{Type: TypeUnknown},
			Selected:       selector != "" && SelectorMatches(selector, candidate.Source.Filename),
		}
		inspection, inspectErr := m.InspectRemote(candidate)
		if inspectErr != nil {
			discovered.Diagnostic = inspectErr.Error()
		} else {
			discovered.Classification = inspection.Classification
			discovered.Artifact = inspection.Artifact
		}
		result.Candidates = append(result.Candidates, discovered)
	}
	if len(candidates) > 0 {
		result.Revision = candidates[0].Source.ResolvedRevision
	}
	m.logf("model discover completed repository=%s candidates=%d", repository, len(result.Candidates))
	return result, nil
}

// RemoteCompanions reports the artifacts in the principal's own repository that
// are admissible as its companions, read through the same bounded range access
// discovery uses. It returns every admissible candidate rather than selecting
// one, matching EvaluateCompanions: compatible companions are interchangeable
// within their class.
//
// Only projector companions are derived. A draft model's header declares
// general.type "model" exactly like the principal it drafts for, so a draft is
// a role the operator assigns and never one Coquena can read off a file.
func (m *Manager) RemoteCompanions(principal Candidate) ([]Candidate, error) {
	principalInspection, err := m.InspectRemote(principal)
	if err != nil {
		return nil, err
	}
	siblings, err := m.ResolveRevision(principal.Source.Repository, principal.Source.RequestedRevision, "")
	if err != nil {
		return nil, err
	}
	byName := make(map[string]Candidate, len(siblings))
	candidates := make([]Companion, 0, len(siblings))
	for _, sibling := range siblings {
		if sibling.Source.Filename == principal.Source.Filename {
			continue
		}
		inspection, inspectErr := m.InspectRemote(sibling)
		if inspectErr != nil {
			m.logf("model companion inspection failed filename=%s: %v", sibling.Source.Filename, inspectErr)
			continue
		}
		byName[sibling.Source.Filename] = sibling
		candidates = append(candidates, Companion{Name: sibling.Source.Filename, Classification: inspection.Classification})
	}
	admissible := EvaluateCompanions(ProjectorCriterion(principalInspection.Classification), candidates)
	out := make([]Candidate, 0, len(admissible))
	for _, companion := range admissible {
		out = append(out, byName[companion.Name])
	}
	return out, nil
}
