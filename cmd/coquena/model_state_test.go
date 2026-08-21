package main

import (
	"bytes"
	"os"
	"path/filepath"
	"strings"
	"testing"

	modelapp "github.com/mstranich/coquena/internal/model"
)

// Both output modes name a state the same way. They are rendered from the same
// derived value, and a caller scripting against `--json` must be able to talk to
// an operator reading the table without translating between two vocabularies.
func TestModelListReportsTheSameStateVocabularyInBothOutputs(t *testing.T) {
	m := withModelManager(t)
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })

	// One managed artifact and one Coquena does not manage, so the listing
	// carries two different states rather than repeating one.
	partial := filepath.Join(t.TempDir(), "partial")
	if err := os.WriteFile(partial, cliPrincipalGGUF(), 0600); err != nil {
		t.Fatal(err)
	}
	record := modelapp.Record{Source: modelapp.Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: "https://example.test/m.gguf"}}
	if _, err := modelapp.Publish(m.ModelsDir, partial, record); err != nil {
		t.Fatal(err)
	}
	foreign := filepath.Join(m.ModelsDir, "other", "repo")
	if err := os.MkdirAll(foreign, 0755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(foreign, "f.gguf"), cliPrincipalGGUF(), 0600); err != nil {
		t.Fatal(err)
	}

	structured := listedEntries(t)
	var human bytes.Buffer
	if err := runModel([]string{"list"}, &human); err != nil {
		t.Fatal(err)
	}
	if len(structured) != 2 {
		t.Fatalf("entries=%+v", structured)
	}
	for filename, entry := range structured {
		if entry.State == "" {
			t.Fatalf("%s reported no state", filename)
		}
		if !strings.Contains(human.String(), entry.State) {
			t.Fatalf("human output does not carry the state %q reported for %s:\n%s", entry.State, filename, human.String())
		}
	}
	if structured["m.gguf"].State == structured["f.gguf"].State {
		t.Fatalf("a managed and an unmanaged artifact reported the same state: %q", structured["m.gguf"].State)
	}
	if structured["f.gguf"].State != string(modelapp.StateUntracked) {
		t.Fatalf("f.gguf state=%q want untracked", structured["f.gguf"].State)
	}
}

// --json follows the division the surface is built on: a command that reads
// state returns a representation of it, and a command that acts on state does
// not. Each command is the endpoint it would be were the REST API built.
//
// The refusal is a usage error rather than a silently ignored option, and the
// command must not have acted before refusing.
func TestActingCommandsRefuseMachineReadableOutput(t *testing.T) {
	m := withRepositoryManager(t, map[string][]byte{
		"principal-Q4_K_M.gguf": cliPrincipalGGUF(),
	}, "")
	writeCLIArtifact(t, m.ModelsDir, "org/repo/principal-Q4_K_M.gguf", cliPrincipalGGUF())

	for _, args := range [][]string{
		{"download", "org/repo:Q4_K_M", "--json"},
		{"import", "org/repo:Q4_K_M", "--json"},
		{"reconcile", "org/repo:Q4_K_M", "--json"},
		{"verify", "org/repo:Q4_K_M", "--json"},
	} {
		var out bytes.Buffer
		err := runModel(args, &out)
		if err == nil {
			t.Fatalf("%v accepted --json", args)
		}
		if !strings.Contains(err.Error(), "--json does not apply") {
			t.Fatalf("%v: the refusal does not name the option: %v", args, err)
		}
		if out.Len() != 0 {
			t.Fatalf("%v produced output before refusing: %q", args, out.String())
		}
	}

	// Nothing was created or adopted by the refused invocations.
	jobs, err := m.List()
	if err != nil {
		t.Fatal(err)
	}
	if len(jobs) != 0 {
		t.Fatalf("a refused download created jobs: %+v", jobs)
	}
	entries, err := m.Library()
	if err != nil {
		t.Fatal(err)
	}
	for _, entry := range entries {
		if entry.State != modelapp.StateUntracked {
			t.Fatalf("a refused invocation adopted %s into state %q", entry.Source.Filename, entry.State)
		}
	}
}

// The reading commands keep the option, and it still emits exactly one
// schema-versioned document.
func TestReadingCommandsStillAcceptMachineReadableOutput(t *testing.T) {
	withRepositoryManager(t, map[string][]byte{"principal-Q4_K_M.gguf": cliPrincipalGGUF()}, "")
	for _, args := range [][]string{{"list", "--json"}, {"discover", "org/repo", "--json"}} {
		var out bytes.Buffer
		if err := runModel(args, &out); err != nil {
			t.Fatalf("%v: %v", args, err)
		}
		if !strings.Contains(out.String(), `"schemaVersion":1`) {
			t.Fatalf("%v produced no schema-versioned document: %q", args, out.String())
		}
		if strings.Count(strings.TrimSpace(out.String()), "\n") != 0 {
			t.Fatalf("%v emitted more than one document: %q", args, out.String())
		}
	}
}

// import and reconcile are two verbs with one responsibility each, and neither
// falls back to the other. A reconciliation that establishes no match leaves the
// artifact detached — a state the operator can see and act on — rather than
// adopting it as local, which is a decision only a person makes.
func TestReconcileNeverAdoptsWhatItCouldNotMatch(t *testing.T) {
	// The fixture publishes no content hash, so no attestation can be
	// established for the artifact on disk.
	m := withRepositoryManager(t, map[string][]byte{"principal-Q4_K_M.gguf": cliPrincipalGGUF()}, "")
	writeCLIArtifact(t, m.ModelsDir, "org/repo/principal-Q4_K_M.gguf", cliPrincipalGGUF())

	if err := runModel([]string{"reconcile", "org/repo:Q4_K_M"}, &bytes.Buffer{}); err != nil {
		t.Fatal(err)
	}
	entries := listedEntries(t)
	entry := entries["principal-Q4_K_M.gguf"]
	if entry.State != string(modelapp.StateDetached) {
		t.Fatalf("state=%q want detached; reconcile adopted what it could not match", entry.State)
	}
	if !entry.Usable {
		t.Fatalf("a detached artifact is still usable: %+v", entry)
	}
}

// Neither command accepts a mode flag, because the mode is the command.
func TestImportAndReconcileRefuseModeFlags(t *testing.T) {
	withRepositoryManager(t, map[string][]byte{"principal-Q4_K_M.gguf": cliPrincipalGGUF()}, "")
	for _, args := range [][]string{
		{"import", "org/repo:Q4_K_M", "--online"},
		{"import", "org/repo:Q4_K_M", "--accept"},
		{"reconcile", "org/repo:Q4_K_M", "--online"},
		{"reconcile", "org/repo:Q4_K_M", "--accept"},
		{"import", "--all"},
		{"reconcile", "--all"},
	} {
		if err := runModel(args, &bytes.Buffer{}); err == nil {
			t.Fatalf("%v was accepted", args)
		}
	}
}
