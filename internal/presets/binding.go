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
			Filename:         entry.Source.Filename,
			ResolvedRevision: entry.Source.ResolvedRevision,
			ResolvedAt:       time.Now().UTC().Format(time.RFC3339),
		},
	}, nil
}

func parseModelReference(reference string) (ModelReference, error) {
	repo, quant, err := model.SplitReference(reference)
	if err != nil {
		return ModelReference{}, err
	}
	return ModelReference{Provider: "huggingface", Repository: repo, Quant: quant, Raw: reference}, nil
}

// StaleModel reports whether a resolved binding's filename is no longer
// present among the resolver's installed artifacts for the same repository.
func StaleModel(resolver ModelResolver, binding ModelBinding) bool {
	if binding.Resolved == nil {
		return false
	}
	entry, err := resolver.ResolveInstalled(binding.Requested.Raw)
	if err != nil {
		return true
	}
	return entry.Source.Filename != binding.Resolved.Filename
}
