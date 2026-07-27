package presets

import (
	"testing"

	"github.com/mstranich/coquena/internal/fields"
	"github.com/mstranich/coquena/internal/model"
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

func modelFileType(v int64) *int64 { return &v }

func TestComputeConstraintWithModelReportsTernaryContribution(t *testing.T) {
	bound := &model.Classification{Type: model.TypeModel, Architecture: "qwen35", FileType: modelFileType(40)}
	got := ComputeConstraintWithModel(availabilityCatalog(), fields.ProviderOfficial, nil, bound)
	if got.Model == nil {
		t.Fatalf("expected a model contribution")
	}
	if got.Model.Source != ModelSource {
		t.Fatalf("source=%q", got.Model.Source)
	}
	if len(got.Model.Requirements) == 0 {
		t.Fatalf("got=%+v", got.Model)
	}
}

func TestComputeConstraintWithModelReportsSSMContribution(t *testing.T) {
	bound := &model.Classification{Type: model.TypeModel, Architecture: "qwen35", FileType: modelFileType(2), SSM: true}
	got := ComputeConstraintWithModel(availabilityCatalog(), fields.ProviderOfficial, nil, bound)
	if got.Model == nil {
		t.Fatalf("expected a model contribution")
	}
}

func TestComputeConstraintWithModelReportsNothingForOrdinaryModel(t *testing.T) {
	bound := &model.Classification{Type: model.TypeModel, Architecture: "gemma4", FileType: modelFileType(2)}
	got := ComputeConstraintWithModel(availabilityCatalog(), fields.ProviderOfficial, nil, bound)
	if got.Model != nil {
		t.Fatalf("got=%+v", got.Model)
	}
}

// The two sources are reported separately: a field bound stays attributed to
// its field, the model contribution stays attributed to the model.
func TestComputeConstraintWithModelKeepsFieldBoundsSeparate(t *testing.T) {
	values := []EffectiveValue{{Field: "n-gpu-layers", Value: float64(99), Source: SourceOwn}}
	bound := &model.Classification{Type: model.TypeModel, Architecture: "qwen35", FileType: modelFileType(40), SSM: true}
	got := ComputeConstraintWithModel(availabilityCatalog(), fields.ProviderOfficial, values, bound)
	if got.MinRelease == nil || got.MinRelease.Release != "b6180" || got.MinRelease.Field != "n-gpu-layers" {
		t.Fatalf("min=%+v", got.MinRelease)
	}
	if got.Model == nil || got.Model.Source != ModelSource {
		t.Fatalf("model=%+v", got.Model)
	}
	// The model contribution states capabilities, never a release bound, so it
	// cannot be confused with the field-attributed bounds and does not move the
	// advisory verdict.
	if got := EvaluateRuntime(fields.ProviderOfficial, "b6234", nil, got); got != fields.Admitted {
		t.Fatalf("status=%v", got)
	}
}

func TestComputeConstraintWithoutModelReportsNoContribution(t *testing.T) {
	values := []EffectiveValue{{Field: "n-gpu-layers", Value: float64(99), Source: SourceOwn}}
	if got := ComputeConstraint(availabilityCatalog(), fields.ProviderOfficial, values); got.Model != nil {
		t.Fatalf("got=%+v", got.Model)
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
