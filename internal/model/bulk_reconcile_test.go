package model

import (
	"encoding/binary"
	"encoding/json"
	"os"
	"path/filepath"
	"testing"
)

// ggufWith builds a minimal GGUF header carrying the given keys in order.
// Values are either a string or a uint32, which covers every key the
// classification digest reads in these tests.
func ggufWith(pairs ...any) []byte {
	b := make([]byte, 24)
	copy(b, "GGUF")
	binary.LittleEndian.PutUint32(b[4:], 3)
	binary.LittleEndian.PutUint64(b[16:], uint64(len(pairs)/2))
	for i := 0; i < len(pairs); i += 2 {
		b = appendString(b, pairs[i].(string))
		switch value := pairs[i+1].(type) {
		case string:
			b = appendType(b, 8)
			b = appendString(b, value)
		case uint32:
			b = appendType(b, 4)
			var n [4]byte
			binary.LittleEndian.PutUint32(n[:], value)
			b = append(b, n[:]...)
		}
	}
	return b
}

func principalGGUF() []byte {
	return ggufWith("general.type", "model", "general.architecture", "qwen35", "qwen35.embedding_length", uint32(5120))
}

func projectorGGUF() []byte {
	return ggufWith("general.type", "mmproj", "general.architecture", "clip", "clip.vision.projection_dim", uint32(5120), "clip.vision.embedding_length", uint32(1152))
}

// writeArtifact places body at modelsDir/<relative path>, creating the parents.
func writeArtifact(t *testing.T, modelsDir, rel string, body []byte) string {
	t.Helper()
	path := filepath.Join(modelsDir, filepath.FromSlash(rel))
	if err := os.MkdirAll(filepath.Dir(path), 0755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path, body, 0600); err != nil {
		t.Fatal(err)
	}
	return path
}

// writePreviousSchemaSidecar writes a schema version 1 sidecar pair, the shape
// every sidecar written before this change carries. Reading it fails, so its
// artifact enumerates as unreconciled and the bulk pass is its recovery route.
func writePreviousSchemaSidecar(t *testing.T, path string, source Source) {
	t.Helper()
	base := path[:len(path)-len(filepath.Ext(path))]
	data, err := json.Marshal(map[string]any{"schemaVersion": 1, "source": source, "artifact": Artifact{SizeBytes: 24}, "gguf": map[string]any{"general.type": "model"}})
	if err != nil {
		t.Fatal(err)
	}
	if err = os.WriteFile(base+".json", append(data, '\n'), 0600); err != nil {
		t.Fatal(err)
	}
	if err = os.WriteFile(base+".revision", []byte(source.ResolvedRevision+"\n"), 0600); err != nil {
		t.Fatal(err)
	}
}

func entryFor(result BulkReconcileResult, path string) (BulkReconcileEntry, bool) {
	for _, entry := range result.Entries {
		if entry.Path == filepath.Clean(path) {
			return entry, true
		}
	}
	return BulkReconcileEntry{}, false
}

func TestReconcileAllMixesInstalledPreviousSchemaAndForeignArtifacts(t *testing.T) {
	models := filepath.Join(t.TempDir(), "models")
	m := NewManager(models)

	installed := writeArtifact(t, models, "org/installed/m.gguf", principalGGUF())
	if _, err := m.ReconcileAccept(installed); err != nil {
		t.Fatal(err)
	}
	previous := writeArtifact(t, models, "org/previous/p.gguf", principalGGUF())
	writePreviousSchemaSidecar(t, previous, Source{Provider: "local", Repository: "org/previous", Filename: "p.gguf"})
	foreign := writeArtifact(t, models, "lmstudio/foreign/f.gguf", projectorGGUF())

	result, err := m.ReconcileAll(ReconcileModeAccept)
	if err != nil {
		t.Fatal(err)
	}
	if len(result.Entries) != 3 {
		t.Fatalf("entries=%+v", result.Entries)
	}
	if entry, ok := entryFor(result, installed); !ok || entry.Outcome != OutcomeSkipped {
		t.Fatalf("installed entry=%+v ok=%v want skipped", entry, ok)
	} else if entry.Classification.Type != TypeModel {
		t.Fatalf("a skipped entry must still contribute its classification: %+v", entry.Classification)
	}
	for _, path := range []string{previous, foreign} {
		entry, ok := entryFor(result, path)
		if !ok || entry.Outcome != OutcomeReconciled {
			t.Fatalf("entry for %s = %+v ok=%v want reconciled", path, entry, ok)
		}
		record, readErr := ReadRecord(path)
		if readErr != nil {
			t.Fatalf("sidecar for %s unreadable after the pass: %v", path, readErr)
		}
		if record.SchemaVersion != RecordSchemaVersion {
			t.Fatalf("schemaVersion=%d", record.SchemaVersion)
		}
	}
	if entry, _ := entryFor(result, foreign); entry.Classification.Type != TypeProjector {
		t.Fatalf("foreign projector classification=%+v", entry.Classification)
	}
}

func TestReconcileAllContinuesPastOneFailure(t *testing.T) {
	models := filepath.Join(t.TempDir(), "models")
	m := NewManager(models)

	// A .gguf nested one level deeper than the <publisher>/<repo>/<file>
	// shape derives to a different library location, which reconcile refuses.
	broken := writeArtifact(t, models, "org/repo/extra/bad.gguf", principalGGUF())
	good := writeArtifact(t, models, "org/repo/good.gguf", principalGGUF())

	result, err := m.ReconcileAll(ReconcileModeAccept)
	if err != nil {
		t.Fatalf("a per-artifact failure must not abandon the pass: %v", err)
	}
	failed, ok := entryFor(result, broken)
	if !ok || failed.Outcome != OutcomeFailed {
		t.Fatalf("broken entry=%+v ok=%v want failed", failed, ok)
	}
	if failed.Diagnostic == "" {
		t.Fatal("a failed artifact must carry a diagnostic")
	}
	if entry, ok := entryFor(result, good); !ok || entry.Outcome != OutcomeReconciled {
		t.Fatalf("good entry=%+v ok=%v want reconciled", entry, ok)
	}
	if _, err = ReadRecord(good); err != nil {
		t.Fatalf("the remaining artifact should have been reconciled: %v", err)
	}
}

func TestReconcileAllReportsComputedCompanionRelations(t *testing.T) {
	models := filepath.Join(t.TempDir(), "models")
	m := NewManager(models)

	principal := writeArtifact(t, models, "org/repo/principal.gguf", principalGGUF())
	projector := writeArtifact(t, models, "org/repo/mmproj.gguf", projectorGGUF())
	unrelated := writeArtifact(t, models, "org/other/other.gguf", ggufWith("general.type", "mmproj", "general.architecture", "clip", "clip.projection_dim", uint32(999)))

	result, err := m.ReconcileAll(ReconcileModeAccept)
	if err != nil {
		t.Fatal(err)
	}
	if len(result.Relations) != 1 {
		t.Fatalf("relations=%+v", result.Relations)
	}
	relation := result.Relations[0]
	if relation.Role != RoleProjector || relation.PrincipalPath != filepath.Clean(principal) || relation.CompanionPath != filepath.Clean(projector) {
		t.Fatalf("relation=%+v", relation)
	}
	if entry, ok := entryFor(result, unrelated); !ok || entry.Outcome != OutcomeReconciled {
		t.Fatalf("an artifact matching no principal must still be reported: %+v ok=%v", entry, ok)
	}
}

func TestReconcileAllRequiresExactlyOneMode(t *testing.T) {
	models := filepath.Join(t.TempDir(), "models")
	m := NewManager(models)
	writeArtifact(t, models, "org/repo/m.gguf", principalGGUF())

	if _, err := m.ReconcileAll(ReconcileMode("")); err == nil {
		t.Fatal("bulk mode must still require exactly one of --online/--accept")
	}
	if _, err := ReadRecord(filepath.Join(models, "org", "repo", "m.gguf")); err == nil {
		t.Fatal("a rejected invocation must write no sidecar")
	}
}

// TestListingDoesNotTriggerReconciliation pins that reconciliation stays an
// explicit operator action: enumerating the library must leave an unreconciled
// artifact unreconciled, never writing a sidecar as a side effect.
func TestListingDoesNotTriggerReconciliation(t *testing.T) {
	models := filepath.Join(t.TempDir(), "models")
	m := NewManager(models)
	path := writeArtifact(t, models, "org/repo/m.gguf", principalGGUF())

	for range 2 {
		entries, err := m.Library()
		if err != nil {
			t.Fatal(err)
		}
		if len(entries) != 1 || entries[0].State != "unreconciled" {
			t.Fatalf("entries=%+v", entries)
		}
	}
	if _, err := m.List(); err != nil {
		t.Fatal(err)
	}
	base := path[:len(path)-len(filepath.Ext(path))]
	for _, sidecar := range []string{base + ".json", base + ".revision"} {
		if _, err := os.Stat(sidecar); !os.IsNotExist(err) {
			t.Fatalf("listing wrote %s as a side effect: %v", sidecar, err)
		}
	}
}
