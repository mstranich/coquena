package model

import (
	"bytes"
	"encoding/binary"
	"maps"
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

// TestLibraryClassifiesUnreconciledArtifactFromItsHeader covers the case a
// real library is made of: files another application downloaded, with no
// sidecar at all. The type column must be filled from the artifact itself.
func TestLibraryClassifiesUnreconciledArtifactFromItsHeader(t *testing.T) {
	models := t.TempDir()
	writeArtifact(t, models, "org/repo/m.gguf", classifiableGGUF())
	writeArtifact(t, models, "org/repo/mmproj.gguf", projectorGGUF())
	entries, err := NewManager(models).Library()
	if err != nil {
		t.Fatal(err)
	}
	if len(entries) != 2 {
		t.Fatalf("entries=%+v", entries)
	}
	byName := map[string]LibraryEntry{}
	for _, entry := range entries {
		byName[entry.Source.Filename] = entry
	}
	principal, ok := byName["m.gguf"]
	if !ok || principal.State != "unreconciled" || principal.Classification == nil {
		t.Fatalf("principal=%+v", principal)
	}
	if principal.Classification.Type != TypeModel || principal.Classification.Architecture != "gemma4" {
		t.Fatalf("classification=%#v", principal.Classification)
	}
	// The vocabulary count survives the skipping read, while its digest does
	// not: the tokens were never decoded, and an empty digest is the honest
	// report of that.
	if principal.Classification.Tokenizer.VocabSize != 2 {
		t.Fatalf("vocabSize=%d", principal.Classification.Tokenizer.VocabSize)
	}
	if principal.Classification.Tokenizer.Digest != "" {
		t.Fatalf("digest=%q from a skipping read", principal.Classification.Tokenizer.Digest)
	}
	projector, ok := byName["mmproj.gguf"]
	if !ok || projector.Classification == nil || projector.Classification.Type != TypeProjector || projector.Classification.ProjectionDim != 5120 {
		t.Fatalf("projector=%+v", projector)
	}
}

// TestLibraryReportsUnknownForAnUnreadableHeader confirms one malformed file
// degrades to an explicit unknown rather than hiding the rest of the library.
func TestLibraryReportsUnknownForAnUnreadableHeader(t *testing.T) {
	models := t.TempDir()
	writeArtifact(t, models, "org/repo/broken.gguf", []byte("not a gguf at all"))
	writeArtifact(t, models, "org/repo/m.gguf", classifiableGGUF())
	entries, err := NewManager(models).Library()
	if err != nil {
		t.Fatalf("one malformed artifact failed the whole enumeration: %v", err)
	}
	if len(entries) != 2 {
		t.Fatalf("entries=%+v", entries)
	}
	for _, entry := range entries {
		if entry.Classification == nil {
			t.Fatalf("entry=%+v has no classification", entry)
		}
		if entry.Source.Filename == "broken.gguf" && entry.Classification.Type != TypeUnknown {
			t.Fatalf("broken classification=%#v", entry.Classification)
		}
	}
}

// TestLibraryUsesSidecarClassificationForInstalled pins that an installed
// artifact reports what its sidecar records rather than a fresh read of the
// file. The sidecar here deliberately contradicts the header.
func TestLibraryUsesSidecarClassificationForInstalled(t *testing.T) {
	models := t.TempDir()
	target := writeArtifact(t, models, "org/repo/m.gguf", classifiableGGUF())
	base := strings.TrimSuffix(target, ".gguf")
	if err := os.WriteFile(base+".json", []byte(`{"schemaVersion":4,"source":{"provider":"huggingface","repository":"org/repo","requestedRevision":"main","resolvedRevision":"abc","filename":"m.gguf","url":"u"},"artifact":{"sizeBytes":1,"digests":[{"role":"integrity","algorithm":"sha256","domain":"raw-file","value":"recorded"}]},"derivation":{"version":0,"classification":{"type":"mmproj","architecture":"recorded-by-sidecar"}}}`), 0600); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(base+".revision", []byte("abc\n"), 0600); err != nil {
		t.Fatal(err)
	}
	entries, err := NewManager(models).Library()
	if err != nil {
		t.Fatal(err)
	}
	if len(entries) != 1 || entries[0].State != "installed" || entries[0].Classification == nil {
		t.Fatalf("entries=%+v", entries)
	}
	if entries[0].Classification.Type != TypeProjector || entries[0].Classification.Architecture != "recorded-by-sidecar" {
		t.Fatalf("classification=%#v was re-read from the artifact", entries[0].Classification)
	}
	if !entries[0].DerivationStale {
		t.Fatal("a sidecar whose derivation algorithm version has fallen behind was not reported as stale")
	}
	after, err := os.ReadFile(base + ".json")
	if err != nil || !bytes.Contains(after, []byte(`"version":0`)) {
		t.Fatalf("library read rewrote the sidecar rather than reporting it stale: %s err=%v", after, err)
	}
}

// TestLibraryWritesNothingOverForeignArtifacts is the constraint classification
// at enumeration must not breach: reading a header grants no standing, so no
// sidecar, revision unit or temporary file may appear, and the artifacts stay
// unresolvable for binding.
func TestLibraryWritesNothingOverForeignArtifacts(t *testing.T) {
	models := t.TempDir()
	writeArtifact(t, models, "org/repo/m.gguf", classifiableGGUF())
	writeArtifact(t, models, "org/repo/mmproj.gguf", projectorGGUF())
	writeArtifact(t, models, "other/repo/n.gguf", principalGGUF())
	before := treeSnapshot(t, models)
	manager := NewManager(models)
	if _, err := manager.Library(); err != nil {
		t.Fatal(err)
	}
	if _, err := manager.Library(); err != nil {
		t.Fatal(err)
	}
	after := treeSnapshot(t, models)
	if !maps.Equal(before, after) {
		t.Fatalf("enumeration changed the models directory:\nbefore=%v\nafter=%v", before, after)
	}
	// Classified but still without standing: binding must not resolve them.
	if _, err := manager.ResolveInstalled("org/repo:m.gguf"); err == nil {
		t.Fatal("a classified unreconciled artifact resolved as installed")
	}
}

// treeSnapshot records every file beneath root with its size, so a test can
// assert that an operation created, removed or rewrote nothing.
func treeSnapshot(t *testing.T, root string) map[string]int64 {
	t.Helper()
	out := map[string]int64{}
	err := filepath.WalkDir(root, func(path string, entry os.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if entry.IsDir() {
			return nil
		}
		info, err := entry.Info()
		if err != nil {
			return err
		}
		rel, err := filepath.Rel(root, path)
		if err != nil {
			return err
		}
		out[filepath.ToSlash(rel)] = info.Size()
		return nil
	})
	if err != nil {
		t.Fatal(err)
	}
	return out
}

func TestPublishWritesRevisionUnit(t *testing.T) {
	root := t.TempDir()
	partial := filepath.Join(root, "partial")
	if err := os.WriteFile(partial, []byte("bytes"), 0600); err != nil {
		t.Fatal(err)
	}
	r := Record{Source: Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: "https://example.test/abc/m.gguf"}, Artifact: Artifact{SizeBytes: 5}}
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
	_ = os.WriteFile(strings.TrimSuffix(target, ".gguf")+".json", []byte(`{"schemaVersion":4,"source":{"provider":"huggingface","repository":"o/r","requestedRevision":"main","resolvedRevision":"one","filename":"m.gguf","url":"u"},"artifact":{"sizeBytes":1,"digests":[{"role":"integrity","algorithm":"sha256","domain":"raw-file","value":"recorded"}]},"derivation":{"version":1,"classification":{"type":"model"}}}`), 0600)
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
