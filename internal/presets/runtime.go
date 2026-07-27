package presets

import "github.com/mstranich/coquena/internal/fields"

// Bound attributes one edge of a computed release range to the field that
// produced it.
type Bound struct {
	Release string `json:"release"`
	Field   string `json:"field"`
}

// ComputedConstraint is the release range implied by the availability of the
// fields a preset uses, always advisory.
type ComputedConstraint struct {
	MinRelease *Bound `json:"minRelease,omitempty"`
	MaxRelease *Bound `json:"maxRelease,omitempty"`
}

// ComputeConstraint intersects the availability ranges of every field in
// values that declares availability for provider, attributing each bound to
// the field that produced it.
func ComputeConstraint(catalog fields.Catalog, provider fields.Provider, values []EffectiveValue) ComputedConstraint {
	var out ComputedConstraint
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
