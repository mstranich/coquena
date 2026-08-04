package presets

import (
	"errors"
	"fmt"
	"time"

	"github.com/mstranich/coquena/internal/model"
)

// ModelResolver resolves a user/repo:QUANT reference against the installed
// library without contacting any network service. *model.Manager satisfies
// this through ResolveInstalled.
type ModelResolver interface {
	ResolveInstalled(reference string) (model.LibraryEntry, error)
}

// BindModel resolves reference against resolver and returns the binding to
// store. When resolution fails and allowMissing is false, it returns the
// resolution error. When allowMissing is true, an unresolved reference is
// stored with a requested layer and no resolved layer.
func BindModel(resolver ModelResolver, reference string, allowMissing bool) (ModelBinding, error) {
	entry, err := resolver.ResolveInstalled(reference)
	if err != nil {
		var noMatch *model.ErrNoInstalledMatch
		if errors.As(err, &noMatch) {
			if allowMissing {
				requested, parseErr := parseModelReference(reference)
				if parseErr != nil {
					return ModelBinding{}, parseErr
				}
				return ModelBinding{Requested: requested}, nil
			}
			return ModelBinding{}, fmt.Errorf("%w; pass --allow-missing to bind it anyway", err)
		}
		return ModelBinding{}, err
	}
	requested, err := parseModelReference(reference)
	if err != nil {
		return ModelBinding{}, err
	}
	return ModelBinding{
		Requested: requested,
		Resolved: &ResolvedModel{
			CanonicalReference: model.CanonicalReference(entry.Source),
			Filename:           entry.Source.Filename,
			ResolvedRevision:   entry.Source.ResolvedRevision,
			ResolvedAt:         time.Now().UTC().Format(time.RFC3339),
		},
	}, nil
}

func parseModelReference(reference string) (ModelReference, error) {
	ref, err := model.ParseModelReference(reference)
	if err != nil {
		return ModelReference{}, err
	}
	return ModelReference{
		Provider:     "huggingface",
		Repository:   ref.Repository,
		Revision:     ref.Revision,
		SelectorKind: string(ref.SelectorKind),
		Quant:        ref.Selector,
		Raw:          reference,
	}, nil
}

// BindingState preserves the recovery-relevant outcomes of evaluating a
// durable canonical binding and its unchanged authored selector.
type BindingState string

const (
	BindingUnresolved BindingState = "unresolved"
	BindingSame       BindingState = "resolved-same"
	BindingDifferent  BindingState = "resolved-different"
	BindingMissing    BindingState = "missing"
	BindingAmbiguous  BindingState = "ambiguous"
)

// ResolvedReference returns the canonical singleton reference represented by a
// binding, deriving it for schema-1 documents written before the explicit field
// was added.
func ResolvedReference(binding ModelBinding) string {
	if binding.Resolved == nil {
		return ""
	}
	if binding.Resolved.CanonicalReference != "" {
		return binding.Resolved.CanonicalReference
	}
	base := binding.Requested.Repository
	if binding.Resolved.ResolvedRevision != "" {
		base += "@" + binding.Resolved.ResolvedRevision
	}
	return base + ":" + binding.Resolved.Filename
}

// EvaluateBinding checks the canonical artifact first. The authored logical
// selector is consulted only to explain what replaced or obscured it.
func EvaluateBinding(resolver ModelResolver, binding ModelBinding) BindingState {
	if binding.Resolved == nil {
		return BindingUnresolved
	}
	if _, err := resolver.ResolveInstalled(ResolvedReference(binding)); err == nil {
		return BindingSame
	}
	entry, err := resolver.ResolveInstalled(binding.Requested.Raw)
	if err != nil {
		var ambiguous *model.ErrAmbiguousInstalledMatch
		if errors.As(err, &ambiguous) {
			return BindingAmbiguous
		}
		return BindingMissing
	}
	if entry.Source.Filename == binding.Resolved.Filename &&
		(binding.Resolved.ResolvedRevision == "" || entry.Source.ResolvedRevision == binding.Resolved.ResolvedRevision) {
		return BindingSame
	}
	return BindingDifferent
}

// StaleModel remains as a compatibility convenience. Only a missing or
// uniquely different artifact is stale; ambiguity alone is not.
func StaleModel(resolver ModelResolver, binding ModelBinding) bool {
	state := EvaluateBinding(resolver, binding)
	return state == BindingMissing || state == BindingDifferent
}
