package model

import (
	"encoding/json"
	"maps"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// installArtifact writes body at rel together with a valid sidecar pair, so the
// artifact enumerates as installed. The classification recorded is the one read
// from the body itself, exactly as publication derives it.
func installArtifact(t *testing.T, modelsDir, rel string, body []byte) string {
	t.Helper()
	path := writeArtifact(t, modelsDir, rel, body)
	slashed := filepath.ToSlash(rel)
	parts := strings.Split(slashed, "/")
	filename := parts[len(parts)-1]
	repository := strings.Join(parts[:len(parts)-1], "/")
	record := Record{
		SchemaVersion:  RecordSchemaVersion,
		Source:         Source{Provider: "huggingface", Repository: repository, RequestedRevision: "main", ResolvedRevision: "abc", Filename: filename, URL: "https://example.test/" + filename},
		Artifact:       Artifact{SizeBytes: int64(len(body))},
		Classification: classifyFile(path),
	}
	data, err := json.Marshal(record)
	if err != nil {
		t.Fatal(err)
	}
	base := strings.TrimSuffix(path, filepath.Ext(path))
	if err = os.WriteFile(base+".json", append(data, '\n'), 0600); err != nil {
		t.Fatal(err)
	}
	if err = os.WriteFile(base+".revision", []byte("abc\n"), 0600); err != nil {
		t.Fatal(err)
	}
	return path
}

// entriesByFilename indexes an enumerated library for assertion.
func entriesByFilename(t *testing.T, models string) map[string]LibraryEntry {
	t.Helper()
	entries, err := NewManager(models).Library()
	if err != nil {
		t.Fatal(err)
	}
	byName := map[string]LibraryEntry{}
	for _, entry := range entries {
		byName[entry.Source.Filename] = entry
	}
	return byName
}

// A reconciled principal is usable, and stays usable with no projector beside
// it: a projector adds vision rather than enabling inference, so refusing a
// text-only model would report the ordinary library as broken.
func TestUsabilityReportsReconciledPrincipalUsableWithoutAProjector(t *testing.T) {
	models := t.TempDir()
	installArtifact(t, models, "org/repo/m.gguf", principalGGUF())
	entry := entriesByFilename(t, models)["m.gguf"]
	if !entry.Usability.Usable {
		t.Fatalf("usability=%+v", entry.Usability)
	}
	if entry.Usability.Reason != "" || entry.Usability.Detail != "" {
		t.Fatalf("a usable artifact carries a reason: %+v", entry.Usability)
	}
}

// A projector alone is never independently usable: it has no principal to
// accompany.
func TestUsabilityReportsOrphanedProjectorUnusable(t *testing.T) {
	models := t.TempDir()
	installArtifact(t, models, "org/repo/mmproj.gguf", projectorGGUF())
	entry := entriesByFilename(t, models)["mmproj.gguf"]
	if entry.Usability.Usable || entry.Usability.Reason != ReasonOrphanedProjector {
		t.Fatalf("usability=%+v", entry.Usability)
	}
	if !strings.Contains(entry.Usability.Detail, "principal") {
		t.Fatalf("the reason does not name the absent principal: %q", entry.Usability.Detail)
	}
}

// An installed principal whose embedding length disagrees with the projector's
// projection dimension does not rescue it: the criterion is a comparison, not
// the mere presence of some model.
func TestUsabilityRefusesProjectorAgainstIncompatiblePrincipal(t *testing.T) {
	models := t.TempDir()
	installArtifact(t, models, "org/repo/mmproj.gguf", projectorGGUF())
	installArtifact(t, models, "org/other/m.gguf", ggufWith("general.type", "model", "general.architecture", "gemma4", "gemma4.embedding_length", uint32(3840)))
	entry := entriesByFilename(t, models)["mmproj.gguf"]
	if entry.Usability.Usable || entry.Usability.Reason != ReasonOrphanedProjector {
		t.Fatalf("usability=%+v", entry.Usability)
	}
}

// An artifact with no valid sidecar is unusable, and reconciliation is the
// reason reported even though its declared type is known.
func TestUsabilityReportsUnreconciledArtifactUnusable(t *testing.T) {
	models := t.TempDir()
	writeArtifact(t, models, "org/repo/m.gguf", principalGGUF())
	entry := entriesByFilename(t, models)["m.gguf"]
	if entry.Usability.Usable || entry.Usability.Reason != ReasonUnreconciled {
		t.Fatalf("usability=%+v", entry.Usability)
	}
	if !strings.Contains(entry.Usability.Detail, "reconcile") {
		t.Fatalf("the reason does not name reconciliation: %q", entry.Usability.Detail)
	}
}

// An unreconciled projector satisfies both the unreconciled and the orphaned
// test. Reconciliation governs: it is what the operator must do first, and the
// artifact would be unusable whatever principals surrounded it.
func TestUsabilityPrefersUnreconciledOverOrphanedForAForeignProjector(t *testing.T) {
	models := t.TempDir()
	writeArtifact(t, models, "org/repo/mmproj.gguf", projectorGGUF())
	entry := entriesByFilename(t, models)["mmproj.gguf"]
	if entry.Usability.Reason != ReasonUnreconciled {
		t.Fatalf("usability=%+v", entry.Usability)
	}
}

// An artifact Coquena cannot type is unusable rather than listed untyped.
func TestUsabilityReportsUnclassifiableArtifactUnusable(t *testing.T) {
	models := t.TempDir()
	installArtifact(t, models, "org/repo/broken.gguf", []byte("not a gguf at all"))
	entry := entriesByFilename(t, models)["broken.gguf"]
	if entry.Usability.Usable || entry.Usability.Reason != ReasonUnclassifiable {
		t.Fatalf("usability=%+v", entry.Usability)
	}
}

// An unreconciled principal grants nothing: it is excluded from binding, so it
// cannot make a projector usable either.
func TestUsabilityIgnoresUnreconciledPrincipalWhenAdmittingAProjector(t *testing.T) {
	models := t.TempDir()
	installArtifact(t, models, "org/repo/mmproj.gguf", projectorGGUF())
	writeArtifact(t, models, "org/other/m.gguf", principalGGUF())
	entry := entriesByFilename(t, models)["mmproj.gguf"]
	if entry.Usability.Usable {
		t.Fatalf("an unreconciled principal made a projector usable: %+v", entry.Usability)
	}
}

// TestUsabilityIsDerivedNotPersisted is the decision's proof. An orphaned
// projector becomes usable the moment a compatible principal is installed
// beside it, and its own sidecar — the only place a persisted answer could
// live — is untouched by that change. A stored usability would still read
// unusable here.
func TestUsabilityIsDerivedNotPersisted(t *testing.T) {
	models := t.TempDir()
	projectorPath := installArtifact(t, models, "org/repo/mmproj.gguf", projectorGGUF())
	if entry := entriesByFilename(t, models)["mmproj.gguf"]; entry.Usability.Usable {
		t.Fatalf("an orphaned projector reported usable: %+v", entry.Usability)
	}
	projectorSidecar := strings.TrimSuffix(projectorPath, ".gguf") + ".json"
	before := treeSnapshot(t, models)
	beforeSidecar, err := os.ReadFile(projectorSidecar)
	if err != nil {
		t.Fatal(err)
	}

	installArtifact(t, models, "org/repo/m.gguf", principalGGUF())

	entry := entriesByFilename(t, models)["mmproj.gguf"]
	if !entry.Usability.Usable || entry.Usability.Reason != "" {
		t.Fatalf("the projector stayed unusable after a compatible principal arrived: %+v", entry.Usability)
	}
	afterSidecar, err := os.ReadFile(projectorSidecar)
	if err != nil {
		t.Fatal(err)
	}
	if string(beforeSidecar) != string(afterSidecar) {
		t.Fatal("the projector's sidecar was rewritten to record its new usability")
	}
	// Nothing that existed before was rewritten either: the only difference is
	// the principal's own three files.
	after := treeSnapshot(t, models)
	for path, size := range before {
		if afterSize, ok := after[path]; !ok || afterSize != size {
			t.Fatalf("enumeration rewrote %s", path)
		}
	}
	// And re-reading changes nothing at all.
	stable := treeSnapshot(t, models)
	if _, err = NewManager(models).Library(); err != nil {
		t.Fatal(err)
	}
	if !maps.Equal(stable, treeSnapshot(t, models)) {
		t.Fatal("deriving usability wrote to the models directory")
	}
}
