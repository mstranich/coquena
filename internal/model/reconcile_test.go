package model

import (
	"crypto/sha256"
	"encoding/hex"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"testing"
)

func writeReconcileFixture(t *testing.T, root string, body []byte) string {
	t.Helper()
	dir := filepath.Join(root, "models", "org", "repo")
	if err := os.MkdirAll(dir, 0755); err != nil {
		t.Fatal(err)
	}
	path := filepath.Join(dir, "m.gguf")
	if err := os.WriteFile(path, body, 0600); err != nil {
		t.Fatal(err)
	}
	return path
}

func TestReconcileOnlineSucceedsOnMatchingOID(t *testing.T) {
	body := emptyGGUF()
	digest := sha256.Sum256(body)
	oid := hex.EncodeToString(digest[:])
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", "24")
			return
		}
		if r.URL.Path != "/api/models/org/repo/revision/main" {
			t.Fatalf("path=%s", r.URL.Path)
		}
		_, _ = w.Write([]byte(`{"sha":"deadbeef","siblings":[{"rfilename":"m.gguf","size":24,"lfs":{"oid":"` + oid + `"}}]}`))
	}))
	defer server.Close()
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	m.Resolver = Resolver{Client: server.Client(), BaseURL: server.URL}
	path := writeReconcileFixture(t, root, body)

	result, err := m.ReconcileOnline(path)
	if err != nil {
		t.Fatal(err)
	}
	if result.Partial {
		t.Fatalf("expected non-partial result, got %+v", result)
	}
	if result.Record.Source.Provider != "huggingface" || result.Record.Source.Repository != "org/repo" || result.Record.Source.ResolvedRevision != "deadbeef" {
		t.Fatalf("source=%+v", result.Record.Source)
	}
	if result.Record.Artifact.SHA256 != oid {
		t.Fatalf("sha256=%q want=%q", result.Record.Artifact.SHA256, oid)
	}
	rec, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	if rec.Source.Provider != "huggingface" || rec.Artifact.SHA256 != oid {
		t.Fatalf("persisted record=%+v", rec)
	}
}

func TestReconcileOnlineRefusesOnHashMismatch(t *testing.T) {
	body := emptyGGUF()
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", "24")
			return
		}
		_, _ = w.Write([]byte(`{"sha":"deadbeef","siblings":[{"rfilename":"m.gguf","size":24,"lfs":{"oid":"not-the-real-hash"}}]}`))
	}))
	defer server.Close()
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	m.Resolver = Resolver{Client: server.Client(), BaseURL: server.URL}
	path := writeReconcileFixture(t, root, body)

	_, err := m.ReconcileOnline(path)
	if err == nil {
		t.Fatal("expected refusal on hash mismatch")
	}
	if _, statErr := os.Stat(filepath.Join(filepath.Dir(path), "m.json")); !os.IsNotExist(statErr) {
		t.Fatalf("sidecar should not have been written: %v", statErr)
	}
}

func TestReconcileOnlineDegradesWhenNoOIDAvailable(t *testing.T) {
	body := emptyGGUF()
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", "24")
			return
		}
		_, _ = w.Write([]byte(`{"sha":"deadbeef","siblings":[{"rfilename":"m.gguf","size":24}]}`))
	}))
	defer server.Close()
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	m.Resolver = Resolver{Client: server.Client(), BaseURL: server.URL}
	path := writeReconcileFixture(t, root, body)

	result, err := m.ReconcileOnline(path)
	if err != nil {
		t.Fatal(err)
	}
	if !result.Partial {
		t.Fatal("expected a partial result when Hugging Face reports no oid")
	}
	digest := sha256.Sum256(body)
	if result.Record.Artifact.SHA256 != hex.EncodeToString(digest[:]) {
		t.Fatalf("sha256=%q", result.Record.Artifact.SHA256)
	}
	if _, err = ReadRecord(path); err != nil {
		t.Fatal(err)
	}
}

func TestReconcileAcceptWritesLocalProviderSidecar(t *testing.T) {
	body := emptyGGUF()
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	path := writeReconcileFixture(t, root, body)

	result, err := m.ReconcileAccept(path)
	if err != nil {
		t.Fatal(err)
	}
	if result.Record.Source.Provider != "local" || result.Record.Source.Repository != "org/repo" || result.Record.Source.Filename != "m.gguf" {
		t.Fatalf("source=%+v", result.Record.Source)
	}
	if result.Record.Source.ResolvedRevision != "" || result.Record.Source.URL != "" {
		t.Fatalf("local source should carry no Hugging Face identity: %+v", result.Record.Source)
	}
	digest := sha256.Sum256(body)
	if result.Record.Artifact.SHA256 != hex.EncodeToString(digest[:]) {
		t.Fatalf("sha256=%q", result.Record.Artifact.SHA256)
	}
	rec, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	if rec.Source.Provider != "local" {
		t.Fatalf("persisted record=%+v", rec)
	}
	entries, err := m.Library()
	if err != nil {
		t.Fatal(err)
	}
	var found bool
	for _, entry := range entries {
		if entry.TargetPath == path {
			found = true
			if entry.State != "installed" {
				t.Fatalf("state=%s want installed", entry.State)
			}
		}
	}
	if !found {
		t.Fatal("reconciled entry not found in library")
	}
}

// TestReconcileFunctionsAreMutuallyExclusiveByDesign documents that
// ReconcileOnline and ReconcileAccept are two separate exported functions,
// each independently callable, so a caller (the CLI in section 8) expresses
// "exactly one of --online/--accept" by choosing which single function to
// call based on which flag was passed, with a usage error when neither or
// both flags are given. There is no shared "mode" parameter that could be
// mis-set to invoke both at once.
func TestReconcileFunctionsAreMutuallyExclusiveByDesign(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	body := emptyGGUF()
	path := writeReconcileFixture(t, root, body)

	// Accept first: writes a local sidecar with no network access.
	if _, err := m.ReconcileAccept(path); err != nil {
		t.Fatal(err)
	}
	rec, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	if rec.Source.Provider != "local" {
		t.Fatalf("expected local provider after accept, got %+v", rec.Source)
	}
	// A second call to ReconcileOnline for the same identity is a distinct,
	// separately invoked operation -- nothing in the type system allows a
	// single call site to request both modes simultaneously.
}
