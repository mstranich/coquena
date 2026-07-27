package presets

import (
	"errors"
	"os"
	"path/filepath"
	"testing"

	"github.com/mstranich/coquena/internal/fields"
)

func newFieldsStore(t *testing.T, home string) *fields.Store {
	t.Helper()
	return fields.NewStore(home)
}

func removalCatalog(t *testing.T, fieldStore *fields.Store) fields.Catalog {
	t.Helper()
	if _, err := fieldStore.Define(fields.Field{Name: "gpu", Kind: fields.KindBoolean}); err != nil {
		t.Fatal(err)
	}
	if _, err := fieldStore.Define(fields.Field{Name: "n-gpu-layers", Kind: fields.KindInteger, Parent: "gpu", ParentValue: true}); err != nil {
		t.Fatal(err)
	}
	if _, err := fieldStore.Define(fields.Field{Name: "context-size", Kind: fields.KindInteger}); err != nil {
		t.Fatal(err)
	}
	c, err := fieldStore.Load()
	if err != nil {
		t.Fatal(err)
	}
	return c
}

func TestReferencingPresetsFindsDirectReference(t *testing.T) {
	home := t.TempDir()
	if err := os.MkdirAll(filepath.Join(home, "config"), 0o755); err != nil {
		t.Fatal(err)
	}
	fieldStore := newFieldsStore(t, home)
	catalog := removalCatalog(t, fieldStore)
	presetStore := NewStore(home)
	if _, err := presetStore.Define(Preset{Name: "qwen3-fast"}); err != nil {
		t.Fatal(err)
	}
	if _, _, err := presetStore.SetValue(catalog, "qwen3-fast", "context-size", float64(4096)); err != nil {
		t.Fatal(err)
	}
	doc, err := presetStore.Load()
	if err != nil {
		t.Fatal(err)
	}
	got := ReferencingPresets(doc, catalog, "context-size")
	if len(got) != 1 || got[0] != "qwen3-fast" {
		t.Fatalf("got=%v", got)
	}
}

func TestReferencingPresetsFindsDescendantReference(t *testing.T) {
	home := t.TempDir()
	if err := os.MkdirAll(filepath.Join(home, "config"), 0o755); err != nil {
		t.Fatal(err)
	}
	fieldStore := newFieldsStore(t, home)
	catalog := removalCatalog(t, fieldStore)
	presetStore := NewStore(home)
	if _, err := presetStore.Define(Preset{Name: "qwen3-fast"}); err != nil {
		t.Fatal(err)
	}
	if _, _, err := presetStore.SetValue(catalog, "qwen3-fast", "gpu", true); err != nil {
		t.Fatal(err)
	}
	if _, _, err := presetStore.SetValue(catalog, "qwen3-fast", "n-gpu-layers", float64(99)); err != nil {
		t.Fatal(err)
	}
	doc, err := presetStore.Load()
	if err != nil {
		t.Fatal(err)
	}
	got := ReferencingPresets(doc, catalog, "gpu")
	if len(got) != 1 || got[0] != "qwen3-fast" {
		t.Fatalf("got=%v", got)
	}
}

func TestReferencingPresetsNamesGlobal(t *testing.T) {
	home := t.TempDir()
	if err := os.MkdirAll(filepath.Join(home, "config"), 0o755); err != nil {
		t.Fatal(err)
	}
	fieldStore := newFieldsStore(t, home)
	catalog := removalCatalog(t, fieldStore)
	presetStore := NewStore(home)
	if _, _, err := presetStore.SetValue(catalog, GlobalName, "context-size", float64(4096)); err != nil {
		t.Fatal(err)
	}
	doc, err := presetStore.Load()
	if err != nil {
		t.Fatal(err)
	}
	got := ReferencingPresets(doc, catalog, "context-size")
	if len(got) != 1 || got[0] != GlobalName {
		t.Fatalf("got=%v", got)
	}
}

func TestRemoveFieldRefusesWithoutForce(t *testing.T) {
	home := t.TempDir()
	if err := os.MkdirAll(filepath.Join(home, "config"), 0o755); err != nil {
		t.Fatal(err)
	}
	fieldStore := newFieldsStore(t, home)
	catalog := removalCatalog(t, fieldStore)
	presetStore := NewStore(home)
	if _, err := presetStore.Define(Preset{Name: "qwen3-fast"}); err != nil {
		t.Fatal(err)
	}
	if _, _, err := presetStore.SetValue(catalog, "qwen3-fast", "context-size", float64(4096)); err != nil {
		t.Fatal(err)
	}
	if _, _, err := RemoveField(fieldStore, presetStore, "context-size", false); err == nil {
		t.Fatal("expected error")
	}
	c, err := fieldStore.Load()
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := c.Find("context-size"); !ok {
		t.Fatal("expected field unchanged")
	}
	doc, err := presetStore.Load()
	if err != nil {
		t.Fatal(err)
	}
	p, _ := doc.Find("qwen3-fast")
	if p.Values["context-size"] != float64(4096) {
		t.Fatalf("p=%+v", p.Values)
	}
}

func TestRemoveFieldForcesCascade(t *testing.T) {
	home := t.TempDir()
	if err := os.MkdirAll(filepath.Join(home, "config"), 0o755); err != nil {
		t.Fatal(err)
	}
	fieldStore := newFieldsStore(t, home)
	catalog := removalCatalog(t, fieldStore)
	presetStore := NewStore(home)
	if _, err := presetStore.Define(Preset{Name: "qwen3-fast"}); err != nil {
		t.Fatal(err)
	}
	if _, _, err := presetStore.SetValue(catalog, "qwen3-fast", "gpu", true); err != nil {
		t.Fatal(err)
	}
	if _, _, err := presetStore.SetValue(catalog, "qwen3-fast", "n-gpu-layers", float64(99)); err != nil {
		t.Fatal(err)
	}
	newCatalog, doc, err := RemoveField(fieldStore, presetStore, "gpu", true)
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := newCatalog.Find("gpu"); ok {
		t.Fatal("expected gpu removed")
	}
	if _, ok := newCatalog.Find("n-gpu-layers"); ok {
		t.Fatal("expected descendant removed")
	}
	p, _ := doc.Find("qwen3-fast")
	if len(p.Values) != 0 {
		t.Fatalf("expected values cleared, got %+v", p.Values)
	}
}

func TestRemoveFieldUnreferencedNoForceNeeded(t *testing.T) {
	home := t.TempDir()
	if err := os.MkdirAll(filepath.Join(home, "config"), 0o755); err != nil {
		t.Fatal(err)
	}
	fieldStore := newFieldsStore(t, home)
	catalog := removalCatalog(t, fieldStore)
	presetStore := NewStore(home)
	if _, err := presetStore.Define(Preset{Name: "qwen3-fast"}); err != nil {
		t.Fatal(err)
	}
	_ = catalog
	newCatalog, _, err := RemoveField(fieldStore, presetStore, "context-size", false)
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := newCatalog.Find("context-size"); ok {
		t.Fatal("expected field removed")
	}
}

func TestCascadeRemoveFieldValuesLeavesOtherFieldsIntact(t *testing.T) {
	doc := Document{SchemaVersion: SchemaVersion, Global: Preset{Name: GlobalName, Values: map[string]any{"a": 1, "b": 2}}}
	got := CascadeRemoveFieldValues(doc, []string{"a"})
	if got.Global.Values["a"] != nil {
		t.Fatalf("a=%v", got.Global.Values["a"])
	}
	if got.Global.Values["b"] != 2 {
		t.Fatalf("b=%v", got.Global.Values["b"])
	}
}

func TestRemoveFieldPublishFailureLeavesBothIntact(t *testing.T) {
	home := t.TempDir()
	if err := os.MkdirAll(filepath.Join(home, "config"), 0o755); err != nil {
		t.Fatal(err)
	}
	fieldStore := newFieldsStore(t, home)
	catalog := removalCatalog(t, fieldStore)
	presetStore := NewStore(home)
	if _, err := presetStore.Define(Preset{Name: "qwen3-fast"}); err != nil {
		t.Fatal(err)
	}
	if _, _, err := presetStore.SetValue(catalog, "qwen3-fast", "context-size", float64(4096)); err != nil {
		t.Fatal(err)
	}
	beforePresets, err := os.ReadFile(presetStore.path())
	if err != nil {
		t.Fatal(err)
	}
	fieldsPath := filepath.Join(home, "config", "llamacpp-fields.json")
	beforeFields, err := os.ReadFile(fieldsPath)
	if err != nil {
		t.Fatal(err)
	}
	presetStore.ops.rename = func(string, string) error { return errors.New("boom") }
	if _, _, err := RemoveField(fieldStore, presetStore, "context-size", true); err == nil {
		t.Fatal("expected error")
	}
	afterPresets, err := os.ReadFile(presetStore.path())
	if err != nil {
		t.Fatal(err)
	}
	afterFields, err := os.ReadFile(fieldsPath)
	if err != nil {
		t.Fatal(err)
	}
	if string(beforePresets) != string(afterPresets) {
		t.Fatal("expected preset document unchanged after publish failure")
	}
	if string(beforeFields) != string(afterFields) {
		t.Fatal("expected field catalog unchanged after publish failure")
	}
}
