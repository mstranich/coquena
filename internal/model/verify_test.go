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

// TestVerifyRepairsStaleDerivationAndPreservesOtherAuthorities builds a stale
// record at the current schema rather than at an earlier one. An earlier schema
// is no longer readable at all, so the only way derivation can be stale is a
// record whose derivation algorithm version has fallen behind — which is what
// repair exists for.
func TestVerifyRepairsStaleDerivationAndPreservesOtherAuthorities(t *testing.T) {
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
	stale := map[string]any{
		"schemaVersion": RecordSchemaVersion, "source": record.Source, "artifact": record.Artifact,
		"derivation": map[string]any{"version": 0, "classification": record.Classification},
		"drift":      map[string]any{"outcome": "current", "lastCheckedAt": "2026-08-04T00:00:00Z"},
	}
	staleBytes, _ := json.MarshalIndent(stale, "", "  ")
	staleBytes = append(staleBytes, '\n')
	if err = os.WriteFile(base+".json", staleBytes, 0600); err != nil {
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
	// A plain verify writes its own axis and only that: it must not repair the
	// derivation it just reported as stale.
	afterPlain, _ := os.ReadFile(base + ".json")
	if bytes.Equal(before, afterPlain) {
		t.Fatal("verify did not record what it found")
	}
	recorded, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	if recorded.Verification == nil || recorded.Verification.Outcome != VerifyMatched {
		t.Fatalf("verification=%+v", recorded.Verification)
	}
	if !recorded.DerivationStale() {
		t.Fatal("a plain verify repaired the derivation it only had licence to report")
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
	if current.SchemaVersion != RecordSchemaVersion || current.Derivation.Version != DerivationVersion || current.Derivation.CountParameters != 6 {
		t.Fatalf("record=%+v", current)
	}
	currentIntegrity, currentOK := current.Artifact.Integrity()
	recordIntegrity, recordOK := record.Artifact.Integrity()
	if current.Source != record.Source || currentOK != recordOK || currentIntegrity != recordIntegrity {
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
	before, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	if _, err = m.VerifyWithOptions("org/repo", VerifyOptions{RepairDerived: true}); err == nil {
		t.Fatal("mismatched repair succeeded")
	}
	after, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	// The repair is refused, and the mismatch is still recorded: a refusal to
	// re-derive evidence is not a reason to forget what the hash just showed.
	if after.Verification == nil || after.Verification.Outcome != VerifyMismatched {
		t.Fatalf("verification=%+v; a refused repair must not discard the mismatch", after.Verification)
	}
	if after.Derivation.Version != before.Derivation.Version || after.Derivation.ComputedAt != before.Derivation.ComputedAt {
		t.Fatal("refused repair re-derived the artifact anyway")
	}
	beforeIntegrity, _ := before.Artifact.Integrity()
	afterIntegrity, _ := after.Artifact.Integrity()
	if afterIntegrity != beforeIntegrity {
		t.Fatal("verify replaced the integrity digest it was only meant to check against")
	}
}

func TestVerifyMatchedOnIntactFile(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	body := principalGGUF()
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
	body := principalGGUF()
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

func TestVerifyUnresolvableReference(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))

	if _, err := m.Verify("no/such"); err == nil {
		t.Fatal("expected error for no match")
	} else if _, ok := err.(*ErrNoInstalledMatch); !ok {
		t.Fatalf("err type=%T want *ErrNoInstalledMatch: %v", err, err)
	}

	body := principalGGUF()
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
	sourceA := untrackedSource(m2.ModelsDir, pathA)
	sourceA.Provider = "local"
	sourceB := untrackedSource(m2.ModelsDir, pathB)
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

// TestReconcileLeavesTheVerificationAxisAlone pins the authority partition from
// the other side: reconciliation hashes the file and therefore knows the
// answer, and still must not write down a verdict it does not own.
func TestReconcileLeavesTheVerificationAxisAlone(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	body := principalGGUF()
	path := writeReconcileFixture(t, root, body)
	if _, err := m.ReconcileAccept(path); err != nil {
		t.Fatal(err)
	}
	if _, err := m.Verify("org/repo"); err != nil {
		t.Fatal(err)
	}
	recorded, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	if recorded.Verification == nil {
		t.Fatal("verify recorded nothing")
	}

	if _, err = m.ReconcileAccept(path); err != nil {
		t.Fatal(err)
	}
	after, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	if after.Verification == nil || *after.Verification != *recorded.Verification {
		t.Fatalf("reconciliation disturbed the verification axis: %+v want %+v", after.Verification, recorded.Verification)
	}
}

// TestReplacingTheIntegrityDigestRetiresTheVerification is the mechanism that
// makes the single-writer rule affordable: nobody has to clear this axis,
// because a record about bytes that are no longer the recorded ones stops
// applying on its own.
func TestReplacingTheIntegrityDigestRetiresTheVerification(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	body := principalGGUF()
	path := writeReconcileFixture(t, root, body)
	if _, err := m.ReconcileAccept(path); err != nil {
		t.Fatal(err)
	}
	if _, err := m.Verify("org/repo"); err != nil {
		t.Fatal(err)
	}
	record, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	if !record.Altered() && record.Verification.Outcome != VerifyMatched {
		t.Fatalf("verification=%+v", record.Verification)
	}

	record.SetIntegrity("0000000000000000000000000000000000000000000000000000000000000000", "2026-01-01T00:00:00Z")
	if _, applies := record.AppliedVerification(); applies {
		t.Fatal("a verification naming a digest the artifact no longer carries must not apply")
	}
	if record.Altered() {
		t.Fatal("a retired verification must read as unverified, not as a verdict about the new digest")
	}
}
