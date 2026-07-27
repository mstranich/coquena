package model

import (
	"fmt"
	"os"
	"path/filepath"
	"slices"
	"strings"
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
	if !ok || amb.Count() != 2 {
		t.Fatalf("err=%v", err)
	}
	// The diagnostic must name which artifacts matched, not only how many.
	for _, want := range []string{"model-Q4_K_M.gguf", "model-Q4_K_M-imatrix.gguf"} {
		if !slices.Contains(amb.Matches, want) {
			t.Fatalf("matches=%v missing %q", amb.Matches, want)
		}
		if !strings.Contains(amb.Error(), want) {
			t.Fatalf("error=%q missing %q", amb.Error(), want)
		}
	}
}

func TestResolveInstalledNoMatchNamesAvailableFilenames(t *testing.T) {
	dir := t.TempDir()
	installFixture(t, dir, "org/repo", "model-Q4_K_M.gguf", "abc")
	installFixture(t, dir, "org/repo", "model-Q8_0.gguf", "def")
	m := NewManager(dir)
	_, err := m.ResolveInstalled("org/repo:Q2_K")
	missing, ok := err.(*ErrNoInstalledMatch)
	if !ok {
		t.Fatalf("err=%T %v", err, err)
	}
	for _, want := range []string{"model-Q4_K_M.gguf", "model-Q8_0.gguf"} {
		if !slices.Contains(missing.Available, want) {
			t.Fatalf("available=%v missing %q", missing.Available, want)
		}
		if !strings.Contains(missing.Error(), want) {
			t.Fatalf("error=%q missing %q", missing.Error(), want)
		}
	}
}

func TestErrorStringTruncatesLongCandidateLists(t *testing.T) {
	names := make([]string, 0, 40)
	for i := 0; i < 40; i++ {
		names = append(names, fmt.Sprintf("model-%02d.gguf", i))
	}
	err := &ErrAmbiguousInstalledMatch{Reference: "org/repo:gguf", Matches: names}
	message := err.Error()
	if !strings.Contains(message, "and 32 more") {
		t.Fatalf("message=%q", message)
	}
	if strings.Contains(message, "model-39.gguf") {
		t.Fatalf("message renders the whole list: %q", message)
	}
	if len(err.Matches) != 40 {
		t.Fatalf("structured field truncated: %d", len(err.Matches))
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
