package model

import (
	"encoding/binary"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// classifiableGGUF builds a minimal real GGUF header declaring a principal
// model and carrying a vocabulary array, so publication has both something to
// classify and something it must refuse to persist.
func classifiableGGUF() []byte {
	b := emptyGGUF()
	binary.LittleEndian.PutUint64(b[16:], 3)
	b = appendString(b, "general.architecture")
	b = appendType(b, 8)
	b = appendString(b, "gemma4")
	b = appendString(b, "general.type")
	b = appendType(b, 8)
	b = appendString(b, "model")
	b = appendString(b, "tokenizer.ggml.tokens")
	b = appendType(b, 9)
	b = appendType(b, 8)
	var count [8]byte
	binary.LittleEndian.PutUint64(count[:], 2)
	b = append(b, count[:]...)
	b = appendString(b, "vocabulary-token-one")
	return appendString(b, "vocabulary-token-two")
}

func TestPublishRecordsClassificationAndNoRawMetadata(t *testing.T) {
	root := t.TempDir()
	partial := filepath.Join(root, "partial")
	body := classifiableGGUF()
	if err := os.WriteFile(partial, body, 0600); err != nil {
		t.Fatal(err)
	}
	source := Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: "https://example.test"}
	target, err := Publish(filepath.Join(root, "models"), partial, Record{Source: source, Artifact: Artifact{SizeBytes: int64(len(body))}})
	if err != nil {
		t.Fatal(err)
	}
	sidecar, err := os.ReadFile(strings.TrimSuffix(target, ".gguf") + ".json")
	if err != nil {
		t.Fatal(err)
	}
	if strings.Contains(string(sidecar), `"gguf"`) || strings.Contains(string(sidecar), "vocabulary-token-one") {
		t.Fatalf("sidecar persisted raw metadata: %s", sidecar)
	}
	got, err := ReadRecord(target)
	if err != nil {
		t.Fatal(err)
	}
	if got.SchemaVersion != RecordSchemaVersion {
		t.Fatalf("schemaVersion=%d", got.SchemaVersion)
	}
	if got.Classification.Type != TypeModel || got.Classification.Architecture != "gemma4" {
		t.Fatalf("classification=%#v", got.Classification)
	}
	if got.Classification.Tokenizer.VocabSize != 2 || got.Classification.Tokenizer.Digest == "" {
		t.Fatalf("tokenizer=%#v", got.Classification.Tokenizer)
	}
}

func TestReadRecordRejectsPreviousSchemaVersion(t *testing.T) {
	root := t.TempDir()
	target := filepath.Join(root, "m.gguf")
	base := strings.TrimSuffix(target, ".gguf")
	if err := os.WriteFile(base+".json", []byte(`{"schemaVersion":1,"source":{"provider":"huggingface","repository":"o/r","requestedRevision":"main","resolvedRevision":"abc","filename":"m.gguf","url":"u"},"artifact":{"sizeBytes":1},"gguf":{"general.name":"M"}}`), 0600); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(base+".revision", []byte("abc\n"), 0600); err != nil {
		t.Fatal(err)
	}
	if _, err := ReadRecord(target); err == nil {
		t.Fatal("previous-version sidecar accepted")
	}
}

func TestLibraryReportsPreviousSchemaVersionAsUnreconciled(t *testing.T) {
	root := t.TempDir()
	models := filepath.Join(root, "models")
	dir := filepath.Join(models, "org", "repo")
	if err := os.MkdirAll(dir, 0755); err != nil {
		t.Fatal(err)
	}
	target := filepath.Join(dir, "m.gguf")
	if err := os.WriteFile(target, classifiableGGUF(), 0600); err != nil {
		t.Fatal(err)
	}
	base := strings.TrimSuffix(target, ".gguf")
	if err := os.WriteFile(base+".json", []byte(`{"schemaVersion":1,"source":{"provider":"huggingface","repository":"org/repo","requestedRevision":"main","resolvedRevision":"abc","filename":"m.gguf","url":"u"},"artifact":{"sizeBytes":1},"gguf":{}}`), 0600); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(base+".revision", []byte("abc\n"), 0600); err != nil {
		t.Fatal(err)
	}
	entries, err := NewManager(models).Library()
	if err != nil {
		t.Fatal(err)
	}
	if len(entries) != 1 || entries[0].State != "unreconciled" {
		t.Fatalf("entries=%+v", entries)
	}
}

func TestPublishWritesRevisionUnit(t *testing.T) {
	root := t.TempDir()
	partial := filepath.Join(root, "partial")
	if err := os.WriteFile(partial, []byte("bytes"), 0600); err != nil {
		t.Fatal(err)
	}
	r := Record{Source: Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: "https://example.test/abc/m.gguf"}, Artifact: Artifact{SizeBytes: 5}, Metadata: map[string]any{"general.name": "M"}}
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
	_ = os.WriteFile(strings.TrimSuffix(target, ".gguf")+".json", []byte(`{"schemaVersion":2,"source":{"provider":"huggingface","repository":"o/r","requestedRevision":"main","resolvedRevision":"one","filename":"m.gguf","url":"u"},"artifact":{"sizeBytes":1},"classification":{"type":"model"}}`), 0600)
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
	target, err := Publish(filepath.Join(root, "models"), partial, Record{Source: source, Artifact: Artifact{SizeBytes: 4}})
	if err != nil {
		t.Fatal(err)
	}
	partial = filepath.Join(root, "two")
	_ = os.WriteFile(partial, []byte("other"), 0600)
	got, err := Publish(filepath.Join(root, "models"), partial, Record{Source: source, Artifact: Artifact{SizeBytes: 4}})
	if err != nil || got != target {
		t.Fatalf("got=%q err=%v", got, err)
	}
}
