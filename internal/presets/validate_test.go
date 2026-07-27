package presets

import "testing"

func emptyDoc() Document {
	return Document{SchemaVersion: SchemaVersion, Global: Preset{Name: GlobalName}}
}

func TestValidateDocumentAcceptsEmpty(t *testing.T) {
	if err := ValidateDocument(emptyDoc()); err != nil {
		t.Fatal(err)
	}
}

func TestValidateDocumentRejectsBadName(t *testing.T) {
	d := emptyDoc()
	d.Presets = []Preset{{Name: "Qwen3 Fast"}}
	if err := ValidateDocument(d); err == nil {
		t.Fatal("expected error")
	}
}

func TestValidateDocumentAcceptsDottedVersionInName(t *testing.T) {
	d := emptyDoc()
	d.Presets = []Preset{{Name: "qwen3.5-4b-fast"}, {Name: "ornith1.0-small"}}
	if err := ValidateDocument(d); err != nil {
		t.Fatal(err)
	}
}

func TestValidateDocumentRejectsUppercaseName(t *testing.T) {
	d := emptyDoc()
	d.Presets = []Preset{{Name: "Qwen3.5-4B"}}
	if err := ValidateDocument(d); err == nil {
		t.Fatal("expected error")
	}
}

func TestValidateDocumentRejectsLeadingOrTrailingSeparator(t *testing.T) {
	for _, name := range []string{".qwen3", "qwen3.", "-qwen3", "qwen3-", "qwen3..5", "qwen3--5"} {
		d := emptyDoc()
		d.Presets = []Preset{{Name: name}}
		if err := ValidateDocument(d); err == nil {
			t.Fatalf("expected error for %q", name)
		}
	}
}

func TestValidateDocumentRejectsReservedName(t *testing.T) {
	for _, name := range []string{"global", "render"} {
		d := emptyDoc()
		d.Presets = []Preset{{Name: name}}
		if err := ValidateDocument(d); err == nil {
			t.Fatalf("expected error for %q", name)
		}
	}
}

func TestValidateDocumentRejectsDuplicateName(t *testing.T) {
	d := emptyDoc()
	d.Presets = []Preset{{Name: "qwen3-fast"}, {Name: "qwen3-fast"}}
	if err := ValidateDocument(d); err == nil {
		t.Fatal("expected error")
	}
}

func TestValidateDocumentRejectsPinWithCapability(t *testing.T) {
	d := emptyDoc()
	d.Presets = []Preset{{Name: "qwen3-fast", Runtime: &RuntimeConstraint{RuntimeID: "llama-b6234", Backend: "cuda@12.4"}}}
	if err := ValidateDocument(d); err == nil {
		t.Fatal("expected error")
	}
}

func TestValidateDocumentAcceptsPinAlone(t *testing.T) {
	d := emptyDoc()
	d.Presets = []Preset{{Name: "qwen3-fast", Runtime: &RuntimeConstraint{RuntimeID: "llama-b6234"}}}
	if err := ValidateDocument(d); err != nil {
		t.Fatal(err)
	}
}

func TestValidateDocumentAcceptsCapabilityAlone(t *testing.T) {
	d := emptyDoc()
	d.Presets = []Preset{{Name: "qwen3-fast", Runtime: &RuntimeConstraint{Backend: "cuda@12.4", MinRelease: "b6200"}}}
	if err := ValidateDocument(d); err != nil {
		t.Fatal(err)
	}
}

func TestValidateDocumentRejectsGlobalWithModel(t *testing.T) {
	d := emptyDoc()
	d.Global.Model = &ModelBinding{Requested: ModelReference{Raw: "org/repo:Q4_K_M"}}
	if err := ValidateDocument(d); err == nil {
		t.Fatal("expected error")
	}
}
