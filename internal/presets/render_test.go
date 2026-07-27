package presets

import (
	"testing"

	"github.com/mstranich/coquena/internal/fields"
	"github.com/mstranich/coquena/internal/model"
)

func renderCatalog() fields.Catalog {
	return fields.Catalog{SchemaVersion: fields.SchemaVersion, Fields: []fields.Field{
		{Name: "temp", Kind: fields.KindString},
		{Name: "context-size", Kind: fields.KindInteger, ShortArgument: "c"},
		{Name: "n-gpu-layers", Kind: fields.KindInteger, Availability: &fields.Availability{Provider: fields.ProviderOfficial, IntroducedIn: "b6180"}},
	}}
}

func TestRenderGlobalAndModelSections(t *testing.T) {
	doc := Document{
		SchemaVersion: SchemaVersion,
		Global:        Preset{Name: GlobalName, Values: map[string]any{"temp": "0.7"}},
		Presets: []Preset{{
			Name:   "qwen3-fast",
			Model:  &ModelBinding{Requested: ModelReference{Provider: "huggingface", Repository: "org/repo", Raw: "org/repo:Q4_K_M"}, Resolved: &ResolvedModel{Filename: "m-Q4_K_M.gguf"}},
			Values: map[string]any{"context-size": float64(8192)},
		}},
	}
	result := Render(doc, renderCatalog(), fields.ProviderOfficial, "b6234", "/models")
	if len(result.Sections) != 2 || result.Sections[0].Name != "*" || result.Sections[1].Name != "qwen3-fast" {
		t.Fatalf("sections=%+v", result.Sections)
	}
}

func TestRenderEmitsCanonicalNamesOnly(t *testing.T) {
	doc := Document{SchemaVersion: SchemaVersion, Global: Preset{Name: GlobalName}, Presets: []Preset{
		{Name: "qwen3-fast", Values: map[string]any{"context-size": float64(4096)}},
	}}
	result := Render(doc, renderCatalog(), fields.ProviderOfficial, "b6234", "/models")
	if result.Sections[1].Entries[0].Key != "context-size" {
		t.Fatalf("entries=%+v", result.Sections[1].Entries)
	}
}

func TestRenderEmitsInstalledModelPath(t *testing.T) {
	doc := Document{SchemaVersion: SchemaVersion, Global: Preset{Name: GlobalName}, Presets: []Preset{
		{Name: "qwen3-fast", Model: &ModelBinding{Requested: ModelReference{Provider: "huggingface", Repository: "org/repo"}, Resolved: &ResolvedModel{Filename: "m.gguf"}}},
	}}
	result := Render(doc, renderCatalog(), fields.ProviderOfficial, "b6234", "/models")
	entries := result.Sections[1].Entries
	if len(entries) != 1 || entries[0].Key != "model" {
		t.Fatalf("entries=%+v", entries)
	}
}

func TestRenderEmitsRequestedReferenceWhenNotInstalled(t *testing.T) {
	doc := Document{SchemaVersion: SchemaVersion, Global: Preset{Name: GlobalName}, Presets: []Preset{
		{Name: "qwen3-fast", Model: &ModelBinding{Requested: ModelReference{Raw: "org/repo:Q4_K_M"}}},
	}}
	result := Render(doc, renderCatalog(), fields.ProviderOfficial, "b6234", "/models")
	entries := result.Sections[1].Entries
	if len(entries) != 1 || entries[0].Key != "hf" || entries[0].Value != "org/repo:Q4_K_M" {
		t.Fatalf("entries=%+v", entries)
	}
}

func TestRenderOmitsNotAdmittedPreset(t *testing.T) {
	doc := Document{SchemaVersion: SchemaVersion, Global: Preset{Name: GlobalName}, Presets: []Preset{
		{Name: "needs-newer", Values: map[string]any{"n-gpu-layers": float64(99)}},
	}}
	result := Render(doc, renderCatalog(), fields.ProviderOfficial, "b6000", "/models")
	if len(result.Sections) != 1 {
		t.Fatalf("sections=%+v", result.Sections)
	}
	if len(result.Diagnostics) != 1 {
		t.Fatalf("diagnostics=%+v", result.Diagnostics)
	}
}

func TestRenderFlattensInheritedValues(t *testing.T) {
	doc := Document{
		SchemaVersion: SchemaVersion,
		Global:        Preset{Name: GlobalName, Values: map[string]any{"temp": "0.7"}},
		Presets:       []Preset{{Name: "qwen3-fast", Values: map[string]any{"context-size": float64(8192)}}},
	}
	result := Render(doc, renderCatalog(), fields.ProviderOfficial, "b6234", "/models")
	for _, e := range result.Sections[1].Entries {
		if e.Key == "temp" {
			t.Fatal("expected inherited value to not be repeated in the preset section")
		}
	}
}

func TestRenderIsDeterministic(t *testing.T) {
	doc := Document{
		SchemaVersion: SchemaVersion,
		Global:        Preset{Name: GlobalName, Values: map[string]any{"temp": "0.7"}},
		Presets:       []Preset{{Name: "b-preset", Values: map[string]any{"context-size": float64(1)}}, {Name: "a-preset", Values: map[string]any{"context-size": float64(2)}}},
	}
	first := Format(Render(doc, renderCatalog(), fields.ProviderOfficial, "b6234", "/models"))
	second := Format(Render(doc, renderCatalog(), fields.ProviderOfficial, "b6234", "/models"))
	if first != second {
		t.Fatalf("first=%q second=%q", first, second)
	}
}

func TestRenderGoldenFile(t *testing.T) {
	doc := Document{
		SchemaVersion: SchemaVersion,
		Global:        Preset{Name: GlobalName, Values: map[string]any{"temp": "0.7"}},
		Presets: []Preset{{
			Name:   "qwen3-fast",
			Model:  &ModelBinding{Requested: ModelReference{Provider: "huggingface", Repository: "qwen/Qwen3-4B-GGUF", Raw: "qwen/Qwen3-4B-GGUF:Q4_K_M"}, Resolved: &ResolvedModel{Filename: "Qwen3-4B-Q4_K_M.gguf"}},
			Values: map[string]any{"context-size": float64(8192)},
		}},
	}
	got := Format(Render(doc, renderCatalog(), fields.ProviderOfficial, "b6234", "/models"))
	want := "[*]\ntemp = 0.7\n\n[qwen3-fast]\nmodel = " + want_modelPath(t) + "\ncontext-size = 8192\n"
	if got != want {
		t.Fatalf("got=%q want=%q", got, want)
	}
}

func want_modelPath(t *testing.T) string {
	t.Helper()
	target, err := model.PathFor("/models", "qwen/Qwen3-4B-GGUF", "Qwen3-4B-Q4_K_M.gguf")
	if err != nil {
		t.Fatal(err)
	}
	return target
}

func TestRenderWithUninstalledRuntimeStillRenders(t *testing.T) {
	doc := Document{SchemaVersion: SchemaVersion, Global: Preset{Name: GlobalName}, Presets: []Preset{
		{Name: "qwen3-fast", Values: map[string]any{"context-size": float64(1)}},
	}}
	result := Render(doc, renderCatalog(), fields.ProviderOfficial, "b6234", "/models")
	if len(result.Sections) != 2 {
		t.Fatalf("sections=%+v", result.Sections)
	}
}
