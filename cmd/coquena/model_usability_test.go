package main

import (
	"bytes"
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"

	modelapp "github.com/mstranich/coquena/internal/model"
)

// listedEntries runs the JSON listing and indexes it by filename.
func listedEntries(t *testing.T) map[string]modelListEntry {
	t.Helper()
	var out bytes.Buffer
	if err := runModel([]string{"list", "--json"}, &out); err != nil {
		t.Fatal(err)
	}
	var envelope struct {
		Result map[string][]modelListEntry `json:"result"`
	}
	if err := json.Unmarshal(out.Bytes(), &envelope); err != nil {
		t.Fatal(err)
	}
	byName := map[string]modelListEntry{}
	for _, entries := range envelope.Result {
		for _, entry := range entries {
			byName[entry.Filename] = entry
		}
	}
	return byName
}

// The listing reports usability in both modes. A companion whose principal is
// absent is eligible like any other artifact; what makes an artifact ineligible
// is having no valid sidecar.
func TestRunModelListReportsUsabilityAndItsReason(t *testing.T) {
	m := withModelManager(t)
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	publishArtifact(t, m, "principal.gguf", cliPrincipalGGUF())
	// Its projection dimension matches no installed principal's embedding
	// length. It stays eligible: which principal a companion accompanies is
	// decided by the preset that binds them, not by the listing.
	publishArtifact(t, m, "orphan.gguf", cliGGUF("general.type", "mmproj", "general.architecture", "clip", "clip.vision.projection_dim", uint32(3840)))
	// A foreign file with no sidecar, so the third outcome is present too.
	foreign := filepath.Join(m.ModelsDir, "other", "repo", "foreign.gguf")
	if err := os.MkdirAll(filepath.Dir(foreign), 0755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(foreign, cliProjectorGGUF(), 0600); err != nil {
		t.Fatal(err)
	}

	entries := listedEntries(t)
	if principal := entries["principal.gguf"]; !principal.Usable {
		t.Fatalf("principal=%+v", principal)
	}
	companion := entries["orphan.gguf"]
	if !companion.Usable {
		t.Fatalf("a companion with no compatible principal is still eligible: %+v", companion)
	}
	unreconciled := entries["foreign.gguf"]
	if unreconciled.Usable {
		t.Fatalf("unreconciled=%+v", unreconciled)
	}

	var human bytes.Buffer
	if err := runModel([]string{"list"}, &human); err != nil {
		t.Fatal(err)
	}
	for _, expected := range []string{"USABLE", "yes", "no"} {
		if !strings.Contains(human.String(), expected) {
			t.Fatalf("missing %q in:\n%s", expected, human.String())
		}
	}
}

// A companion is usable from the moment it is installed and stays usable when
// its principal arrives, with nothing rewritten in between.
func TestRunModelListReflectsANewPrincipalWithoutRewritingSidecars(t *testing.T) {
	m := withModelManager(t)
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	publishArtifact(t, m, "mmproj.gguf", cliProjectorGGUF())
	if entry := listedEntries(t)["mmproj.gguf"]; !entry.Usable {
		t.Fatalf("a companion is ineligible before its principal arrives: %+v", entry)
	}
	sidecar := filepath.Join(m.ModelsDir, "org", "repo", "mmproj.json")
	before, err := os.ReadFile(sidecar)
	if err != nil {
		t.Fatal(err)
	}

	publishArtifact(t, m, "principal.gguf", cliPrincipalGGUF())

	entry := listedEntries(t)["mmproj.gguf"]
	if !entry.Usable {
		t.Fatalf("mmproj=%+v", entry)
	}
	after, err := os.ReadFile(sidecar)
	if err != nil {
		t.Fatal(err)
	}
	if string(before) != string(after) {
		t.Fatal("the projector's sidecar was rewritten to record its usability")
	}
}
