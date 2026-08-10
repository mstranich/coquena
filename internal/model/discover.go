package model

import (
	"math"
	"path"
	"regexp"
	"sort"
	"strconv"
	"strings"
)

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

// DiscoveredQuantization is the comparison unit discovery presents. Files
// retains the artifact-level evidence while TotalSizeKnown distinguishes an
// unavailable total from a real byte count.
type DiscoveredQuantization struct {
	Label          string                `json:"label"`
	Files          []DiscoveredCandidate `json:"files"`
	TotalSizeBytes int64                 `json:"totalSizeBytes"`
	TotalSizeKnown bool                  `json:"totalSizeKnown"`
	Selected       bool                  `json:"selected"`
}

type DiscoverOptions struct {
	IncludeModelCard bool
}

// Discovery is the result of exploring a remote repository: every GGUF
// candidate it contains with its classification.
type Discovery struct {
	Repository string `json:"repository"`
	Revision   string `json:"revision"`
	// Selector is the selector carried by the reference, empty when none was
	// supplied.
	Selector      string                   `json:"selector,omitempty"`
	Candidates    []DiscoveredCandidate    `json:"candidates"`
	Quantizations []DiscoveredQuantization `json:"quantizations"`
	Companions    []DiscoveredCandidate    `json:"companions"`
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
	return m.DiscoverWithOptions(reference, DiscoverOptions{})
}

func (m *Manager) DiscoverWithOptions(reference string, options DiscoverOptions) (Discovery, error) {
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
	result.Quantizations, result.Companions = projectDiscovery(result.Candidates)
	if options.IncludeModelCard {
		result.ModelCard, err = m.Resolver.ModelCard(repository, result.Revision)
		if err != nil {
			return Discovery{}, err
		}
	}
	m.logf("model discover completed repository=%s candidates=%d", repository, len(result.Candidates))
	return result, nil
}

var splitGGUFName = regexp.MustCompile(`(?i)^(.*)-(\d{5})-of-(\d{5})\.gguf$`)

type splitPart struct {
	key, label   string
	index, total int
}

func parseSplit(filename string) (splitPart, bool) {
	normalized := strings.ReplaceAll(filename, `\`, "/")
	dir, base := path.Split(normalized)
	m := splitGGUFName.FindStringSubmatch(base)
	if m == nil {
		return splitPart{}, false
	}
	index, indexErr := strconv.Atoi(m[2])
	total, totalErr := strconv.Atoi(m[3])
	if indexErr != nil || totalErr != nil || index < 1 || total < 1 || index > total {
		return splitPart{}, false
	}
	prefix := strings.TrimSuffix(dir, "/") + "/" + m[1]
	return splitPart{key: "split:" + strings.ToLower(prefix) + ":" + m[3], label: path.Base(m[1]), index: index, total: total}, true
}

func projectDiscovery(candidates []DiscoveredCandidate) ([]DiscoveredQuantization, []DiscoveredCandidate) {
	parts := make(map[string][]splitPart)
	for _, candidate := range candidates {
		if candidate.Classification.Type == TypeProjector {
			continue
		}
		if part, ok := parseSplit(candidate.Source.Filename); ok {
			parts[part.key] = append(parts[part.key], part)
		}
	}
	validSplit := make(map[string]bool)
	for key, group := range parts {
		seen := make(map[int]bool, len(group))
		valid := len(group) == group[0].total
		for _, part := range group {
			valid = valid && part.total == group[0].total && !seen[part.index]
			seen[part.index] = true
		}
		validSplit[key] = valid
	}

	groups := make(map[string]*DiscoveredQuantization)
	order := make([]string, 0, len(candidates))
	companions := make([]DiscoveredCandidate, 0)
	for _, candidate := range candidates {
		if candidate.Classification.Type == TypeProjector {
			companions = append(companions, candidate)
			continue
		}
		normalized := strings.ReplaceAll(candidate.Source.Filename, `\`, "/")
		dir, base := path.Split(normalized)
		key, label := "file:"+strings.ToLower(normalized), strings.TrimSuffix(base, path.Ext(base))
		if strings.TrimSuffix(dir, "/") != "" {
			folder := strings.TrimSuffix(dir, "/")
			key, label = "dir:"+strings.ToLower(folder), path.Base(folder)
		} else if part, ok := parseSplit(candidate.Source.Filename); ok && validSplit[part.key] {
			key, label = part.key, part.label
		}
		group := groups[key]
		if group == nil {
			group = &DiscoveredQuantization{Label: label, TotalSizeKnown: true}
			groups[key] = group
			order = append(order, key)
		}
		group.Files = append(group.Files, candidate)
		group.Selected = group.Selected || candidate.Selected
		if candidate.Artifact.SizeBytes <= 0 || group.TotalSizeBytes > math.MaxInt64-candidate.Artifact.SizeBytes {
			group.TotalSizeKnown = false
		} else {
			group.TotalSizeBytes += candidate.Artifact.SizeBytes
		}
	}
	quantizations := make([]DiscoveredQuantization, 0, len(order))
	for _, key := range order {
		group := groups[key]
		if !group.TotalSizeKnown {
			group.TotalSizeBytes = 0
		}
		sort.SliceStable(group.Files, func(i, j int) bool { return group.Files[i].Source.Filename < group.Files[j].Source.Filename })
		quantizations = append(quantizations, *group)
	}
	return quantizations, companions
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
