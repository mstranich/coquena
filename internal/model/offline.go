package model

import (
	"fmt"
	"strings"
)

// maxNamedCandidates bounds how many filenames an Error() string renders. The
// structured fields always carry the complete list; only the human sentence is
// truncated, so a repository holding hundreds of quants stays readable in a
// terminal.
const maxNamedCandidates = 8

// formatCandidates renders filenames for a human, naming at most
// maxNamedCandidates of them and counting the rest.
func formatCandidates(filenames []string) string {
	if len(filenames) == 0 {
		return "none"
	}
	if len(filenames) <= maxNamedCandidates {
		return strings.Join(filenames, ", ")
	}
	return fmt.Sprintf("%s and %d more", strings.Join(filenames[:maxNamedCandidates], ", "), len(filenames)-maxNamedCandidates)
}

// ErrNoInstalledMatch indicates a reference matched no installed artifact.
type ErrNoInstalledMatch struct {
	Reference string
	// Available holds the filenames installed for the reference's repository,
	// so the diagnostic can name what the operator may select instead of the
	// selector that missed.
	Available []string
}

func (e *ErrNoInstalledMatch) Error() string {
	return fmt.Sprintf("reference %q matches no installed model; installed for that repository: %s", e.Reference, formatCandidates(e.Available))
}

// ErrAmbiguousInstalledMatch indicates a reference matched more than one installed artifact.
type ErrAmbiguousInstalledMatch struct {
	Reference string
	// Matches holds the filenames of every artifact the reference matched.
	Matches []string
}

// Count reports how many installed artifacts the reference matched. It is
// derived from Matches so the two can never disagree.
func (e *ErrAmbiguousInstalledMatch) Count() int { return len(e.Matches) }

func (e *ErrAmbiguousInstalledMatch) Error() string {
	return fmt.Sprintf("reference %q matches %d installed models: %s", e.Reference, len(e.Matches), formatCandidates(e.Matches))
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
	var available []string
	for _, entry := range entries {
		if entry.State != "installed" || entry.Source.Repository != repo {
			continue
		}
		available = append(available, entry.Source.Filename)
		if selector != "" {
			if strings.HasSuffix(strings.ToLower(selector), ".gguf") {
				if !strings.EqualFold(entry.Source.Filename, selector) {
					continue
				}
			} else if !strings.Contains(strings.ToLower(entry.Source.Filename), strings.ToLower(selector)) {
				continue
			}
		}
		matches = append(matches, entry)
	}
	switch len(matches) {
	case 0:
		return LibraryEntry{}, &ErrNoInstalledMatch{Reference: reference, Available: available}
	case 1:
		return matches[0], nil
	default:
		matched := make([]string, 0, len(matches))
		for _, entry := range matches {
			matched = append(matched, entry.Source.Filename)
		}
		return LibraryEntry{}, &ErrAmbiguousInstalledMatch{Reference: reference, Matches: matched}
	}
}
