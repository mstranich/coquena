package presets

import (
	"testing"

	"github.com/mstranich/coquena/internal/fields"
)

func testCatalog() fields.Catalog {
	return fields.Catalog{SchemaVersion: fields.SchemaVersion, Fields: []fields.Field{
		{Name: "context-size", Kind: fields.KindInteger, ShortArgument: "c"},
		{Name: "spec-type", Kind: fields.KindEnum, EnumOptions: []string{"none", "draft-simple"}},
		{Name: "n-gpu-layers", Kind: fields.KindInteger, Parent: "gpu", ParentValue: true},
		{Name: "gpu", Kind: fields.KindBoolean},
	}}
}

func TestValidateValueAcceptsValidInteger(t *testing.T) {
	got, err := ValidateValue(testCatalog(), "context-size", float64(8192), nil)
	if err != nil || got.Field != "context-size" {
		t.Fatalf("got=%+v err=%v", got, err)
	}
}

func TestValidateValueRejectsWrongKind(t *testing.T) {
	if _, err := ValidateValue(testCatalog(), "context-size", "not-a-number", nil); err == nil {
		t.Fatal("expected error")
	}
}

func TestValidateValueRejectsUnknownField(t *testing.T) {
	if _, err := ValidateValue(testCatalog(), "does-not-exist", float64(1), nil); err == nil {
		t.Fatal("expected error")
	}
}

func TestValidateValueRejectsEnumOutsideOptions(t *testing.T) {
	if _, err := ValidateValue(testCatalog(), "spec-type", "bogus", nil); err == nil {
		t.Fatal("expected error")
	}
}

func TestValidateValueResolvesAlias(t *testing.T) {
	got, err := ValidateValue(testCatalog(), "c", float64(4096), nil)
	if err != nil || got.Field != "context-size" {
		t.Fatalf("got=%+v err=%v", got, err)
	}
}

func TestValidateValueWarnsOnUnsatisfiedParent(t *testing.T) {
	got, err := ValidateValue(testCatalog(), "n-gpu-layers", float64(99), map[string]any{})
	if err != nil {
		t.Fatal(err)
	}
	if got.Diagnostic == "" {
		t.Fatal("expected advisory diagnostic")
	}
}

func TestValidateValueNoDiagnosticWhenParentSatisfied(t *testing.T) {
	got, err := ValidateValue(testCatalog(), "n-gpu-layers", float64(99), map[string]any{"gpu": true})
	if err != nil {
		t.Fatal(err)
	}
	if got.Diagnostic != "" {
		t.Fatalf("diagnostic=%q", got.Diagnostic)
	}
}
