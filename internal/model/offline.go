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

// FormatReference builds the <user>/<repo>:<quant> reference string from a LibraryEntry.
// It tries to extract the quantization from the filename first, falling back to
// the FileTypeNames mapping for the general.file_type value.
func FormatReference(entry LibraryEntry) string {
	if entry.Classification == nil {
		return entry.Source.Repository + ":unknown"
	}
	if entry.Classification.FileType != nil {
		quant := extractQuantFromFilename(entry.Source.Filename)
		if quant != "" {
			return entry.Source.Repository + ":" + quant
		}
		return entry.Source.Repository + ":" + FileTypeNames[*entry.Classification.FileType]
	}
	return entry.Source.Repository + ":unknown"
}

// extractQuantFromFilename tries to extract the quantization from the filename.
// Examples: "model-Q4_K_M.gguf" -> "Q4_K_M", "model.gguf" -> ""
func extractQuantFromFilename(filename string) string {
	// Remove extension
	name := strings.TrimSuffix(filename, ".gguf")
	// Split by dash
	parts := strings.Split(name, "-")
	if len(parts) < 2 {
		return ""
	}
	// The quantization is usually the last part before .gguf
	quant := parts[len(parts)-1]
	// Validate it looks like a quantization name
	validQuants := map[string]bool{
		"F32": true, "F16": true, "Q4_0": true, "Q4_1": true, "Q4_2": true, "Q4_3": true,
		"Q5_0": true, "Q5_1": true, "Q8_0": true, "Q8_1": true, "Q2_K": true, "Q3_K": true,
		"Q4_K": true, "Q4_K_M": true, "Q4_K_S": true, "Q5_K": true, "Q5_K_M": true, "Q5_K_S": true,
		"Q6_K": true, "Q6_K_M": true, "Q8_K": true, "Q8_K_M": true, "IQ2_XXS": true, "IQ2_XS": true,
		"IQ3_XXS": true, "IQ1_S": true, "IQ4_NL": true, "IQ3_S": true, "IQ2_S": true, "IQ4_XS": true,
		"I8": true, "I16": true, "I32": true, "I64": true, "F64": true, "IQ1_M": true, "BF16": true,
		"Q4_0_4_4": true, "Q4_0_4_8": true, "Q4_0_8_8": true, "TQ1_0": true, "TQ2_0": true,
		"IQ4_NL_4_4": true, "IQ4_NL_4_8": true, "IQ4_NL_8_8": true, "MXFP4": true, "NVFP4": true,
		"Q1_0": true, "Q2_0": true,
	}
	if validQuants[quant] {
		return quant
	}
	return ""
}

// ErrNoInstalledMatch indicates a reference matched no artifact in the library
// state it was resolved against.
type ErrNoInstalledMatch struct {
	Reference string
	// State names the library state the reference was resolved against, so a
	// failed reconciliation does not report the artifact as "not installed"
	// when being uninstalled is precisely its subject.
	State string
	// Available holds the filenames in that state for the reference's
	// repository, so the diagnostic can name what the operator may select
	// instead of the selector that missed.
	Available []string
}

// state reports the library state to name in a diagnostic, defaulting to
// installed for a zero value written before the field existed.
func (e *ErrNoInstalledMatch) state() string {
	if e.State == "" {
		return "installed"
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
	// Matches holds the filenames of every artifact the reference matched.
	Matches []string
}

// Count reports how many artifacts the reference matched. It is derived from
// Matches so the two can never disagree.
func (e *ErrAmbiguousInstalledMatch) Count() int { return len(e.Matches) }

func (e *ErrAmbiguousInstalledMatch) Error() string {
	state := e.State
	if state == "" {
		state = "installed"
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
	repo, _, selector, err := parseRef(reference, "")
	if err != nil {
		return "", "", err
	}
	return repo, selector, nil
}

// ResolveInstalled resolves the user/repo:QUANT reference grammar against the
// installed model library, without issuing any network request.
func (m *Manager) ResolveInstalled(reference string) (LibraryEntry, error) {
	return m.resolveLibrary(reference, "installed")
}

// ResolveUnreconciled resolves the same user/repo:QUANT reference grammar
// against the artifacts that have no valid sidecar yet.
//
// Reconciliation is the one operation whose subject is by definition not
// installed, so it cannot go through ResolveInstalled. It still addresses that
// subject the way every other model command does: the reference is the
// catalog's identity, and an operator never has to switch to a filesystem path
// to name an artifact Coquena just listed.
func (m *Manager) ResolveUnreconciled(reference string) (LibraryEntry, error) {
	return m.resolveLibrary(reference, "unreconciled")
}

// resolveLibrary matches reference against the library entries in one state,
// so the reference grammar is written once and every caller applies it
// identically.
func (m *Manager) resolveLibrary(reference, state string) (LibraryEntry, error) {
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
		if entry.State != state || entry.Source.Repository != repo {
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
		return LibraryEntry{}, &ErrNoInstalledMatch{Reference: reference, State: state, Available: available}
	case 1:
		return matches[0], nil
	default:
		matched := make([]string, 0, len(matches))
		for _, entry := range matches {
			matched = append(matched, entry.Source.Filename)
		}
		return LibraryEntry{}, &ErrAmbiguousInstalledMatch{Reference: reference, State: state, Matches: matched}
	}
}
