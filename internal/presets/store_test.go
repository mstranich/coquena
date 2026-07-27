package presets

import (
	"errors"
	"os"
	"path/filepath"
	"testing"

	"github.com/mstranich/coquena/internal/model"
)

func newTestStore(t *testing.T) *Store {
	t.Helper()
	home := t.TempDir()
	if err := os.MkdirAll(filepath.Join(home, "config"), 0o755); err != nil {
		t.Fatal(err)
	}
	return NewStore(home)
}

func TestLoadEmptyInstallation(t *testing.T) {
	s := newTestStore(t)
	d, err := s.Load()
	if err != nil {
		t.Fatal(err)
	}
	if !d.Global.IsGlobal() || len(d.Presets) != 0 {
		t.Fatalf("d=%+v", d)
	}
	if _, err := os.Stat(s.path()); !errors.Is(err, os.ErrNotExist) {
		t.Fatal("expected no document to be created by Load")
	}
}

func TestDefinePublishesFirstDocument(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Define(Preset{Name: "qwen3-fast"}); err != nil {
		t.Fatal(err)
	}
	data, err := os.ReadFile(s.path())
	if err != nil {
		t.Fatal(err)
	}
	if data[len(data)-1] != '\n' {
		t.Fatal("expected document to end with a newline")
	}
}

func TestDefineSurvivesRestart(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Define(Preset{Name: "qwen3-fast"}); err != nil {
		t.Fatal(err)
	}
	reloaded := NewStore(s.home)
	d, err := reloaded.Load()
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := d.Find("qwen3-fast"); !ok {
		t.Fatal("expected preset to survive a fresh Store instance")
	}
}

func TestDefineRejectsExistingName(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Define(Preset{Name: "qwen3-fast"}); err != nil {
		t.Fatal(err)
	}
	if _, err := s.Define(Preset{Name: "qwen3-fast"}); err == nil {
		t.Fatal("expected error defining an existing name")
	}
}

func TestRemovePreservesOthers(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Define(Preset{Name: "a"}); err != nil {
		t.Fatal(err)
	}
	if _, err := s.Define(Preset{Name: "b"}); err != nil {
		t.Fatal(err)
	}
	d, err := s.Remove("a")
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := d.Find("a"); ok {
		t.Fatal("expected a removed")
	}
	if _, ok := d.Find("b"); !ok {
		t.Fatal("expected b to survive")
	}
}

func TestRemoveRefusesGlobal(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Remove(GlobalName); err == nil {
		t.Fatal("expected error")
	}
}

func TestRenamePreservesState(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Define(Preset{Name: "qwen3-fst", Title: "Qwen3 Fast"}); err != nil {
		t.Fatal(err)
	}
	if _, _, err := s.SetValue(testCatalog(), "qwen3-fst", "context-size", float64(8192)); err != nil {
		t.Fatal(err)
	}
	d, err := s.Rename("qwen3-fst", "qwen3-fast")
	if err != nil {
		t.Fatal(err)
	}
	p, ok := d.Find("qwen3-fast")
	if !ok || p.Title != "Qwen3 Fast" || p.Values["context-size"] != float64(8192) {
		t.Fatalf("p=%+v ok=%v", p, ok)
	}
	if _, ok := d.Find("qwen3-fst"); ok {
		t.Fatal("expected old name gone")
	}
}

func TestRenameRefusesCollision(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Define(Preset{Name: "a"}); err != nil {
		t.Fatal(err)
	}
	if _, err := s.Define(Preset{Name: "b"}); err != nil {
		t.Fatal(err)
	}
	if _, err := s.Rename("a", "b"); err == nil {
		t.Fatal("expected error")
	}
	d, err := s.Load()
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := d.Find("a"); !ok {
		t.Fatal("expected a unchanged")
	}
}

func TestSetValueOnlyTouchesOwnLayer(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Define(Preset{Name: "qwen3-fast"}); err != nil {
		t.Fatal(err)
	}
	if _, err := s.SetTitle(GlobalName, ""); err != nil {
		t.Fatal(err)
	}
	if _, _, err := s.SetValue(testCatalog(), GlobalName, "context-size", float64(4096)); err != nil {
		t.Fatal(err)
	}
	d, _, err := s.SetValue(testCatalog(), "qwen3-fast", "context-size", float64(8192))
	if err != nil {
		t.Fatal(err)
	}
	if d.Global.Values["context-size"] != float64(4096) {
		t.Fatalf("global=%+v", d.Global.Values)
	}
	p, _ := d.Find("qwen3-fast")
	if p.Values["context-size"] != float64(8192) {
		t.Fatalf("p=%+v", p.Values)
	}
}

func TestUnsetValueReturnsToInheritance(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Define(Preset{Name: "qwen3-fast"}); err != nil {
		t.Fatal(err)
	}
	if _, _, err := s.SetValue(testCatalog(), GlobalName, "context-size", float64(4096)); err != nil {
		t.Fatal(err)
	}
	if _, _, err := s.SetValue(testCatalog(), "qwen3-fast", "context-size", float64(8192)); err != nil {
		t.Fatal(err)
	}
	d, err := s.UnsetValue(testCatalog(), "qwen3-fast", "context-size")
	if err != nil {
		t.Fatal(err)
	}
	p, _ := d.Find("qwen3-fast")
	values := Effective(d.Global, p)
	v, ok := findValue(values, "context-size")
	if !ok || v.Source != SourceGlobal || v.Value != float64(4096) {
		t.Fatalf("v=%+v ok=%v", v, ok)
	}
}

func TestSetValueRejectsInvalidValue(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Define(Preset{Name: "qwen3-fast"}); err != nil {
		t.Fatal(err)
	}
	if _, _, err := s.SetValue(testCatalog(), "qwen3-fast", "context-size", "not-a-number"); err == nil {
		t.Fatal("expected error")
	}
	d, err := s.Load()
	if err != nil {
		t.Fatal(err)
	}
	p, _ := d.Find("qwen3-fast")
	if len(p.Values) != 0 {
		t.Fatalf("expected no value stored, got %+v", p.Values)
	}
}

func TestBindModelRefusedOnGlobal(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.SetModel(GlobalName, ModelBinding{Requested: ModelReference{Raw: "org/repo:Q4_K_M"}}); err == nil {
		t.Fatal("expected error")
	}
}

func TestSetRuntimeAndUnsetRuntime(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Define(Preset{Name: "qwen3-fast"}); err != nil {
		t.Fatal(err)
	}
	d, err := s.SetRuntime("qwen3-fast", RuntimeConstraint{Backend: "cuda@12.4", MinRelease: "b6200"})
	if err != nil {
		t.Fatal(err)
	}
	p, _ := d.Find("qwen3-fast")
	if p.Runtime == nil || p.Runtime.Backend != "cuda@12.4" {
		t.Fatalf("p=%+v", p)
	}
	d, err = s.UnsetRuntime("qwen3-fast")
	if err != nil {
		t.Fatal(err)
	}
	p, _ = d.Find("qwen3-fast")
	if p.Runtime != nil {
		t.Fatalf("expected portable, got %+v", p.Runtime)
	}
}

func TestSetCompanionPersistsAndSurvivesRestart(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Define(Preset{Name: "gemma4-vision"}); err != nil {
		t.Fatal(err)
	}
	assignment := CompanionAssignment{
		Role:      model.RoleProjector,
		Requested: ModelReference{Provider: "huggingface", Repository: "google/Gemma4-GGUF", Quant: "mmproj", Raw: "google/Gemma4-GGUF:mmproj"},
		Resolved:  &ResolvedModel{Filename: "mmproj-F16.gguf"},
	}
	if _, err := s.SetCompanion("gemma4-vision", assignment); err != nil {
		t.Fatal(err)
	}
	d, err := NewStore(s.home).Load()
	if err != nil {
		t.Fatal(err)
	}
	p, _ := d.Find("gemma4-vision")
	stored, ok := p.Companion(model.RoleProjector)
	if !ok {
		t.Fatalf("p=%+v", p)
	}
	if stored.Requested.Raw != "google/Gemma4-GGUF:mmproj" || stored.Resolved == nil || stored.Resolved.Filename != "mmproj-F16.gguf" {
		t.Fatalf("stored=%+v", stored)
	}
}

func TestSetCompanionReplacesTheSameRoleOnly(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Define(Preset{Name: "gemma4-vision"}); err != nil {
		t.Fatal(err)
	}
	first := CompanionAssignment{Role: model.RoleProjector, Requested: ModelReference{Raw: "a/b:mmproj"}}
	draft := CompanionAssignment{Role: model.RoleDraft, Requested: ModelReference{Raw: "c/d:Q4_K_M"}}
	second := CompanionAssignment{Role: model.RoleProjector, Requested: ModelReference{Raw: "e/f:mmproj"}, UnmetCriterion: "overridden"}
	for _, a := range []CompanionAssignment{first, draft, second} {
		if _, err := s.SetCompanion("gemma4-vision", a); err != nil {
			t.Fatal(err)
		}
	}
	d, err := s.Load()
	if err != nil {
		t.Fatal(err)
	}
	p, _ := d.Find("gemma4-vision")
	if len(p.Companions) != 2 {
		t.Fatalf("companions=%+v", p.Companions)
	}
	projector, _ := p.Companion(model.RoleProjector)
	if projector.Requested.Raw != "e/f:mmproj" {
		t.Fatalf("projector=%+v", projector)
	}
	if diagnostic, ok := projector.Diagnostic(); !ok || diagnostic != "overridden" {
		t.Fatalf("diagnostic=%q ok=%v", diagnostic, ok)
	}
	if kept, ok := p.Companion(model.RoleDraft); !ok || kept.Requested.Raw != "c/d:Q4_K_M" {
		t.Fatalf("draft=%+v ok=%v", kept, ok)
	}
}

func TestUnsetCompanionRemovesOneRole(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Define(Preset{Name: "gemma4-vision"}); err != nil {
		t.Fatal(err)
	}
	if _, err := s.SetCompanion("gemma4-vision", CompanionAssignment{Role: model.RoleProjector, Requested: ModelReference{Raw: "a/b:mmproj"}}); err != nil {
		t.Fatal(err)
	}
	if _, err := s.SetCompanion("gemma4-vision", CompanionAssignment{Role: model.RoleDraft, Requested: ModelReference{Raw: "c/d:Q4_K_M"}}); err != nil {
		t.Fatal(err)
	}
	d, err := s.UnsetCompanion("gemma4-vision", model.RoleProjector)
	if err != nil {
		t.Fatal(err)
	}
	p, _ := d.Find("gemma4-vision")
	if _, ok := p.Companion(model.RoleProjector); ok {
		t.Fatalf("companions=%+v", p.Companions)
	}
	if _, ok := p.Companion(model.RoleDraft); !ok {
		t.Fatalf("companions=%+v", p.Companions)
	}
	// Removing an assignment the preset does not carry is accepted.
	if _, err := s.UnsetCompanion("gemma4-vision", model.RoleProjector); err != nil {
		t.Fatal(err)
	}
}

func TestSetCompanionRefusedOnGlobalAndOnUnknownRole(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Define(Preset{Name: "gemma4-vision"}); err != nil {
		t.Fatal(err)
	}
	if _, err := s.SetCompanion(GlobalName, CompanionAssignment{Role: model.RoleProjector}); err == nil {
		t.Fatal("expected the global preset to refuse a companion")
	}
	if _, err := s.SetCompanion("gemma4-vision", CompanionAssignment{Role: model.CompanionRole("adapter")}); err == nil {
		t.Fatal("expected an unknown role to be refused")
	}
}

func TestPublishFailureLeavesPreviousDocumentIntact(t *testing.T) {
	s := newTestStore(t)
	if _, err := s.Define(Preset{Name: "qwen3-fast"}); err != nil {
		t.Fatal(err)
	}
	before, err := os.ReadFile(s.path())
	if err != nil {
		t.Fatal(err)
	}
	s.ops.rename = func(string, string) error { return errors.New("boom") }
	if _, err := s.Define(Preset{Name: "qwen3-quality"}); err == nil {
		t.Fatal("expected error")
	}
	after, err := os.ReadFile(s.path())
	if err != nil {
		t.Fatal(err)
	}
	if string(before) != string(after) {
		t.Fatal("expected document unchanged after publish failure")
	}
}

func TestUnsupportedSchemaVersion(t *testing.T) {
	s := newTestStore(t)
	if err := os.WriteFile(s.path(), []byte(`{"schemaVersion":2,"global":{"name":"global"},"presets":[]}`), 0o600); err != nil {
		t.Fatal(err)
	}
	if _, err := s.Load(); err == nil {
		t.Fatal("expected error")
	}
}
