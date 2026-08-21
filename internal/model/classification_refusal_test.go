package model

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"testing"
)

// A repair that can no longer classify the artifact fails without changing the
// derivation it was asked to refresh.
//
// It does not leave the sidecar byte-identical, and should not: `verify` records
// the outcome of the hash comparison before it attempts any repair, and that
// comparison genuinely happened. Rolling it back would discard a real
// observation to keep a file unchanged.
//
// Reaching this needs a sidecar whose integrity digest is the digest of bytes
// that are not classifiable, which publication now refuses to create. The record
// is therefore written by hand: the point under test is what repair does when it
// meets one, not how one could come to exist.
func TestRepairFailsAndLeavesTheSidecarUnchangedWhenClassificationFails(t *testing.T) {
	models := t.TempDir()
	body := []byte("this is not a GGUF file at all")
	path := writeArtifact(t, models, "org/repo/m.gguf", body)
	digest := sha256.Sum256(body)
	record := Record{
		SchemaVersion: RecordSchemaVersion,
		Source:        Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: "https://example.test/m.gguf"},
		Artifact:      Artifact{SizeBytes: int64(len(body))},
	}
	// A derivation at version zero is stale by construction, which is what makes
	// repair attempt to recompute it.
	record.SetDerivation(Derivation{Version: 0, Classification: Classification{Type: TypeModel}})
	record.SetIntegrity(hex.EncodeToString(digest[:]), "2026-01-01T00:00:00Z")
	data, err := json.Marshal(record)
	if err != nil {
		t.Fatal(err)
	}
	sidecar := strings.TrimSuffix(path, filepath.Ext(path)) + ".json"
	if err = os.WriteFile(sidecar, append(data, '\n'), 0600); err != nil {
		t.Fatal(err)
	}
	if err = os.WriteFile(strings.TrimSuffix(path, filepath.Ext(path))+".revision", []byte("abc\n"), 0600); err != nil {
		t.Fatal(err)
	}
	m := NewManager(models)
	if _, err = m.VerifyWithOptions("org/repo:m.gguf", VerifyOptions{RepairDerived: true}); err == nil {
		t.Fatal("repair accepted an artifact it could not classify")
	}

	// The verification axis is expected to have changed: the hash comparison did
	// happen, and recording it is what verify is for. What a failed repair must
	// not do is leave a half-written derivation behind.
	after, err := ReadRecord(path)
	if err != nil {
		t.Fatalf("a failed repair left an unreadable sidecar: %v", err)
	}
	if after.Derivation.Version != 0 {
		t.Fatalf("a failed repair advanced the derivation to version %d", after.Derivation.Version)
	}
	if after.Derivation.Classification.Type != TypeModel {
		t.Fatalf("a failed repair overwrote the recorded classification: %+v", after.Derivation.Classification)
	}
	if after.Verification == nil {
		t.Fatal("the verification that did run was not recorded")
	}

}

// Enumeration still tolerates what publication refuses. A file that cannot be
// classified has no sidecar, so it is listed as an unmanaged artifact rather
// than hiding the rest of the library.
func TestEnumerationListsAFileItCannotClassify(t *testing.T) {
	models := t.TempDir()
	writeArtifact(t, models, "org/repo/broken.gguf", []byte("not a gguf"))
	installArtifact(t, models, "org/repo/good.gguf", principalGGUF())

	m := NewManager(models)
	entries, err := m.Library()
	if err != nil {
		t.Fatalf("one unclassifiable file failed the whole walk: %v", err)
	}
	found := map[string]bool{}
	for _, entry := range entries {
		found[entry.Source.Filename] = true
	}
	if !found["broken.gguf"] || !found["good.gguf"] {
		t.Fatalf("enumeration dropped an artifact: %+v", found)
	}
}

// The Gemma 4 witness: the principal's bounded remote inspection fails while the
// repository listing succeeds. The artifact must be reported as refused, not
// quietly reclassified as unknown and dropped from the target set.
func TestReconcileRepositoryRefusesACandidateItCannotInspect(t *testing.T) {
	files := map[string][]byte{"principal-Q4_K_M.gguf": principalGGUF(), "mmproj-BF16.gguf": projectorGGUF()}
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path == "/api/models/org/repo/revision/main" {
			var siblings []string
			for _, name := range []string{"mmproj-BF16.gguf", "principal-Q4_K_M.gguf"} {
				digest := sha256.Sum256(files[name])
				siblings = append(siblings, fmt.Sprintf(`{"rfilename":%q,"size":%d,"lfs":{"sha256":%q}}`, name, len(files[name]), hex.EncodeToString(digest[:])))
			}
			_, _ = fmt.Fprintf(w, `{"sha":"abc","siblings":[%s]}`, strings.Join(siblings, ","))
			return
		}
		name := strings.TrimPrefix(r.URL.Path, "/org/repo/resolve/abc/")
		body, ok := files[name]
		if !ok {
			http.NotFound(w, r)
			return
		}
		// The principal's header cannot be read: exactly the failure the witness
		// describes, where the content hash is available but the bounded range
		// inspection through the CDN does not answer.
		if name == "principal-Q4_K_M.gguf" && r.Header.Get("Range") != "" {
			http.Error(w, "range unavailable", http.StatusInternalServerError)
			return
		}
		w.Header().Set("Content-Length", strconv.Itoa(len(body)))
		if r.Method == http.MethodHead {
			return
		}
		_, _ = w.Write(body)
	}))
	t.Cleanup(server.Close)
	m := NewManager(filepath.Join(t.TempDir(), "models"))
	m.Client = server.Client()
	m.Resolver = Resolver{Client: server.Client(), BaseURL: server.URL}
	principal := writeArtifact(t, m.ModelsDir, "org/repo/principal-Q4_K_M.gguf", files["principal-Q4_K_M.gguf"])

	result, err := m.ReconcileRepository("org/repo:Q4_K_M")
	if err != nil {
		t.Fatal(err)
	}
	artifact, ok := artifactFor(result, "principal-Q4_K_M.gguf")
	if !ok {
		t.Fatal("the uninspectable artifact vanished from the reconciliation instead of being reported")
	}
	if artifact.Outcome != RepositoryRefused {
		t.Fatalf("artifact=%+v want refused", artifact)
	}
	if !strings.Contains(artifact.Diagnostic, "could not inspect") {
		t.Fatalf("diagnostic does not name the inspection failure: %q", artifact.Diagnostic)
	}
	if sidecarExists(principal) {
		t.Fatal("a sidecar was written for an artifact that could not be classified")
	}
}
