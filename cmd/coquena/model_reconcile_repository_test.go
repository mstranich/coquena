package main

import (
	"bytes"
	"os"
	"path/filepath"
	"strings"
	"testing"

	modelapp "github.com/mstranich/coquena/internal/model"
)

// writeCLIArtifact places body beneath the manager's models directory, the way
// another application would have left a foreign .gguf there.
func writeCLIArtifact(t *testing.T, modelsDir, rel string, body []byte) string {
	t.Helper()
	path := filepath.Join(modelsDir, filepath.FromSlash(rel))
	if err := os.MkdirAll(filepath.Dir(path), 0755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path, body, 0600); err != nil {
		t.Fatal(err)
	}
	return path
}

// Reconciling a reference naming a repository reconciles the principal and its
// projector in one action, and reports the candidate the repository publishes
// but the library does not hold.
func TestRunModelReconcileReconcilesTheWholeRepository(t *testing.T) {
	files := map[string][]byte{
		"principal-Q4_K_M.gguf": cliPrincipalGGUF(),
		"mmproj-BF16.gguf":      cliProjectorGGUF(),
		"principal-Q8_0.gguf":   cliPrincipalGGUF(),
	}
	m := withRepositoryManager(t, files, "")
	writeCLIArtifact(t, m.ModelsDir, "org/repo/principal-Q4_K_M.gguf", files["principal-Q4_K_M.gguf"])
	writeCLIArtifact(t, m.ModelsDir, "org/repo/mmproj-BF16.gguf", files["mmproj-BF16.gguf"])

	var out bytes.Buffer
	if err := runModel([]string{"reconcile", "org/repo:Q4_K_M"}, &out); err != nil {
		t.Fatal(err)
	}
	rendered := out.String()
	for _, expected := range []string{"org/repo", "principal-Q4_K_M.gguf", "mmproj-BF16.gguf", "reconciled"} {
		if !strings.Contains(rendered, expected) {
			t.Fatalf("missing %q in %s", expected, rendered)
		}
	}
	// The quantization the operator did not download is an alternative it
	// declined, not something missing, so it is not reported as absent.
	if strings.Contains(rendered, "principal-Q8_0.gguf") {
		t.Fatalf("an unselected alternative quantization was reported: %s", rendered)
	}
	entries, err := m.Library()
	if err != nil {
		t.Fatal(err)
	}
	for _, entry := range entries {
		// The fixture publishes no content hash, so no attestation can be
		// established and the artifacts land detached rather than synced. What
		// matters here is that reconciliation adopted them at all: none is left
		// outside Coquena's management.
		if entry.State == modelapp.StateUntracked {
			t.Fatalf("entry %s left in state %q after a repository reconciliation", entry.Source.Filename, entry.State)
		}
	}
}

// A reference resolving to two principals on disk is refused before anything is
// written: choosing a quantization is the operator's decision.
func TestRunModelReconcileRefusesAmbiguousPrincipals(t *testing.T) {
	files := map[string][]byte{
		"principal-Q4_K_M.gguf": cliPrincipalGGUF(),
		"principal-Q8_0.gguf":   cliPrincipalGGUF(),
	}
	m := withRepositoryManager(t, files, "")
	writeCLIArtifact(t, m.ModelsDir, "org/repo/principal-Q4_K_M.gguf", files["principal-Q4_K_M.gguf"])
	writeCLIArtifact(t, m.ModelsDir, "org/repo/principal-Q8_0.gguf", files["principal-Q8_0.gguf"])

	var out bytes.Buffer
	err := runModel([]string{"reconcile", "org/repo"}, &out)
	if err == nil {
		t.Fatal("expected an ambiguity refusal")
	}
	for _, expected := range []string{"principal-Q4_K_M.gguf", "principal-Q8_0.gguf"} {
		if !strings.Contains(err.Error(), expected) {
			t.Fatalf("diagnostic does not name %s: %s", expected, err.Error())
		}
	}
}
