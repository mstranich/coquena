package model

import (
	"os"
	"path/filepath"
	"testing"
)

func TestVerifyMatchedOnIntactFile(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	body := emptyGGUF()
	path := writeReconcileFixture(t, root, body)
	if _, err := m.ReconcileAccept(path); err != nil {
		t.Fatal(err)
	}

	result, err := m.Verify("org/repo")
	if err != nil {
		t.Fatal(err)
	}
	if result.Outcome != VerifyMatched {
		t.Fatalf("outcome=%q want=%q result=%+v", result.Outcome, VerifyMatched, result)
	}
	if result.RecordedSHA256 == "" || result.RecordedSHA256 != result.ComputedSHA256 {
		t.Fatalf("recorded=%q computed=%q", result.RecordedSHA256, result.ComputedSHA256)
	}
}

func TestVerifyMismatchedOnCorruptedFile(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	body := emptyGGUF()
	path := writeReconcileFixture(t, root, body)
	if _, err := m.ReconcileAccept(path); err != nil {
		t.Fatal(err)
	}
	corrupted := append([]byte{}, body...)
	corrupted[0] ^= 0xFF
	if err := os.WriteFile(path, corrupted, 0600); err != nil {
		t.Fatal(err)
	}

	result, err := m.Verify("org/repo")
	if err != nil {
		t.Fatal(err)
	}
	if result.Outcome != VerifyMismatched {
		t.Fatalf("outcome=%q want=%q result=%+v", result.Outcome, VerifyMismatched, result)
	}
	if result.RecordedSHA256 == result.ComputedSHA256 {
		t.Fatalf("expected digests to differ, both=%q", result.RecordedSHA256)
	}
}

func TestVerifyNoBaselineWhenSidecarHasEmptySHA256(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	body := emptyGGUF()
	path := writeReconcileFixture(t, root, body)
	if _, err := m.ReconcileAccept(path); err != nil {
		t.Fatal(err)
	}
	record, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	record.Artifact.SHA256 = ""
	if _, err := PublishInPlace(m.ModelsDir, record); err != nil {
		t.Fatal(err)
	}

	result, err := m.Verify("org/repo")
	if err != nil {
		t.Fatal(err)
	}
	if result.Outcome != VerifyNoBaseline {
		t.Fatalf("outcome=%q want=%q result=%+v", result.Outcome, VerifyNoBaseline, result)
	}
	if result.RecordedSHA256 != "" {
		t.Fatalf("expected empty recorded sha256, got %q", result.RecordedSHA256)
	}
}

func TestVerifyUnresolvableReference(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))

	if _, err := m.Verify("no/such"); err == nil {
		t.Fatal("expected error for no match")
	} else if _, ok := err.(*ErrNoInstalledMatch); !ok {
		t.Fatalf("err type=%T want *ErrNoInstalledMatch: %v", err, err)
	}

	body := emptyGGUF()
	dir := filepath.Join(root, "models", "org", "repo")
	if err := os.MkdirAll(dir, 0755); err != nil {
		t.Fatal(err)
	}
	pathA := filepath.Join(dir, "model-a.gguf")
	pathB := filepath.Join(dir, "model-b.gguf")
	if err := os.WriteFile(pathA, body, 0600); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(pathB, body, 0600); err != nil {
		t.Fatal(err)
	}
	m2 := NewManager(filepath.Join(root, "models"))
	sourceA := unreconciledSource(m2.ModelsDir, pathA)
	sourceA.Provider = "local"
	sourceB := unreconciledSource(m2.ModelsDir, pathB)
	sourceB.Provider = "local"
	if _, err := PublishInPlace(m2.ModelsDir, Record{SchemaVersion: RecordSchemaVersion, Source: sourceA, Artifact: Artifact{SizeBytes: int64(len(body))}}); err != nil {
		t.Fatal(err)
	}
	if _, err := PublishInPlace(m2.ModelsDir, Record{SchemaVersion: RecordSchemaVersion, Source: sourceB, Artifact: Artifact{SizeBytes: int64(len(body))}}); err != nil {
		t.Fatal(err)
	}

	if _, err := m2.Verify("org/repo:model"); err == nil {
		t.Fatal("expected ambiguous match error")
	} else if _, ok := err.(*ErrAmbiguousInstalledMatch); !ok {
		t.Fatalf("err type=%T want *ErrAmbiguousInstalledMatch: %v", err, err)
	}
}
