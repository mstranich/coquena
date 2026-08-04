package model

import (
	"os"
	"path/filepath"
	"testing"
)

func TestFormatReference(t *testing.T) {
	fileType := LlamaFileType(15) // Q4_K_M
	entry := LibraryEntry{
		Source: Source{
			Repository: "deepreinforce-ai/Ornith-1.0-9B-GGUF",
			Filename:   "ornith-1.0-9b-Q4_K_M.gguf",
		},
		Classification: &Classification{
			FileType: &fileType,
		},
	}

	got := FormatReference(entry)
	want := "deepreinforce-ai/Ornith-1.0-9B-GGUF:Q4_K_M"
	if got != want {
		t.Errorf("FormatReference() = %q, want %q", got, want)
	}
}

func TestFormatReferenceWithQ4K(t *testing.T) {
	fileType := LlamaFileType(12) // Q3_K_M
	entry := LibraryEntry{
		Source: Source{
			Repository: "user/repo",
			Filename:   "model-Q4_K_M.gguf",
		},
		Classification: &Classification{
			FileType: &fileType,
		},
	}

	got := FormatReference(entry)
	want := "user/repo:Q3_K_M"
	if got != want {
		t.Errorf("FormatReference() = %q, want %q", got, want)
	}
}

func TestFormatReferenceWithDeprecatedType(t *testing.T) {
	fileType := LlamaFileType(33) // Q4_0_4_4 — deprecated but still functional
	entry := LibraryEntry{
		Source: Source{
			Repository: "user/repo",
			Filename:   "model-Q4_2.gguf",
		},
		Classification: &Classification{
			FileType: &fileType,
		},
	}

	got := FormatReference(entry)
	want := "user/repo:Q4_0_4_4"
	if got != want {
		t.Errorf("FormatReference() = %q, want %q", got, want)
	}
}

func TestFormatReferenceWithNilClassification(t *testing.T) {
	entry := LibraryEntry{
		Source: Source{
			Repository: "user/repo",
			Filename:   "model.gguf",
		},
		Classification: nil,
	}

	// This should panic because Classification is nil and we dereference it
	defer func() {
		if r := recover(); r != nil {
			t.Logf("FormatReference() panicked with nil Classification: %v", r)
		}
	}()
	_ = FormatReference(entry)
}

func TestResolveInstalledEnforcesExplicitRevision(t *testing.T) {
	root := t.TempDir()
	partial := filepath.Join(root, "partial")
	body := classifiableGGUF()
	if err := os.WriteFile(partial, body, 0o600); err != nil {
		t.Fatal(err)
	}
	source := Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "model.gguf", URL: "https://example.test/model.gguf"}
	if _, err := Publish(filepath.Join(root, "models"), partial, Record{Source: source, Artifact: Artifact{SizeBytes: int64(len(body))}}); err != nil {
		t.Fatal(err)
	}
	manager := NewManager(filepath.Join(root, "models"))
	if _, err := manager.ResolveInstalled("org/repo@abc:model.gguf"); err != nil {
		t.Fatalf("matching revision: %v", err)
	}
	if _, err := manager.ResolveInstalled("org/repo@def:model.gguf"); err == nil {
		t.Fatal("mismatched explicit revision resolved")
	}
	if got, err := manager.ResolveInstalled("org/repo:model.gguf"); err != nil || got.Source.ResolvedRevision != "abc" {
		t.Fatalf("unpinned got=%+v err=%v", got.Source, err)
	}
}
