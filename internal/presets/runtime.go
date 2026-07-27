package presets

import (
	"github.com/mstranich/coquena/internal/fields"
	"github.com/mstranich/coquena/internal/model"
)

// ModelSource is the attribution carried by a model-derived contribution. It is
// a distinct constant rather than a Field name so a read can never mistake a
// model-attributed contribution for a bound produced by a field.
const ModelSource = "model"

// Bound attributes one edge of a computed release range to the field that
// produced it.
type Bound struct {
	Release string `json:"release"`
	Field   string `json:"field"`
}

// ModelContribution is the runtime capability the preset's bound model implies,
// attributed to the model rather than to any field.
//
// It carries no release, because nothing Coquena reads establishes which
// llama.cpp release first supported a given quantization or hybrid
// attention/SSM inference. It is a stated capability requirement, and a reader
// tells it apart from a field-attributed bound structurally: Bound has Release
// and Field, this has Source == ModelSource and a list of requirements.
type ModelContribution struct {
	Source       string                        `json:"source"`
	Requirements []model.CapabilityRequirement `json:"requirements"`
}

// ComputedConstraint is the constraint implied by a preset rather than declared
// on it, always advisory. It has two independent sources: the availability of
// the fields the preset uses, reported as release bounds attributed to a field,
// and the preset's bound model, reported as Model and attributed to the model.
type ComputedConstraint struct {
	MinRelease *Bound             `json:"minRelease,omitempty"`
	MaxRelease *Bound             `json:"maxRelease,omitempty"`
	Model      *ModelContribution `json:"model,omitempty"`
}

// ComputeConstraint intersects the availability ranges of every field in
// values that declares availability for provider, attributing each bound to
// the field that produced it. It reports no model contribution; callers holding
// the preset's bound model call ComputeConstraintWithModel instead.
func ComputeConstraint(catalog fields.Catalog, provider fields.Provider, values []EffectiveValue) ComputedConstraint {
	return ComputeConstraintWithModel(catalog, provider, values, nil)
}

// ComputeConstraintWithModel computes the field-attributed release bounds and,
// when the preset is bound to a classified model, the capability that model
// implies as a second, separately attributed source.
//
// ADVISORY, BY SPECIFICATION. The model contribution is reported and nothing
// else: it never assigns a preset value, never selects or launches a runtime,
// and never blocks authoring, storing, renaming or removing a preset. It is
// also deliberately excluded from EvaluateRuntime's release comparison, since
// it states a capability rather than a release bound.
func ComputeConstraintWithModel(catalog fields.Catalog, provider fields.Provider, values []EffectiveValue, bound *model.Classification) ComputedConstraint {
	var out ComputedConstraint
	if bound != nil {
		if capability := model.DeriveRuntimeCapability(*bound); !capability.Empty() {
			out.Model = &ModelContribution{Source: ModelSource, Requirements: capability.Requirements}
		}
	}
	for _, v := range values {
		field, ok := catalog.Find(v.Field)
		if !ok || field.Availability == nil || field.Availability.Provider != provider {
			continue
		}
		if in := field.Availability.IntroducedIn; in != "" && fields.ValidReleaseTag(in) {
			if out.MinRelease == nil {
				out.MinRelease = &Bound{Release: in, Field: field.Name}
			} else if cmp, ok := fields.CompareReleaseTags(in, out.MinRelease.Release); ok && cmp > 0 {
				out.MinRelease = &Bound{Release: in, Field: field.Name}
			}
		}
		if rm := field.Availability.RemovedIn; rm != "" && fields.ValidReleaseTag(rm) {
			if out.MaxRelease == nil {
				out.MaxRelease = &Bound{Release: rm, Field: field.Name}
			} else if cmp, ok := fields.CompareReleaseTags(rm, out.MaxRelease.Release); ok && cmp < 0 {
				out.MaxRelease = &Bound{Release: rm, Field: field.Name}
			}
		}
	}
	return out
}

// EvaluateRuntime reports whether a runtime at the given official release
// admits the constraint formed by declared and computed together. It never
// blocks authoring; callers use it only for advisory reporting.
//
// computed.Model takes no part in this verdict. A model-implied capability is
// not a release bound, and turning it into one would require inventing the
// release that first supported it. It is reported alongside the verdict for the
// operator to read, never folded into it.
func EvaluateRuntime(provider fields.Provider, release string, declared *RuntimeConstraint, computed ComputedConstraint) fields.Status {
	if provider != fields.ProviderOfficial || !fields.ValidReleaseTag(release) {
		return fields.Unknown
	}
	bounds := []struct {
		release string
		isMin   bool
	}{}
	if computed.MinRelease != nil {
		bounds = append(bounds, struct {
			release string
			isMin   bool
		}{computed.MinRelease.Release, true})
	}
	if computed.MaxRelease != nil {
		bounds = append(bounds, struct {
			release string
			isMin   bool
		}{computed.MaxRelease.Release, false})
	}
	if declared != nil {
		if declared.MinRelease != "" && fields.ValidReleaseTag(declared.MinRelease) {
			bounds = append(bounds, struct {
				release string
				isMin   bool
			}{declared.MinRelease, true})
		}
		if declared.MaxRelease != "" && fields.ValidReleaseTag(declared.MaxRelease) {
			bounds = append(bounds, struct {
				release string
				isMin   bool
			}{declared.MaxRelease, false})
		}
	}
	if len(bounds) == 0 {
		return fields.Unknown
	}
	status := fields.Admitted
	for _, b := range bounds {
		cmp, ok := fields.CompareReleaseTags(release, b.release)
		if !ok {
			return fields.Unknown
		}
		if b.isMin && cmp < 0 {
			return fields.NotAdmitted
		}
		if !b.isMin && cmp >= 0 {
			return fields.NotAdmitted
		}
	}
	return status
}
