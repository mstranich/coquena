package main

import (
	"bytes"
	"errors"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
	"testing"

	"github.com/mstranich/coquena/internal/fields"
	"github.com/mstranich/coquena/internal/presets"
)

func withFieldStore(t *testing.T, store fieldStoreCLI, err error) {
	t.Helper()
	oldFactory := fieldStoreFactory
	fieldStoreFactory = func() (fieldStoreCLI, error) { return store, err }
	t.Cleanup(func() { fieldStoreFactory = oldFactory })
}

func withFieldConfirm(t *testing.T, answer bool) {
	t.Helper()
	old := fieldConfirmOverwrite
	fieldConfirmOverwrite = func(string) bool { return answer }
	t.Cleanup(func() { fieldConfirmOverwrite = old })
}

func TestFieldDefine(t *testing.T) {
	store := &fakeFieldStoreCLI{}
	withFieldStore(t, store, nil)
	var out bytes.Buffer
	err := runField([]string{"define", "integer", "context-size", "--default", "4096", "--provider", "official", "--introduced-in", "b10107"}, &out)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if !strings.Contains(out.String(), "context-size") {
		t.Fatalf("output = %q", out.String())
	}
	f, ok := store.catalog.Find("context-size")
	if !ok || f.Default != 4096 {
		t.Fatalf("expected persisted field with default 4096, got %+v", f)
	}
}

func TestFieldDefineListsAfterward(t *testing.T) {
	store := &fakeFieldStoreCLI{}
	withFieldStore(t, store, nil)
	var out bytes.Buffer
	if err := runField([]string{"define", "integer", "context-size"}, &out); err != nil {
		t.Fatal(err)
	}
	out.Reset()
	if err := runField([]string{"list"}, &out); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(out.String(), "context-size") {
		t.Fatalf("list output = %q", out.String())
	}
}

func TestFieldDefineUpsertWithOverwrite(t *testing.T) {
	store := &fakeFieldStoreCLI{catalog: fields.Catalog{Fields: []fields.Field{{Name: "context-size", Kind: fields.KindInteger, Default: 4096}}}}
	withFieldStore(t, store, nil)
	var out bytes.Buffer
	if err := runField([]string{"define", "integer", "context-size", "--default", "8192", "--overwrite"}, &out); err != nil {
		t.Fatal(err)
	}
	f, _ := store.catalog.Find("context-size")
	if f.Default != 8192 {
		t.Fatalf("expected overwritten default 8192, got %v", f.Default)
	}
}

func TestFieldDefineReplayIsIdempotent(t *testing.T) {
	store := &fakeFieldStoreCLI{}
	withFieldStore(t, store, nil)
	run := func() {
		var out bytes.Buffer
		if err := runField([]string{"define", "integer", "context-size", "--default", "4096", "--overwrite"}, &out); err != nil {
			t.Fatal(err)
		}
	}
	run()
	run()
	if len(store.catalog.Fields) != 1 {
		t.Fatalf("expected replay to be idempotent, got %d fields", len(store.catalog.Fields))
	}
}

func TestFieldDefineRefusesNonInteractiveOverwrite(t *testing.T) {
	store := &fakeFieldStoreCLI{catalog: fields.Catalog{Fields: []fields.Field{{Name: "context-size", Kind: fields.KindInteger}}}}
	withFieldStore(t, store, nil)
	withFieldConfirm(t, false)
	var out bytes.Buffer
	err := runField([]string{"define", "integer", "context-size", "--default", "8192"}, &out)
	if err == nil {
		t.Fatal("expected error for refused overwrite")
	}
}

func TestFieldDefineInteractiveConfirmOverwrites(t *testing.T) {
	store := &fakeFieldStoreCLI{catalog: fields.Catalog{Fields: []fields.Field{{Name: "context-size", Kind: fields.KindInteger}}}}
	withFieldStore(t, store, nil)
	withFieldConfirm(t, true)
	var out bytes.Buffer
	err := runField([]string{"define", "integer", "context-size", "--default", "8192"}, &out)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	f, _ := store.catalog.Find("context-size")
	if f.Default != 8192 {
		t.Fatalf("expected confirmed overwrite to apply, got %v", f.Default)
	}
}

func TestFieldListShowsDefaultEnumOptionsAndParent(t *testing.T) {
	store := &fakeFieldStoreCLI{catalog: fields.Catalog{Fields: []fields.Field{
		{Name: "ctx-size", Kind: fields.KindInteger, Default: 4096},
		{Name: "sampler", Kind: fields.KindEnum, EnumOptions: []string{"top-k", "top-p"}},
		{Name: "temp", Kind: fields.KindString},
		{Name: "spec-type", Kind: fields.KindEnum, EnumOptions: []string{"none", "draft-simple"}},
		{Name: "spec-ngram-mod-n-min", Kind: fields.KindInteger, Parent: "spec-type", ParentValue: "ngram-mod"},
	}}}
	withFieldStore(t, store, nil)
	var out bytes.Buffer
	if err := runField([]string{"list"}, &out); err != nil {
		t.Fatal(err)
	}
	got := out.String()
	if !strings.Contains(got, "4096") {
		t.Errorf("expected default value 4096 in output, got %q", got)
	}
	if !strings.Contains(got, "top-k,top-p") {
		t.Errorf("expected enum options in output, got %q", got)
	}
	if !strings.Contains(got, "temp") {
		t.Errorf("expected field without a default to still be listed, got %q", got)
	}
	if !strings.Contains(got, "spec-type=ngram-mod") {
		t.Errorf("expected parent with required value in output, got %q", got)
	}
}

func TestFieldDefineOptionsCommaSeparated(t *testing.T) {
	store := &fakeFieldStoreCLI{}
	withFieldStore(t, store, nil)
	var out bytes.Buffer
	err := runField([]string{"define", "enum", "spec-type", "--options", "none,draft-simple,draft-eagle3,draft-mtp"}, &out)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	f, ok := store.catalog.Find("spec-type")
	if !ok {
		t.Fatal("expected field to be defined")
	}
	want := []string{"none", "draft-simple", "draft-eagle3", "draft-mtp"}
	if len(f.EnumOptions) != len(want) {
		t.Fatalf("expected %d options, got %v", len(want), f.EnumOptions)
	}
	for i, option := range want {
		if f.EnumOptions[i] != option {
			t.Fatalf("option %d = %q, want %q", i, f.EnumOptions[i], option)
		}
	}
}

func TestFieldDefineOptionsAcceptsSingleValueWithoutComma(t *testing.T) {
	store := &fakeFieldStoreCLI{}
	withFieldStore(t, store, nil)
	var out bytes.Buffer
	err := runField([]string{"define", "enum", "cache-type", "--options", "q8_0"}, &out)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	f, _ := store.catalog.Find("cache-type")
	if len(f.EnumOptions) != 1 || f.EnumOptions[0] != "q8_0" {
		t.Fatalf("expected a single option, got %v", f.EnumOptions)
	}
}

func TestFieldRemoveDeletesSubtree(t *testing.T) {
	store := &fakeFieldStoreCLI{catalog: fields.Catalog{Fields: []fields.Field{
		{Name: "parent", Kind: fields.KindVoid},
		{Name: "child", Kind: fields.KindString, Parent: "parent"},
	}}}
	withFieldStore(t, store, nil)
	var out bytes.Buffer
	if err := runField([]string{"remove", "parent"}, &out); err != nil {
		t.Fatal(err)
	}
	if len(store.catalog.Fields) != 0 {
		t.Fatalf("expected subtree removed, got %+v", store.catalog.Fields)
	}
}

func withPresetStore(t *testing.T, home string) {
	t.Helper()
	old := presetStoreFactory
	presetStoreFactory = func() (*presets.Store, error) { return presets.NewStore(home), nil }
	t.Cleanup(func() { presetStoreFactory = old })
}

func TestFieldRemoveRefusesReferencedFieldWithoutForce(t *testing.T) {
	home := t.TempDir()
	if err := os.MkdirAll(filepath.Join(home, "config"), 0o755); err != nil {
		t.Fatal(err)
	}
	withPresetStore(t, home)
	store := &fakeFieldStoreCLI{catalog: fields.Catalog{Fields: []fields.Field{{Name: "context-size", Kind: fields.KindInteger}}}}
	withFieldStore(t, store, nil)
	presetStore := presets.NewStore(home)
	if _, err := presetStore.Define(presets.Preset{Name: "qwen3-fast"}); err != nil {
		t.Fatal(err)
	}
	if _, _, err := presetStore.SetValue(store.catalog, "qwen3-fast", "context-size", float64(4096)); err != nil {
		t.Fatal(err)
	}
	var out bytes.Buffer
	if err := runField([]string{"remove", "context-size"}, &out); err == nil {
		t.Fatal("expected error")
	}
	if len(store.catalog.Fields) != 1 {
		t.Fatalf("expected field unchanged, got %+v", store.catalog.Fields)
	}
}

func TestFieldRemoveForcesCascade(t *testing.T) {
	home := t.TempDir()
	if err := os.MkdirAll(filepath.Join(home, "config"), 0o755); err != nil {
		t.Fatal(err)
	}
	withPresetStore(t, home)
	store := &fakeFieldStoreCLI{catalog: fields.Catalog{Fields: []fields.Field{{Name: "context-size", Kind: fields.KindInteger}}}}
	withFieldStore(t, store, nil)
	presetStore := presets.NewStore(home)
	if _, err := presetStore.Define(presets.Preset{Name: "qwen3-fast"}); err != nil {
		t.Fatal(err)
	}
	if _, _, err := presetStore.SetValue(store.catalog, "qwen3-fast", "context-size", float64(4096)); err != nil {
		t.Fatal(err)
	}
	var out bytes.Buffer
	if err := runField([]string{"remove", "context-size", "--force"}, &out); err != nil {
		t.Fatal(err)
	}
	if len(store.catalog.Fields) != 0 {
		t.Fatalf("expected field removed, got %+v", store.catalog.Fields)
	}
	doc, err := presetStore.Load()
	if err != nil {
		t.Fatal(err)
	}
	p, _ := doc.Find("qwen3-fast")
	if len(p.Values) != 0 {
		t.Fatalf("expected values cleared, got %+v", p.Values)
	}
}

func TestFieldShowByName(t *testing.T) {
	store := &fakeFieldStoreCLI{catalog: fields.Catalog{Fields: []fields.Field{{Name: "context-size", Kind: fields.KindInteger}}}}
	withFieldStore(t, store, nil)
	var out bytes.Buffer
	if err := runField([]string{"show", "context-size"}, &out); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(out.String(), "context-size") {
		t.Fatalf("output = %q", out.String())
	}
}

func TestFieldShowByAlias(t *testing.T) {
	store := &fakeFieldStoreCLI{catalog: fields.Catalog{Fields: []fields.Field{{Name: "context-size", Kind: fields.KindInteger, ShortArgument: "c", EnvVar: "LLAMA_ARG_CTX_SIZE"}}}}
	withFieldStore(t, store, nil)
	var out bytes.Buffer
	if err := runField([]string{"show", "c"}, &out); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(out.String(), "context-size") {
		t.Fatalf("expected canonical name in output, got %q", out.String())
	}
	out.Reset()
	if err := runField([]string{"show", "LLAMA_ARG_CTX_SIZE"}, &out); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(out.String(), "context-size") {
		t.Fatalf("expected canonical name in output, got %q", out.String())
	}
}

func TestFieldShowUnknown(t *testing.T) {
	store := &fakeFieldStoreCLI{}
	withFieldStore(t, store, nil)
	var out bytes.Buffer
	if err := runField([]string{"show", "missing"}, &out); err == nil {
		t.Fatal("expected error for unknown field")
	}
}

func TestFieldDefineRejectsInvalidClasses(t *testing.T) {
	cases := [][]string{
		{"define", "number", "context-size"},
		{"define", "integer", "--bad"},
		{"define", "enum", "sampler"},
	}
	for _, args := range cases {
		store := &fakeFieldStoreCLI{}
		withFieldStore(t, store, nil)
		var out bytes.Buffer
		if err := runField(args, &out); err == nil {
			t.Errorf("args %v: expected error", args)
		}
	}
}

func TestFieldCommandsBeforeInit(t *testing.T) {
	withFieldStore(t, nil, errors.New("portable installation is not initialized; run coquena init"))
	var out bytes.Buffer
	if err := runField([]string{"list"}, &out); err == nil {
		t.Fatal("expected error before initialization")
	}
}

func TestFieldUsage(t *testing.T) {
	var out bytes.Buffer
	if err := runField(nil, &out); err == nil {
		t.Fatal("expected usage error")
	}
}

func TestFieldExecutableDefinePersistsAcrossInvocations(t *testing.T) {
	if testing.Short() {
		t.Skip("skipping executable integration test in short mode")
	}

	installationRoot := t.TempDir()
	binaryName := "coquena"
	if runtime.GOOS == "windows" {
		binaryName += ".exe"
	}
	binaryPath := filepath.Join(installationRoot, binaryName)

	build := exec.Command("go", "build", "-o", binaryPath, ".")
	if output, err := build.CombinedOutput(); err != nil {
		t.Fatalf("build Coquena: %v\n%s", err, output)
	}

	run := func(args ...string) (string, error) {
		cmd := exec.Command(binaryPath, args...)
		cmd.Dir = installationRoot
		output, err := cmd.CombinedOutput()
		return string(output), err
	}

	if _, err := run("init"); err != nil {
		t.Fatalf("coquena init failed: %v", err)
	}
	if _, err := os.Stat(filepath.Join(installationRoot, "config")); err != nil {
		t.Fatalf("expected initialized installation: %v", err)
	}
	if output, err := run("field", "define", "integer", "context-size", "--default", "4096"); err != nil {
		t.Fatalf("coquena field define failed: %v\n%s", err, output)
	}
	output, err := run("field", "show", "context-size")
	if err != nil {
		t.Fatalf("coquena field show failed: %v\n%s", err, output)
	}
	if !strings.Contains(output, "context-size") {
		t.Fatalf("field show output = %q, want it to contain the field name", output)
	}
	if output, err := run("field", "define", "integer", "context-size"); err == nil {
		t.Fatalf("expected non-interactive redefinition without --overwrite to fail, got output %q", output)
	}
	if output, err := run("field", "define", "integer", "context-size", "--default", "8192", "--overwrite"); err != nil {
		t.Fatalf("coquena field define --overwrite failed: %v\n%s", err, output)
	}
	if output, err := run("field", "remove", "context-size"); err != nil {
		t.Fatalf("coquena field remove failed: %v\n%s", err, output)
	}
	if output, err := run("field", "show", "context-size"); err == nil {
		t.Fatalf("expected removed field to be absent, got output %q", output)
	}
}

func TestSeedScriptReplayIsIdempotent(t *testing.T) {
	if testing.Short() {
		t.Skip("skipping executable integration test in short mode")
	}
	shPath, err := exec.LookPath("sh")
	if err != nil {
		t.Skip("sh is not available on PATH")
	}

	repoRoot, err := filepath.Abs(filepath.Join("..", ".."))
	if err != nil {
		t.Fatal(err)
	}
	scriptPath := filepath.Join(repoRoot, "scripts", "seed-llamacpp-fields.sh")
	if _, err := os.Stat(scriptPath); err != nil {
		t.Fatalf("seed script not found: %v", err)
	}

	installationRoot := t.TempDir()
	binaryName := "coquena"
	if runtime.GOOS == "windows" {
		binaryName += ".exe"
	}
	binaryPath := filepath.Join(installationRoot, binaryName)

	build := exec.Command("go", "build", "-o", binaryPath, ".")
	if output, err := build.CombinedOutput(); err != nil {
		t.Fatalf("build Coquena: %v\n%s", err, output)
	}

	initCmd := exec.Command(binaryPath, "init")
	initCmd.Dir = installationRoot
	if output, err := initCmd.CombinedOutput(); err != nil {
		t.Fatalf("coquena init failed: %v\n%s", err, output)
	}

	runSeed := func() {
		cmd := exec.Command(shPath, scriptPath)
		cmd.Dir = installationRoot
		cmd.Env = append(os.Environ(), "COQUENA_BIN="+binaryPath)
		if output, err := cmd.CombinedOutput(); err != nil {
			t.Fatalf("seed script failed: %v\n%s", err, output)
		}
	}

	catalogPath := filepath.Join(installationRoot, "config", "llamacpp-fields.json")
	runSeed()
	first, err := os.ReadFile(catalogPath)
	if err != nil {
		t.Fatalf("read catalog after first seed: %v", err)
	}
	runSeed()
	second, err := os.ReadFile(catalogPath)
	if err != nil {
		t.Fatalf("read catalog after second seed: %v", err)
	}
	if string(first) != string(second) {
		t.Fatalf("expected replaying the seed script to be idempotent;\nfirst:\n%s\nsecond:\n%s", first, second)
	}
}

type fakeFieldStoreCLI struct {
	catalog fields.Catalog
}

func (f *fakeFieldStoreCLI) Load() (fields.Catalog, error) { return f.catalog, nil }
func (f *fakeFieldStoreCLI) Define(field fields.Field) (fields.Catalog, error) {
	if _, ok := f.catalog.Find(field.Name); ok {
		return fields.Catalog{}, errors.New("field already exists")
	}
	if err := field.Validate(); err != nil {
		return fields.Catalog{}, err
	}
	f.catalog.Fields = append(f.catalog.Fields, field)
	return f.catalog, nil
}
func (f *fakeFieldStoreCLI) Upsert(field fields.Field) (fields.Catalog, error) {
	if err := field.Validate(); err != nil {
		return fields.Catalog{}, err
	}
	for i, existing := range f.catalog.Fields {
		if existing.Name == field.Name {
			f.catalog.Fields[i] = field
			return f.catalog, nil
		}
	}
	f.catalog.Fields = append(f.catalog.Fields, field)
	return f.catalog, nil
}
func (f *fakeFieldStoreCLI) Remove(name string) (fields.Catalog, error) {
	removed := map[string]bool{}
	var collect func(string)
	collect = func(n string) {
		removed[n] = true
		for _, existing := range f.catalog.Fields {
			if existing.Parent == n && !removed[existing.Name] {
				collect(existing.Name)
			}
		}
	}
	if _, ok := f.catalog.Find(name); !ok {
		return fields.Catalog{}, errors.New("field not found")
	}
	collect(name)
	remaining := make([]fields.Field, 0, len(f.catalog.Fields))
	for _, existing := range f.catalog.Fields {
		if !removed[existing.Name] {
			remaining = append(remaining, existing)
		}
	}
	f.catalog.Fields = remaining
	return f.catalog, nil
}
