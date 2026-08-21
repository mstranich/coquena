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
		SchemaVersion: RecordSchemaVersion,
		Source:        Source{Provider: "huggingface", Repository: repository, RequestedRevision: "main", ResolvedRevision: "abc", Filename: filename, URL: "https://example.test/" + filename},
		Artifact:      Artifact{SizeBytes: int64(len(body))},
	}
	classification, err := classifyFile(path)
	if err != nil {
		t.Fatal(err)
	}
	record.SetDerivation(Derivation{Version: DerivationVersion, Classification: classification})
	digest, err := hashFile(path)
	if err != nil {
		t.Fatal(err)
	}
	record.SetIntegrity(digest, "2026-01-01T00:00:00Z")
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
	if entry.State != StateLocal {
		t.Fatalf("an imported artifact is in state %q, not local", entry.State)
	}
}

// A companion is eligible for a preset whatever the library holds beside it.
// It cannot perform inference alone, but neither can any companion, and which
// principal it accompanies is the preset's decision rather than an accident of
// what is installed at the moment the library is read.
func TestUsabilityReportsCompanionWithoutItsPrincipalUsable(t *testing.T) {
	models := t.TempDir()
	installArtifact(t, models, "org/repo/mmproj.gguf", projectorGGUF())
	entry := entriesByFilename(t, models)["mmproj.gguf"]
	if !entry.Usability.Usable {
		t.Fatalf("usability=%+v", entry.Usability)
	}
	if !strings.Contains(entry.Usability.Advice, "principal") {
		t.Fatalf("the absent principal should still be reported as advice: %q", entry.Usability.Advice)
	}
}

// The compatibility computation survives A6 — only its consequence changed. An
// installed principal whose embedding length disagrees with the projector's
// projection dimension does not satisfy the criterion, so the advice stands.
func TestUsabilityAdvisesWhenNoInstalledPrincipalIsCompatible(t *testing.T) {
	models := t.TempDir()
	installArtifact(t, models, "org/repo/mmproj.gguf", projectorGGUF())
	installArtifact(t, models, "org/other/m.gguf", ggufWith("general.type", "model", "general.architecture", "gemma4", "gemma4.embedding_length", uint32(3840)))
	entry := entriesByFilename(t, models)["mmproj.gguf"]
	if !entry.Usability.Usable {
		t.Fatalf("an incompatible principal does not make a companion ineligible: %+v", entry.Usability)
	}
	if entry.Usability.Advice == "" {
		t.Fatal("the criterion is a comparison, not the mere presence of some model; the advice should stand")
	}
}

// The ordering accident A6 names: an operator who imports a companion first,
// meaning to fetch its principal next, must not be told the artifact they just
// imported is unusable.
func TestUsabilityAdmitsACompanionImportedBeforeItsPrincipal(t *testing.T) {
	models := t.TempDir()
	installArtifact(t, models, "org/repo/mmproj.gguf", projectorGGUF())
	if entry := entriesByFilename(t, models)["mmproj.gguf"]; !entry.Usability.Usable {
		t.Fatalf("the companion is unusable the moment it is imported: %+v", entry.Usability)
	}
	installArtifact(t, models, "org/repo/m.gguf", principalGGUF())
	if entry := entriesByFilename(t, models)["mmproj.gguf"]; !entry.Usability.Usable {
		t.Fatalf("the companion stopped being usable once its principal arrived: %+v", entry.Usability)
	}
}

// An artifact with no valid sidecar is untracked, and untracked is unusable.
// The state is the whole answer: no separate reason is reported, because there
// is nothing to say that the state does not already say.
func TestUsabilityReportsUntrackedArtifactUnusable(t *testing.T) {
	models := t.TempDir()
	writeArtifact(t, models, "org/repo/m.gguf", principalGGUF())
	entry := entriesByFilename(t, models)["m.gguf"]
	if entry.State != StateUntracked {
		t.Fatalf("state=%q want untracked", entry.State)
	}
	if entry.Usability.Usable {
		t.Fatalf("an untracked artifact was reported usable: %+v", entry.Usability)
	}
}

// An untracked projector satisfies both the untracked and the orphaned test.
// Management governs: adopting it is what the operator must do first, and the
// artifact would be unusable whatever principals surrounded it.
func TestUsabilityPrefersUntrackedOverOrphanedForAForeignProjector(t *testing.T) {
	models := t.TempDir()
	writeArtifact(t, models, "org/repo/mmproj.gguf", projectorGGUF())
	entry := entriesByFilename(t, models)["mmproj.gguf"]
	if entry.State != StateUntracked || entry.Usability.Usable {
		t.Fatalf("state=%q usability=%+v", entry.State, entry.Usability)
	}
}

// An artifact Coquena cannot type never becomes a managed artifact at all.
//
// This replaces a test that installed a broken artifact and expected it to be
// listed as unusable for lacking a type. That state cannot be reached any more:
// publication refuses it, so the file keeps no sidecar. It stays in the library
// as an unmanaged file, which is what the operator sees and what a preset cannot
// bind.
func TestPublicationRefusesAnArtifactItCannotClassify(t *testing.T) {
	models := t.TempDir()
	path := writeArtifact(t, models, "org/repo/broken.gguf", []byte("not a gguf at all"))
	partial := filepath.Join(t.TempDir(), "partial")
	if err := os.WriteFile(partial, []byte("not a gguf at all"), 0600); err != nil {
		t.Fatal(err)
	}
	source := Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "broken.gguf", URL: "https://example.test/broken.gguf"}
	if _, err := Publish(filepath.Join(t.TempDir(), "models"), partial, Record{Source: source}); err == nil {
		t.Fatal("publication accepted an artifact it could not classify")
	}
	sidecar := strings.TrimSuffix(path, filepath.Ext(path)) + ".json"
	if _, err := os.Stat(sidecar); !os.IsNotExist(err) {
		t.Fatalf("a refused artifact kept a sidecar: %v", err)
	}
	entry := entriesByFilename(t, models)["broken.gguf"]
	if entry.Usability.Usable {
		t.Fatalf("an unmanaged artifact was reported usable: %+v", entry.Usability)
	}
}

// An untracked principal grants nothing: it is excluded from binding, so it
// does not satisfy the companion criterion and the advice stands.
func TestUsabilityIgnoresUnreconciledPrincipalWhenAdvisingOnAProjector(t *testing.T) {
	models := t.TempDir()
	installArtifact(t, models, "org/repo/mmproj.gguf", projectorGGUF())
	writeArtifact(t, models, "org/other/m.gguf", principalGGUF())
	entry := entriesByFilename(t, models)["mmproj.gguf"]
	if entry.Usability.Advice == "" {
		t.Fatalf("an unreconciled principal satisfied the criterion: %+v", entry.Usability)
	}
}

// TestUsabilityIsDerivedNotPersisted is the decision's proof. The advice on a
// companion disappears the moment a compatible principal is installed beside
// it, and its own sidecar — the only place a persisted answer could live — is
// untouched by that change. A stored answer would still carry the advice here.
func TestUsabilityIsDerivedNotPersisted(t *testing.T) {
	models := t.TempDir()
	projectorPath := installArtifact(t, models, "org/repo/mmproj.gguf", projectorGGUF())
	if entry := entriesByFilename(t, models)["mmproj.gguf"]; entry.Usability.Advice == "" {
		t.Fatalf("a companion with no compatible principal should carry advice: %+v", entry.Usability)
	}
	projectorSidecar := strings.TrimSuffix(projectorPath, ".gguf") + ".json"
	before := treeSnapshot(t, models)
	beforeSidecar, err := os.ReadFile(projectorSidecar)
	if err != nil {
		t.Fatal(err)
	}

	installArtifact(t, models, "org/repo/m.gguf", principalGGUF())

	entry := entriesByFilename(t, models)["mmproj.gguf"]
	if !entry.Usability.Usable || entry.Usability.Advice != "" {
		t.Fatalf("the advice survived a compatible principal arriving: %+v", entry.Usability)
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
