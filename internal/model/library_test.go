package model

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestPublishWritesRevisionUnit(t *testing.T) {
	root := t.TempDir()
	partial := filepath.Join(root, "partial")
	if err := os.WriteFile(partial, []byte("bytes"), 0600); err != nil {
		t.Fatal(err)
	}
	r := Record{Source: Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: "https://example.test/abc/m.gguf"}, Artifact: Artifact{SizeBytes: 5}, GGUF: map[string]any{"general.name": "M"}}
	target, err := Publish(filepath.Join(root, "models"), partial, r)
	if err != nil {
		t.Fatal(err)
	}
	if _, err = os.Stat(target); err != nil {
		t.Fatal(err)
	}
	if got, err := os.ReadFile(strings.TrimSuffix(target, ".gguf") + ".revision"); err != nil || string(got) != "abc\n" {
		t.Fatalf("revision=%q err=%v", got, err)
	}
	got, err := ReadRecord(target)
	if err != nil || got.Source.ResolvedRevision != "abc" {
		t.Fatalf("ReadRecord=%+v err=%v", got, err)
	}
}
func TestReadRecordRejectsMismatchedRevision(t *testing.T) {
	root := t.TempDir()
	target := filepath.Join(root, "m.gguf")
	_ = os.WriteFile(strings.TrimSuffix(target, ".gguf")+".json", []byte(`{"schemaVersion":1,"source":{"provider":"huggingface","repository":"o/r","requestedRevision":"main","resolvedRevision":"one","filename":"m.gguf","url":"u"},"artifact":{"sizeBytes":1},"gguf":{}}`), 0600)
	_ = os.WriteFile(strings.TrimSuffix(target, ".gguf")+".revision", []byte("two\n"), 0600)
	if _, err := ReadRecord(target); err != ErrInconsistentRevision {
		t.Fatalf("err=%v", err)
	}
}
func TestTargetPathRejectsTraversal(t *testing.T) {
	_, err := TargetPath(t.TempDir(), Source{Provider: "huggingface", Repository: "org/../escape", RequestedRevision: "main", ResolvedRevision: "sha", Filename: "m.gguf", URL: "https://example.test"})
	if err == nil {
		t.Fatal("unsafe path accepted")
	}
}
func TestPublishIsIdempotentOnlyForMatchingUnit(t *testing.T) {
	root := t.TempDir()
	source := Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "sha", Filename: "m.gguf", URL: "https://example.test"}
	partial := filepath.Join(root, "one")
	_ = os.WriteFile(partial, []byte("data"), 0600)
	target, err := Publish(filepath.Join(root, "models"), partial, Record{Source: source, Artifact: Artifact{SizeBytes: 4}, GGUF: map[string]any{}})
	if err != nil {
		t.Fatal(err)
	}
	partial = filepath.Join(root, "two")
	_ = os.WriteFile(partial, []byte("other"), 0600)
	got, err := Publish(filepath.Join(root, "models"), partial, Record{Source: source, Artifact: Artifact{SizeBytes: 4}, GGUF: map[string]any{}})
	if err != nil || got != target {
		t.Fatalf("got=%q err=%v", got, err)
	}
}
