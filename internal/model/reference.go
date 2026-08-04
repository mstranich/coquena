package model

import (
	"fmt"
	"net/url"
	"strings"
)

// SelectorKind distinguishes a logical catalog selector from an exact
// repository artifact name. Both occupy the same colon-delimited slot.
type SelectorKind string

const (
	SelectorAbsent       SelectorKind = "absent"
	SelectorQuantization SelectorKind = "quantization"
	SelectorFilename     SelectorKind = "filename"
)

// ParsedReference is the lossless model-reference syntax tree shared by remote
// and installed resolution.
type ParsedReference struct {
	Repository       string
	Revision         string
	RevisionExplicit bool
	Selector         string
	SelectorKind     SelectorKind
}

// RequestedRevision returns the remote revision to request. Omission defaults
// remotely to main but remains distinguishable for local resolution.
func (r ParsedReference) RequestedRevision() string {
	if r.RevisionExplicit {
		return r.Revision
	}
	return "main"
}

// ParseModelReference parses repository syntax without resolving it.
func ParseModelReference(input string) (ParsedReference, error) {
	if u, err := url.Parse(input); err == nil && u.Host == "huggingface.co" {
		parts := strings.Split(strings.Trim(u.Path, "/"), "/")
		if len(parts) < 5 || parts[2] != "resolve" {
			return ParsedReference{}, fmt.Errorf("invalid Hugging Face resolve URL")
		}
		selector := strings.Join(parts[4:], "/")
		return ParsedReference{
			Repository:       strings.Join(parts[:2], "/"),
			Revision:         parts[3],
			RevisionExplicit: true,
			Selector:         selector,
			SelectorKind:     selectorKind(selector),
		}, nil
	}
	if strings.Count(input, ":") > 1 {
		return ParsedReference{}, fmt.Errorf("model reference accepts one selector after ':'")
	}
	parts := strings.SplitN(input, ":", 2)
	repository := parts[0]
	selector := ""
	if len(parts) == 2 {
		selector = parts[1]
		if selector == "" {
			return ParsedReference{}, fmt.Errorf("model reference selector is empty")
		}
	}
	revision := ""
	explicit := false
	if strings.Count(repository, "@") > 1 {
		return ParsedReference{}, fmt.Errorf("model reference accepts one revision after '@'")
	}
	if at := strings.Index(repository, "@"); at != -1 {
		revision = repository[at+1:]
		repository = repository[:at]
		explicit = true
		if revision == "" {
			return ParsedReference{}, fmt.Errorf("model reference revision is empty")
		}
	}
	if strings.Count(repository, "/") != 1 {
		return ParsedReference{}, fmt.Errorf("repository must be namespace/name")
	}
	return ParsedReference{
		Repository:       repository,
		Revision:         revision,
		RevisionExplicit: explicit,
		Selector:         selector,
		SelectorKind:     selectorKind(selector),
	}, nil
}

func selectorKind(selector string) SelectorKind {
	if selector == "" {
		return SelectorAbsent
	}
	if strings.HasSuffix(strings.ToLower(selector), ".gguf") {
		return SelectorFilename
	}
	return SelectorQuantization
}

// CanonicalReference formats one concrete artifact from trusted provenance.
func CanonicalReference(source Source) string {
	base := source.Repository
	if source.Provider != "local" && source.ResolvedRevision != "" {
		base += "@" + source.ResolvedRevision
	}
	return base + ":" + source.Filename
}

// LogicalReference formats the declared quantization class when available.
func LogicalReference(entry LibraryEntry) string {
	if entry.Classification == nil || entry.Classification.FileType == nil {
		return ""
	}
	base := entry.Source.Repository
	if entry.Source.Provider != "local" && entry.Source.ResolvedRevision != "" {
		base += "@" + entry.Source.ResolvedRevision
	}
	return base + ":" + LlamaFileTypeDisplayName(*entry.Classification.FileType)
}
