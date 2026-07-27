package main

import (
	"bytes"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/mstranich/coquena/internal/fields"
	modelapp "github.com/mstranich/coquena/internal/model"
	"github.com/mstranich/coquena/internal/presets"
)

func withPresetCLI(t *testing.T) (home, modelsDir string) {
	t.Helper()
	home = t.TempDir()
	if err := os.MkdirAll(filepath.Join(home, "config"), 0o755); err != nil {
		t.Fatal(err)
	}
	modelsDir = filepath.Join(home, "models")
	if err := os.MkdirAll(modelsDir, 0o755); err != nil {
		t.Fatal(err)
	}
	oldPreset := presetStoreFactory
	presetStoreFactory = func() (*presets.Store, error) { return presets.NewStore(home), nil }
	t.Cleanup(func() { presetStoreFactory = oldPreset })

	oldField := fieldStoreFactory
	fieldStoreFactory = func() (fieldStoreCLI, error) { return fields.NewStore(home), nil }
	t.Cleanup(func() { fieldStoreFactory = oldField })

	oldManager := presetModelManagerFactory
	presetModelManagerFactory = func() (*modelapp.Manager, error) { return modelapp.NewManager(modelsDir), nil }
	t.Cleanup(func() { presetModelManagerFactory = oldManager })

	oldModelsDir := presetModelsDirFunc
	presetModelsDirFunc = func() (string, error) { return modelsDir, nil }
	t.Cleanup(func() { presetModelsDirFunc = oldModelsDir })

	oldHome := presetHomeFunc
	presetHomeFunc = func() (string, error) { return home, nil }
	t.Cleanup(func() { presetHomeFunc = oldHome })
	return home, modelsDir
}

func TestPresetDefineWithAllowMissing(t *testing.T) {
	withPresetCLI(t)
	var out bytes.Buffer
	if err := runPreset([]string{"define", "qwen3-fast", "--model", "org/repo:Q4_K_M", "--allow-missing", "--title", "Qwen3 Fast"}, &out); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(out.String(), "defined qwen3-fast") {
		t.Fatalf("out=%q", out.String())
	}
}

func TestPresetDefineRefusesDuplicate(t *testing.T) {
	withPresetCLI(t)
	var out bytes.Buffer
	if err := runPreset([]string{"define", "qwen3-fast", "--model", "org/repo:Q4_K_M", "--allow-missing"}, &out); err != nil {
		t.Fatal(err)
	}
	if err := runPreset([]string{"define", "qwen3-fast", "--model", "org/repo:Q4_K_M", "--allow-missing"}, &out); err == nil {
		t.Fatal("expected error")
	}
}

func TestPresetSetAndShowReportsEffectiveValue(t *testing.T) {
	withPresetCLI(t)
	fieldStore, err := fieldStoreFactory()
	if err != nil {
		t.Fatal(err)
	}
	if _, err := fieldStore.Define(fields.Field{Name: "context-size", Kind: fields.KindInteger}); err != nil {
		t.Fatal(err)
	}
	var out bytes.Buffer
	if err := runPreset([]string{"define", "qwen3-fast", "--model", "org/repo:Q4_K_M", "--allow-missing"}, &out); err != nil {
		t.Fatal(err)
	}
	out.Reset()
	if err := runPreset([]string{"set", "qwen3-fast", "context-size", "8192"}, &out); err != nil {
		t.Fatal(err)
	}
	out.Reset()
	if err := runPreset([]string{"show", "qwen3-fast"}, &out); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(out.String(), "context-size") || !strings.Contains(out.String(), "8192") || !strings.Contains(out.String(), "own") {
		t.Fatalf("out=%q", out.String())
	}
}

func TestPresetSetGlobalThenShowInherited(t *testing.T) {
	withPresetCLI(t)
	fieldStore, err := fieldStoreFactory()
	if err != nil {
		t.Fatal(err)
	}
	if _, err := fieldStore.Define(fields.Field{Name: "temp", Kind: fields.KindString}); err != nil {
		t.Fatal(err)
	}
	var out bytes.Buffer
	if err := runPreset([]string{"define", "qwen3-fast", "--model", "org/repo:Q4_K_M", "--allow-missing"}, &out); err != nil {
		t.Fatal(err)
	}
	out.Reset()
	if err := runPreset([]string{"set", presets.GlobalName, "temp", "0.7"}, &out); err != nil {
		t.Fatal(err)
	}
	out.Reset()
	if err := runPreset([]string{"show", "qwen3-fast", "--json"}, &out); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(out.String(), `"source":"global"`) {
		t.Fatalf("out=%q", out.String())
	}
}

func TestPresetRenameAndRemove(t *testing.T) {
	withPresetCLI(t)
	var out bytes.Buffer
	if err := runPreset([]string{"define", "qwen3-fst", "--model", "org/repo:Q4_K_M", "--allow-missing"}, &out); err != nil {
		t.Fatal(err)
	}
	out.Reset()
	if err := runPreset([]string{"rename", "qwen3-fst", "qwen3-fast"}, &out); err != nil {
		t.Fatal(err)
	}
	out.Reset()
	if err := runPreset([]string{"show", "qwen3-fast"}, &out); err != nil {
		t.Fatal(err)
	}
	out.Reset()
	if err := runPreset([]string{"remove", "qwen3-fast"}, &out); err != nil {
		t.Fatal(err)
	}
	if err := runPreset([]string{"show", "qwen3-fast"}, &out); err == nil {
		t.Fatal("expected error after removal")
	}
}

func TestPresetBindRuntimeAndUnbind(t *testing.T) {
	withPresetCLI(t)
	var out bytes.Buffer
	if err := runPreset([]string{"define", "qwen3-fast", "--model", "org/repo:Q4_K_M", "--allow-missing"}, &out); err != nil {
		t.Fatal(err)
	}
	out.Reset()
	if err := runPreset([]string{"bind", "runtime", "qwen3-fast", "--backend", "cuda@12.4", "--min-release", "b6200"}, &out); err != nil {
		t.Fatal(err)
	}
	out.Reset()
	if err := runPreset([]string{"show", "qwen3-fast", "--json"}, &out); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(out.String(), "cuda@12.4") {
		t.Fatalf("out=%q", out.String())
	}
	out.Reset()
	if err := runPreset([]string{"unbind", "runtime", "qwen3-fast"}, &out); err != nil {
		t.Fatal(err)
	}
}

func TestPresetRenderPrintsWithoutWritingByDefault(t *testing.T) {
	home, _ := withPresetCLI(t)
	var out bytes.Buffer
	if err := runPreset([]string{"define", "qwen3-fast", "--model", "org/repo:Q4_K_M", "--allow-missing"}, &out); err != nil {
		t.Fatal(err)
	}
	out.Reset()
	if err := runPreset([]string{"render", "--runtime", "b6234"}, &out); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(out.String(), "[*]") {
		t.Fatalf("out=%q", out.String())
	}
	if _, err := os.Stat(filepath.Join(home, "data")); !os.IsNotExist(err) {
		t.Fatal("expected render to not write any file")
	}
}

func TestPresetRenderWritesWithFlag(t *testing.T) {
	home, _ := withPresetCLI(t)
	var out bytes.Buffer
	if err := runPreset([]string{"define", "qwen3-fast", "--model", "org/repo:Q4_K_M", "--allow-missing"}, &out); err != nil {
		t.Fatal(err)
	}
	out.Reset()
	if err := runPreset([]string{"render", "--runtime", "b6234", "--write"}, &out); err != nil {
		t.Fatal(err)
	}
	expected := presets.DefaultRenderPath(home, "b6234")
	if !strings.Contains(out.String(), expected) {
		t.Fatalf("out=%q expected path %q", out.String(), expected)
	}
}
