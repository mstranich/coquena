package model

import (
	"bytes"
	"encoding/binary"
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func validDerivedGGUF() []byte {
	b := emptyGGUF()
	binary.LittleEndian.PutUint64(b[8:], 1)
	binary.LittleEndian.PutUint64(b[16:], 2)
	b = appendString(b, "general.type")
	b = appendType(b, 8)
	b = appendString(b, "model")
	b = appendString(b, "general.file_type")
	b = appendType(b, 11)
	var ft [8]byte
	binary.LittleEndian.PutUint64(ft[:], 15)
	b = append(b, ft[:]...)
	b = append(b, buildTensorTable([]tensorEntry{{name: "blk.0.weight", tensorType: 12, shape: []uint64{2, 3}}})...)
	return b
}

func TestVerifyRepairsLegacyDerivationAndPreservesOtherAuthorities(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	body := validDerivedGGUF()
	path := writeReconcileFixture(t, root, body)
	if _, err := m.ReconcileAccept(path); err != nil {
		t.Fatal(err)
	}
	record, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	base := strings.TrimSuffix(path, filepath.Ext(path))
	legacy := map[string]any{
		"schemaVersion": 2, "source": record.Source, "artifact": record.Artifact,
		"classification": record.Classification,
		"drift":          map[string]any{"outcome": "current", "lastCheckedAt": "2026-08-04T00:00:00Z"},
	}
	legacyBytes, _ := json.MarshalIndent(legacy, "", "  ")
	legacyBytes = append(legacyBytes, '\n')
	if err = os.WriteFile(base+".json", legacyBytes, 0600); err != nil {
		t.Fatal(err)
	}

	before, _ := os.ReadFile(base + ".json")
	plain, err := m.Verify("org/repo")
	if err != nil {
		t.Fatal(err)
	}
	if !plain.DerivationStale || plain.Repaired {
		t.Fatalf("plain=%+v", plain)
	}
	afterPlain, _ := os.ReadFile(base + ".json")
	if !bytes.Equal(before, afterPlain) {
		t.Fatal("plain verify rewrote legacy sidecar")
	}

	repaired, err := m.VerifyWithOptions("org/repo", VerifyOptions{RepairDerived: true})
	if err != nil {
		t.Fatal(err)
	}
	if !repaired.Repaired || repaired.DerivationStale || repaired.DeclaredRecipe != "Q4_K_M" {
		t.Fatalf("repaired=%+v", repaired)
	}
	current, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	if current.SchemaVersion != 3 || current.Derivation.Version != 1 || current.Derivation.CountParameters != 6 {
		t.Fatalf("record=%+v", current)
	}
	if current.Source != record.Source || current.Artifact != record.Artifact {
		t.Fatal("repair changed source or artifact evidence")
	}
	raw, _ := os.ReadFile(base + ".json")
	if !bytes.Contains(raw, []byte(`"drift"`)) {
		t.Fatalf("unrelated authority field lost: %s", raw)
	}
	modelAfter, _ := os.ReadFile(path)
	if !bytes.Equal(body, modelAfter) {
		t.Fatal("repair changed model bytes")
	}
}

func TestVerifyRefusesDerivedRepairOnMismatch(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	body := validDerivedGGUF()
	path := writeReconcileFixture(t, root, body)
	if _, err := m.ReconcileAccept(path); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path, append(body, 0), 0600); err != nil {
		t.Fatal(err)
	}
	base := strings.TrimSuffix(path, filepath.Ext(path))
	before, _ := os.ReadFile(base + ".json")
	if _, err := m.VerifyWithOptions("org/repo", VerifyOptions{RepairDerived: true}); err == nil {
		t.Fatal("mismatched repair succeeded")
	}
	after, _ := os.ReadFile(base + ".json")
	if !bytes.Equal(before, after) {
		t.Fatal("refused repair changed sidecar")
	}
}

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
