package main

import (
	"bytes"
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/mstranich/coquena/internal/fields"
	modelapp "github.com/mstranich/coquena/internal/model"
	"github.com/mstranich/coquena/internal/presets"
)

// TestOperatorFlowDiscoverDownloadListBind exercises the loop the product
// exists for, end to end against a stubbed Hugging Face repository: discover a
// repository holding several quantizations and a projector, download one
// quantization addressed as user/repo:QUANT, see it listed with its declared
// type, and bind a preset to it by the same reference.
//
// It is one sequence rather than four tests on purpose. Each step consumes what
// the previous one produced — above all the logical reference, which has to
// mean the same artifact at discovery, at download, in the listing, and at
// binding — and that continuity is exactly what a per-step test cannot observe.
func TestOperatorFlowDiscoverDownloadListBind(t *testing.T) {
	m := withRepositoryManager(t, map[string][]byte{
		"bonsai-Q4_K_M.gguf": cliPrincipalGGUF(),
		"bonsai-Q6_K.gguf":   cliPrincipalGGUF(),
		"bonsai-Q8_0.gguf":   cliPrincipalGGUF(),
		"mmproj-BF16.gguf":   cliProjectorGGUF(),
	}, "---\nlicense: mit\n---\n# Bonsai\nProse the operator did not ask for.\n")

	// The preset side reads the same library the model commands write to, so it
	// is pointed at this manager's models directory rather than a fresh one.
	home := t.TempDir()
	if err := os.MkdirAll(filepath.Join(home, "config"), 0o755); err != nil {
		t.Fatal(err)
	}
	oldPreset := presetStoreFactory
	presetStoreFactory = func() (*presets.Store, error) { return presets.NewStore(home), nil }
	t.Cleanup(func() { presetStoreFactory = oldPreset })
	oldField := fieldStoreFactory
	fieldStoreFactory = func() (fieldStoreCLI, error) { return fields.NewStore(home), nil }
	t.Cleanup(func() { fieldStoreFactory = oldField })
	oldManager := presetModelManagerFactory
	presetModelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { presetModelManagerFactory = oldManager })

	// Step 1: discover. Every quantization and the projector are reported with
	// their declared type, and the selector marks one without hiding the rest.
	var discovered bytes.Buffer
	if err := runModel([]string{"discover", "org/repo:Q4_K_M"}, &discovered); err != nil {
		t.Fatalf("discover: %v", err)
	}
	for _, expected := range []string{"bonsai-Q4_K_M.gguf", "bonsai-Q6_K.gguf", "bonsai-Q8_0.gguf", "mmproj-BF16.gguf", "model", "mmproj"} {
		if !strings.Contains(discovered.String(), expected) {
			t.Fatalf("discover omitted %q: %s", expected, discovered.String())
		}
	}
	if strings.Contains(discovered.String(), "Prose the operator did not ask for.") {
		t.Fatalf("discover printed the model card: %s", discovered.String())
	}

	// Step 2: download exactly the quantization the operator read off that list,
	// by its logical reference rather than a filename or URL.
	var downloaded bytes.Buffer
	if err := runModel([]string{"download", "org/repo:Q4_K_M"}, &downloaded); err != nil {
		t.Fatalf("download org/repo:Q4_K_M: %v", err)
	}
	if len(strings.Fields(strings.TrimSpace(downloaded.String()))) != 1 {
		t.Fatalf("expected exactly one job, out=%q", downloaded.String())
	}

	// Step 3: the listing reports the downloaded artifact with its declared type.
	var listed bytes.Buffer
	if err := runModel([]string{"list", "--json"}, &listed); err != nil {
		t.Fatalf("list: %v", err)
	}
	var envelope struct {
		Result []modelListEntry `json:"result"`
	}
	if err := json.Unmarshal(listed.Bytes(), &envelope); err != nil {
		t.Fatal(err)
	}
	if len(envelope.Result) != 1 {
		t.Fatalf("expected the one downloaded artifact, result=%+v", envelope.Result)
	}
	entry := envelope.Result[0]
	if entry.Filename != "bonsai-Q4_K_M.gguf" || entry.State != "installed" || entry.Type != "model" {
		t.Fatalf("entry=%+v", entry)
	}

	// Step 4: bind a preset by the same reference the operator downloaded with.
	var defined bytes.Buffer
	if err := runPreset([]string{"define", "bonsai", "--model", "org/repo:Q4_K_M"}, &defined); err != nil {
		t.Fatalf("preset define org/repo:Q4_K_M: %v", err)
	}
	var shown bytes.Buffer
	if err := runPreset([]string{"show", "bonsai", "--json"}, &shown); err != nil {
		t.Fatalf("preset show: %v", err)
	}
	// The binding resolved rather than being stored as merely requested: that is
	// what tells the operator llama-server has a concrete artifact to run.
	if !strings.Contains(shown.String(), "bonsai-Q4_K_M.gguf") {
		t.Fatalf("preset did not resolve to the downloaded artifact: %s", shown.String())
	}
}
