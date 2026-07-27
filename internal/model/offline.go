package model

import (
	"fmt"
	"strings"
)

// ErrNoInstalledMatch indicates a reference matched no installed artifact.
type ErrNoInstalledMatch struct{ Reference string }

func (e *ErrNoInstalledMatch) Error() string {
	return fmt.Sprintf("reference %q matches no installed model", e.Reference)
}

// ErrAmbiguousInstalledMatch indicates a reference matched more than one installed artifact.
type ErrAmbiguousInstalledMatch struct {
	Reference string
	Count     int
}

func (e *ErrAmbiguousInstalledMatch) Error() string {
	return fmt.Sprintf("reference %q matches %d installed models", e.Reference, e.Count)
}

// SplitReference parses the user/repo:QUANT reference grammar into its
// repository and quant selector, without resolving it against any source.
func SplitReference(reference string) (repository, quant string, err error) {
	repo, _, selector, err := parseRef(reference, "")
	if err != nil {
		return "", "", err
	}
	return repo, selector, nil
}

// ResolveInstalled resolves the user/repo:QUANT reference grammar against the
// installed model library, without issuing any network request.
func (m *Manager) ResolveInstalled(reference string) (LibraryEntry, error) {
	repo, _, selector, err := parseRef(reference, "")
	if err != nil {
		return LibraryEntry{}, err
	}
	entries, err := m.Library()
	if err != nil {
		return LibraryEntry{}, err
	}
	var matches []LibraryEntry
	for _, entry := range entries {
		if entry.State != "installed" || entry.Source.Repository != repo {
			continue
		}
		if selector != "" && !strings.Contains(strings.ToLower(entry.Source.Filename), strings.ToLower(selector)) {
			continue
		}
		matches = append(matches, entry)
	}
	switch len(matches) {
	case 0:
		return LibraryEntry{}, &ErrNoInstalledMatch{Reference: reference}
	case 1:
		return matches[0], nil
	default:
		return LibraryEntry{}, &ErrAmbiguousInstalledMatch{Reference: reference, Count: len(matches)}
	}
}
