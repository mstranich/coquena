package model

import (
	"errors"
	"os"
	"path/filepath"
	"testing"
)

func TestJobValidateRequiresPinnedSource(t *testing.T) {
	j := Job{ID: "x", Status: Paused, Source: Source{Provider: "huggingface", Repository: "org/repo", Filename: "m.gguf"}}
	if err := j.Validate(); err == nil {
		t.Fatal("Validate accepted mutable source")
	}
	j.Source.ResolvedRevision = "abc"
	j.Source.URL = "https://example.test/abc"
	if err := j.Validate(); err != nil {
		t.Fatalf("Validate: %v", err)
	}
}
func TestSourceValidateLocalProviderSucceedsWithoutRevisionOrURL(t *testing.T) {
	s := Source{Provider: "local", Repository: "org/repo", Filename: "m.gguf"}
	if err := s.Validate(); err != nil {
		t.Fatalf("Validate: %v", err)
	}
}

func TestSourceValidateLocalProviderRequiresRepository(t *testing.T) {
	s := Source{Provider: "local", Filename: "m.gguf"}
	if err := s.Validate(); err == nil {
		t.Fatal("Validate accepted local source missing Repository")
	}
}

func TestSourceValidateLocalProviderRequiresFilename(t *testing.T) {
	s := Source{Provider: "local", Repository: "org/repo"}
	if err := s.Validate(); err == nil {
		t.Fatal("Validate accepted local source missing Filename")
	}
}

func TestSourceValidateHuggingFaceRequiresAllFields(t *testing.T) {
	s := Source{Provider: "huggingface", Repository: "org/repo", Filename: "m.gguf"}
	if err := s.Validate(); err == nil {
		t.Fatal("Validate accepted huggingface source missing ResolvedRevision/URL")
	}
	s.ResolvedRevision = "abc"
	s.URL = "https://example.test/abc"
	if err := s.Validate(); err != nil {
		t.Fatalf("Validate: %v", err)
	}
}

func TestLocalReader(t *testing.T) {
	p := filepath.Join(t.TempDir(), "model.gguf")
	if err := os.WriteFile(p, []byte("GGUF-data"), 0600); err != nil {
		t.Fatal(err)
	}
	r, err := OpenLocal(p)
	if err != nil {
		t.Fatal(err)
	}
	defer r.Close()
	b := make([]byte, 4)
	n, err := r.ReadAt(b, 0)
	if err != nil || n != 4 || string(b) != "GGUF" {
		t.Fatalf("ReadAt = %d,%q,%v", n, b, err)
	}
	if r.Size() != 9 {
		t.Fatalf("Size=%d", r.Size())
	}
	_, err = r.ReadAt(b, 99)
	if !errors.Is(err, os.ErrInvalid) && err == nil {
		t.Fatal("out of range read succeeded")
	}
}
func TestReadMetadataRejectsInvalidMagic(t *testing.T) {
	p := filepath.Join(t.TempDir(), "bad.gguf")
	_ = os.WriteFile(p, make([]byte, 24), 0600)
	r, _ := OpenLocal(p)
	defer r.Close()
	if _, err := ReadMetadata(r); err == nil {
		t.Fatal("invalid GGUF accepted")
	}
}
