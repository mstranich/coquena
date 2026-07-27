package model

import (
	"os"
	"path/filepath"
	"testing"
)

func installFixture(t *testing.T, modelsDir, repository, filename, revision string) {
	t.Helper()
	partial := filepath.Join(t.TempDir(), "partial")
	if err := os.WriteFile(partial, []byte("bytes"), 0600); err != nil {
		t.Fatal(err)
	}
	r := Record{Source: Source{Provider: "huggingface", Repository: repository, RequestedRevision: "main", ResolvedRevision: revision, Filename: filename, URL: "https://example.test/" + filename}, Artifact: Artifact{SizeBytes: 5}}
	if _, err := Publish(modelsDir, partial, r); err != nil {
		t.Fatal(err)
	}
}

func TestResolveInstalledSingleMatch(t *testing.T) {
	dir := t.TempDir()
	installFixture(t, dir, "org/repo", "model-Q4_K_M.gguf", "abc")
	m := NewManager(dir)
	entry, err := m.ResolveInstalled("org/repo:Q4_K_M")
	if err != nil {
		t.Fatal(err)
	}
	if entry.Source.Filename != "model-Q4_K_M.gguf" {
		t.Fatalf("filename=%q", entry.Source.Filename)
	}
}

func TestResolveInstalledNoMatch(t *testing.T) {
	dir := t.TempDir()
	m := NewManager(dir)
	_, err := m.ResolveInstalled("org/repo:Q4_K_M")
	if _, ok := err.(*ErrNoInstalledMatch); !ok {
		t.Fatalf("err=%v", err)
	}
}

func TestResolveInstalledAmbiguousMatch(t *testing.T) {
	dir := t.TempDir()
	installFixture(t, dir, "org/repo", "model-Q4_K_M.gguf", "abc")
	installFixture(t, dir, "org/repo", "model-Q4_K_M-imatrix.gguf", "def")
	m := NewManager(dir)
	_, err := m.ResolveInstalled("org/repo:Q4_K_M")
	amb, ok := err.(*ErrAmbiguousInstalledMatch)
	if !ok || amb.Count != 2 {
		t.Fatalf("err=%v", err)
	}
}

func TestResolveInstalledExactFilenameSelectorMatchesOne(t *testing.T) {
	dir := t.TempDir()
	installFixture(t, dir, "org/repo", "model-Q4_K_M.gguf", "abc")
	installFixture(t, dir, "org/repo", "model-Q4_K_M-imatrix.gguf", "def")
	m := NewManager(dir)
	entry, err := m.ResolveInstalled("org/repo:model-Q4_K_M.gguf")
	if err != nil {
		t.Fatal(err)
	}
	if entry.Source.Filename != "model-Q4_K_M.gguf" {
		t.Fatalf("filename=%q", entry.Source.Filename)
	}
}

func TestResolveInstalledExactFilenameSelectorMatchesNone(t *testing.T) {
	dir := t.TempDir()
	installFixture(t, dir, "org/repo", "model-Q4_K_M.gguf", "abc")
	m := NewManager(dir)
	_, err := m.ResolveInstalled("org/repo:model-Q8_0.gguf")
	if _, ok := err.(*ErrNoInstalledMatch); !ok {
		t.Fatalf("err=%v", err)
	}
}

func TestResolveInstalledEmptyLibrary(t *testing.T) {
	dir := t.TempDir()
	m := NewManager(dir)
	entries, err := m.Library()
	if err != nil || len(entries) != 0 {
		t.Fatalf("entries=%v err=%v", entries, err)
	}
	if _, err = m.ResolveInstalled("org/repo:Q4_K_M"); err == nil {
		t.Fatal("expected error")
	}
}
