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

// FormatReference retains the logical quantization reference used by existing
// callers. CanonicalReference is the singleton artifact identity.
func FormatReference(entry LibraryEntry) string {
	if logical := LogicalReference(entry); logical != "" {
		return logical
	}
	return entry.Source.Repository + ":unknown"
}

// ErrNoInstalledMatch indicates a reference matched no artifact in the library
// state it was resolved against.
type ErrNoInstalledMatch struct {
	Reference string
	// State names the library state the reference was resolved against, so a
	// failed reconciliation does not report the artifact as "not installed"
	// when being uninstalled is precisely its subject.
	State string
	// Available holds canonical artifact references in that state for the
	// reference's repository, so the diagnostic can name what the operator may select
	// instead of the selector that missed.
	Available []string
}

// state reports the library state to name in a diagnostic, defaulting to
// managed for a zero value.
func (e *ErrNoInstalledMatch) state() string {
	if e.State == "" {
		return "managed"
	}
	return e.State
}

func (e *ErrNoInstalledMatch) Error() string {
	return fmt.Sprintf("reference %q matches no %s model; %s for that repository: %s", e.Reference, e.state(), e.state(), formatCandidates(e.Available))
}

// ErrAmbiguousInstalledMatch indicates a reference matched more than one
// artifact in the library state it was resolved against.
type ErrAmbiguousInstalledMatch struct {
	Reference string
	// State names the library state the reference was resolved against.
	State string
	// Matches holds canonical references for every artifact matched.
	Matches []string
}

// Count reports how many artifacts the reference matched. It is derived from
// Matches so the two can never disagree.
func (e *ErrAmbiguousInstalledMatch) Count() int { return len(e.Matches) }

func (e *ErrAmbiguousInstalledMatch) Error() string {
	state := e.State
	if state == "" {
		state = "managed"
	}
	return fmt.Sprintf("reference %q matches %d %s models: %s", e.Reference, len(e.Matches), state, formatCandidates(e.Matches))
}

// ErrAmbiguousPrincipals indicates a reference resolved to more than one
// principal model of one repository during reconciliation.
//
// It is distinct from ErrAmbiguousInstalledMatch because the ambiguity is
// judged on one axis only: reconciliation takes a principal together with its
// companions, so a projector beside a principal is never an alternative to it,
// while two quantizations of the same model are a choice for llama-server that
// only the operator can make.
type ErrAmbiguousPrincipals struct {
	Reference  string
	Repository string
	// Matches holds the filenames of every principal the reference resolved to.
	Matches []string
}

func (e *ErrAmbiguousPrincipals) Error() string {
	return fmt.Sprintf("reference %q matches %d principal models in %s: %s; name one of them, since choosing a quantization is yours to make", e.Reference, len(e.Matches), e.Repository, formatCandidates(e.Matches))
}

// SplitReference parses the user/repo:QUANT reference grammar into its
// repository and quant selector, without resolving it against any source.
func SplitReference(reference string) (repository, quant string, err error) {
	ref, err := ParseModelReference(reference)
	if err != nil {
		return "", "", err
	}
	return ref.Repository, ref.Selector, nil
}

// ResolveInstalled resolves the user/repo:QUANT reference grammar against the
// installed model library, without issuing any network request.
func (m *Manager) ResolveInstalled(reference string) (LibraryEntry, error) {
	// Every state but untracked is a managed artifact: it has a sidecar, so
	// Coquena knows what it is. Whether it is usable is a separate question the
	// state answers on its own.
	return m.resolveLibrary(reference, func(s ArtifactState) bool { return s != StateUntracked && s != "" }, "managed")
}

// ResolveUntracked resolves the same user/repo:QUANT reference grammar
// against the artifacts that have no valid sidecar yet.
//
// Reconciliation is the one operation whose subject is by definition not
// installed, so it cannot go through ResolveInstalled. It still addresses that
// subject the way every other model command does: the reference is the
// catalog's identity, and an operator never has to switch to a filesystem path
// to name an artifact Coquena just listed.
func (m *Manager) ResolveUntracked(reference string) (LibraryEntry, error) {
	return m.resolveLibrary(reference, func(s ArtifactState) bool { return s == StateUntracked }, string(StateUntracked))
}

// resolveLibrary matches reference against the library entries in one state,
// so the reference grammar is written once and every caller applies it
// identically.
func (m *Manager) resolveLibrary(reference string, wanted func(ArtifactState) bool, state string) (LibraryEntry, error) {
	ref, err := ParseModelReference(reference)
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
		if !wanted(entry.State) || entry.Source.Repository != ref.Repository {
			continue
		}
		available = append(available, CanonicalReference(entry.Source))
		if ref.RevisionExplicit && !strings.EqualFold(entry.Source.ResolvedRevision, ref.Revision) {
			continue
		}
		if ref.Selector != "" {
			if ref.SelectorKind == SelectorFilename {
				if !strings.EqualFold(entry.Source.Filename, ref.Selector) {
					continue
				}
			} else if !strings.Contains(strings.ToLower(entry.Source.Filename), strings.ToLower(ref.Selector)) {
				continue
			}
		}
		matches = append(matches, entry)
	}
	switch len(matches) {
	case 0:
		return LibraryEntry{}, &ErrNoInstalledMatch{Reference: reference, State: state, Available: available}
	case 1:
		return matches[0], nil
	default:
		matched := make([]string, 0, len(matches))
		for _, entry := range matches {
			matched = append(matched, CanonicalReference(entry.Source))
		}
		return LibraryEntry{}, &ErrAmbiguousInstalledMatch{Reference: reference, State: state, Matches: matched}
	}
}
