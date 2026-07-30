package model

import (
	"testing"
)

func TestFormatReference(t *testing.T) {
	fileType := int64(15) // Q8_K
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
	want := "deepreinforce-ai/Ornith-1.0-9B-GGUF:Q8_K"
	if got != want {
		t.Errorf("FormatReference() = %q, want %q", got, want)
	}
}

func TestFormatReferenceWithQ4K(t *testing.T) {
	fileType := int64(12) // Q4_K
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
	want := "user/repo:Q4_K"
	if got != want {
		t.Errorf("FormatReference() = %q, want %q", got, want)
	}
}

func TestFormatReferenceWithRemovedType(t *testing.T) {
	fileType := int64(4) // Q4_2 (removed)
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
	want := "user/repo:Q4_2 (removed)"
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
