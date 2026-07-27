package presets

import (
	"testing"

	"github.com/mstranich/coquena/internal/fields"
)

func availabilityCatalog() fields.Catalog {
	return fields.Catalog{SchemaVersion: fields.SchemaVersion, Fields: []fields.Field{
		{Name: "temp", Kind: fields.KindString},
		{Name: "n-gpu-layers", Kind: fields.KindInteger, Availability: &fields.Availability{Provider: fields.ProviderOfficial, IntroducedIn: "b6180"}},
		{Name: "spec-type", Kind: fields.KindEnum, EnumOptions: []string{"none"}, Availability: &fields.Availability{Provider: fields.ProviderOfficial, IntroducedIn: "b6100", RemovedIn: "b7000"}},
	}}
}

func TestComputeConstraintAttributesBound(t *testing.T) {
	values := []EffectiveValue{{Field: "n-gpu-layers", Value: float64(99), Source: SourceOwn}}
	got := ComputeConstraint(availabilityCatalog(), fields.ProviderOfficial, values)
	if got.MinRelease == nil || got.MinRelease.Release != "b6180" || got.MinRelease.Field != "n-gpu-layers" {
		t.Fatalf("got=%+v", got)
	}
}

func TestComputeConstraintIntersectsMultipleFields(t *testing.T) {
	values := []EffectiveValue{
		{Field: "n-gpu-layers", Value: float64(99), Source: SourceOwn},
		{Field: "spec-type", Value: "none", Source: SourceOwn},
	}
	got := ComputeConstraint(availabilityCatalog(), fields.ProviderOfficial, values)
	if got.MinRelease == nil || got.MinRelease.Release != "b6180" {
		t.Fatalf("min=%+v", got.MinRelease)
	}
	if got.MaxRelease == nil || got.MaxRelease.Release != "b7000" {
		t.Fatalf("max=%+v", got.MaxRelease)
	}
}

func TestComputeConstraintIgnoresFieldsWithoutAvailability(t *testing.T) {
	values := []EffectiveValue{{Field: "temp", Value: "0.7", Source: SourceOwn}}
	got := ComputeConstraint(availabilityCatalog(), fields.ProviderOfficial, values)
	if got.MinRelease != nil || got.MaxRelease != nil {
		t.Fatalf("got=%+v", got)
	}
}

func TestEvaluateRuntimePortablePreset(t *testing.T) {
	if got := EvaluateRuntime(fields.ProviderOfficial, "b6234", nil, ComputedConstraint{}); got != fields.Unknown {
		t.Fatalf("got=%v", got)
	}
}

func TestEvaluateRuntimeAdmitted(t *testing.T) {
	computed := ComputedConstraint{MinRelease: &Bound{Release: "b6180", Field: "n-gpu-layers"}}
	if got := EvaluateRuntime(fields.ProviderOfficial, "b6234", nil, computed); got != fields.Admitted {
		t.Fatalf("got=%v", got)
	}
}

func TestEvaluateRuntimeNotAdmittedBelowComputedMin(t *testing.T) {
	computed := ComputedConstraint{MinRelease: &Bound{Release: "b6180", Field: "n-gpu-layers"}}
	if got := EvaluateRuntime(fields.ProviderOfficial, "b6000", nil, computed); got != fields.NotAdmitted {
		t.Fatalf("got=%v", got)
	}
}

func TestEvaluateRuntimeRespectsDeclaredBound(t *testing.T) {
	declared := &RuntimeConstraint{Provider: string(fields.ProviderOfficial), MinRelease: "b6200"}
	if got := EvaluateRuntime(fields.ProviderOfficial, "b6100", declared, ComputedConstraint{}); got != fields.NotAdmitted {
		t.Fatalf("got=%v", got)
	}
}

func TestEvaluateRuntimeUnknownForUnparsableRelease(t *testing.T) {
	if got := EvaluateRuntime(fields.ProviderOfficial, "not-a-tag", nil, ComputedConstraint{}); got != fields.Unknown {
		t.Fatalf("got=%v", got)
	}
}
