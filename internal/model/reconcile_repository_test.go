package model

import (
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
	"testing"
)

// repositoryFixture serves the Hugging Face surface a repository-driven
// reconciliation reads: the revision listing with each sibling's content
// SHA-256, and
// each artifact by HEAD and byte range so its header can be classified without
// the whole file being transferred. sha256s overrides the reported content hash
// for a filename, which is how a digest disagreement is staged.
func repositoryFixture(t *testing.T, files map[string][]byte, sha256s map[string]string) *Manager {
	t.Helper()
	names := make([]string, 0, len(files))
	for name := range files {
		names = append(names, name)
	}
	sort.Strings(names)
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path == "/api/models/org/repo/revision/main" {
			siblings := make([]string, 0, len(names))
			for _, name := range names {
				contentSHA256, ok := sha256s[name]
				if !ok {
					digest := sha256.Sum256(files[name])
					contentSHA256 = hex.EncodeToString(digest[:])
				}
				lfs := ""
				if contentSHA256 != "" {
					lfs = fmt.Sprintf(`,"lfs":{"sha256":%q}`, contentSHA256)
				}
				siblings = append(siblings, fmt.Sprintf(`{"rfilename":%q,"size":%d%s}`, name, len(files[name]), lfs))
			}
			_, _ = fmt.Fprintf(w, `{"sha":"abc","siblings":[%s]}`, strings.Join(siblings, ","))
			return
		}
		body, ok := files[strings.TrimPrefix(r.URL.Path, "/org/repo/resolve/abc/")]
		if !ok {
			http.NotFound(w, r)
			return
		}
		w.Header().Set("Content-Length", strconv.Itoa(len(body)))
		if r.Method == http.MethodHead {
			return
		}
		if header := r.Header.Get("Range"); header != "" {
			var start, end int
			if _, err := fmt.Sscanf(strings.TrimPrefix(header, "bytes="), "%d-%d", &start, &end); err != nil {
				t.Fatal(err)
			}
			if end >= len(body) {
				end = len(body) - 1
			}
			w.Header().Set("Content-Length", strconv.Itoa(end-start+1))
			w.WriteHeader(http.StatusPartialContent)
			_, _ = w.Write(body[start : end+1])
			return
		}
		_, _ = w.Write(body)
	}))
	t.Cleanup(server.Close)
	m := NewManager(filepath.Join(t.TempDir(), "models"))
	m.Client = server.Client()
	m.Resolver = Resolver{Client: server.Client(), BaseURL: server.URL}
	return m
}

func artifactFor(result RepositoryReconciliation, filename string) (RepositoryArtifact, bool) {
	for _, artifact := range result.Artifacts {
		if artifact.Filename == filename {
			return artifact, true
		}
	}
	return RepositoryArtifact{}, false
}

func sidecarExists(path string) bool {
	base := path[:len(path)-len(filepath.Ext(path))]
	_, err := os.Stat(base + ".json")
	return err == nil
}

// A repository holding a principal and its projector reconciles both in one
// action: they are parts of one logical unit, and no quant selector would ever
// name the projector.
func TestReconcileRepositoryReconcilesPrincipalWithCompanion(t *testing.T) {
	files := map[string][]byte{"principal-Q4_K_M.gguf": principalGGUF(), "mmproj-BF16.gguf": projectorGGUF()}
	m := repositoryFixture(t, files, nil)
	principal := writeArtifact(t, m.ModelsDir, "org/repo/principal-Q4_K_M.gguf", files["principal-Q4_K_M.gguf"])
	projector := writeArtifact(t, m.ModelsDir, "org/repo/mmproj-BF16.gguf", files["mmproj-BF16.gguf"])

	result, err := m.ReconcileRepository("org/repo:Q4_K_M")
	if err != nil {
		t.Fatal(err)
	}
	for name, path := range map[string]string{"principal-Q4_K_M.gguf": principal, "mmproj-BF16.gguf": projector} {
		artifact, ok := artifactFor(result, name)
		if !ok || artifact.Outcome != RepositoryReconciled {
			t.Fatalf("artifact %s = %+v ok=%v want reconciled", name, artifact, ok)
		}
		if !sidecarExists(path) {
			t.Fatalf("no sidecar written for %s", name)
		}
	}
	if artifact, _ := artifactFor(result, "mmproj-BF16.gguf"); artifact.Classification.Type != TypeProjector {
		t.Fatalf("companion classification=%+v", artifact.Classification)
	}
	entries, err := m.Library()
	if err != nil {
		t.Fatal(err)
	}
	for _, entry := range entries {
		if entry.State != "installed" {
			t.Fatalf("entry %s left in state %q", entry.Source.Filename, entry.State)
		}
	}
}

// Two principals on disk are a choice between quantizations, which is the
// operator's to make: the reference is refused and both are named.
func TestReconcileRepositoryRefusesSeveralPrincipals(t *testing.T) {
	files := map[string][]byte{
		"principal-Q4_K_M.gguf": principalGGUF(),
		"principal-Q8_0.gguf":   principalGGUF(),
	}
	m := repositoryFixture(t, files, nil)
	q4 := writeArtifact(t, m.ModelsDir, "org/repo/principal-Q4_K_M.gguf", files["principal-Q4_K_M.gguf"])
	q8 := writeArtifact(t, m.ModelsDir, "org/repo/principal-Q8_0.gguf", files["principal-Q8_0.gguf"])

	_, err := m.ReconcileRepository("org/repo")
	if err == nil {
		t.Fatal("expected an ambiguity refusal")
	}
	ambiguous, ok := err.(*ErrAmbiguousPrincipals)
	if !ok {
		t.Fatalf("err=%T %v", err, err)
	}
	if len(ambiguous.Matches) != 2 {
		t.Fatalf("matches=%v", ambiguous.Matches)
	}
	for _, name := range []string{"principal-Q4_K_M.gguf", "principal-Q8_0.gguf"} {
		if !strings.Contains(err.Error(), name) {
			t.Fatalf("diagnostic does not name %s: %s", name, err.Error())
		}
	}
	for _, path := range []string{q4, q8} {
		if sidecarExists(path) {
			t.Fatalf("a refused reference must write no sidecar: %s", path)
		}
	}
	// Naming one of them resolves the ambiguity and leaves the other alone.
	result, err := m.ReconcileRepository("org/repo:Q8_0")
	if err != nil {
		t.Fatal(err)
	}
	if artifact, ok := artifactFor(result, "principal-Q8_0.gguf"); !ok || artifact.Outcome != RepositoryReconciled {
		t.Fatalf("artifact=%+v ok=%v", artifact, ok)
	}
	if sidecarExists(q4) {
		t.Fatal("the unnamed quantization must not be reconciled")
	}
}

// A companion the repository publishes but the library does not hold is
// reported rather than omitted, so an incomplete model is visible before an
// invocation fails — and the artifacts that are present still reconcile.
func TestReconcileRepositoryReportsAbsentCompanion(t *testing.T) {
	files := map[string][]byte{"principal-Q4_K_M.gguf": principalGGUF(), "mmproj-BF16.gguf": projectorGGUF()}
	m := repositoryFixture(t, files, nil)
	principal := writeArtifact(t, m.ModelsDir, "org/repo/principal-Q4_K_M.gguf", files["principal-Q4_K_M.gguf"])

	result, err := m.ReconcileRepository("org/repo:Q4_K_M")
	if err != nil {
		t.Fatal(err)
	}
	absent, ok := artifactFor(result, "mmproj-BF16.gguf")
	if !ok || absent.Outcome != RepositoryAbsent {
		t.Fatalf("companion = %+v ok=%v want absent", absent, ok)
	}
	if absent.Classification.Type != TypeProjector || absent.Diagnostic == "" {
		t.Fatalf("absent companion reported without its type or a diagnostic: %+v", absent)
	}
	if artifact, _ := artifactFor(result, "principal-Q4_K_M.gguf"); artifact.Outcome != RepositoryReconciled {
		t.Fatalf("the present artifact must still reconcile: %+v", artifact)
	}
	if !sidecarExists(principal) {
		t.Fatal("no sidecar written for the present artifact")
	}
}

// A digest disagreement reconciles to a partial result with a sidecar written:
// reconciliation compares against the repository's current revision, so an
// intact older file fails this comparison while being perfectly usable, and
// discarding what was learned would leave the operator an error instead of a
// model.
func TestReconcileRepositoryRecordsDigestDisagreement(t *testing.T) {
	files := map[string][]byte{"principal-Q4_K_M.gguf": principalGGUF()}
	m := repositoryFixture(t, files, map[string]string{"principal-Q4_K_M.gguf": strings.Repeat("a", 64)})
	principal := writeArtifact(t, m.ModelsDir, "org/repo/principal-Q4_K_M.gguf", files["principal-Q4_K_M.gguf"])

	result, err := m.ReconcileRepository("org/repo:Q4_K_M")
	if err != nil {
		t.Fatal(err)
	}
	artifact, ok := artifactFor(result, "principal-Q4_K_M.gguf")
	if !ok || !artifact.Partial {
		t.Fatalf("artifact=%+v ok=%v want a partial result", artifact, ok)
	}
	if !sidecarExists(principal) {
		t.Fatal("a disagreement still writes a sidecar; the artifact is usable")
	}
	record, err := ReadRecord(principal)
	if err != nil {
		t.Fatal(err)
	}
	if record.Attestation != nil {
		t.Fatalf("a disagreement establishes no attestation: %+v", record.Attestation)
	}
	if record.Observation == nil || record.Observation.PublishedDigest == nil {
		t.Fatalf("the identifier that disagreed should be on the observation: %+v", record.Observation)
	}
	if len(result.Refused()) != 0 {
		t.Fatalf("a disagreement is no longer a refusal: %+v", result.Refused())
	}
}

func TestReconcileRepositoryIsolatesHashlessSibling(t *testing.T) {
	files := map[string][]byte{
		"principal-Q4_K_M.gguf": principalGGUF(),
		"mmproj-BF16.gguf":      projectorGGUF(),
	}
	m := repositoryFixture(t, files, map[string]string{"mmproj-BF16.gguf": ""})
	principal := writeArtifact(t, m.ModelsDir, "org/repo/principal-Q4_K_M.gguf", files["principal-Q4_K_M.gguf"])
	projector := writeArtifact(t, m.ModelsDir, "org/repo/mmproj-BF16.gguf", files["mmproj-BF16.gguf"])

	result, err := m.ReconcileRepository("org/repo:Q4_K_M")
	if err != nil {
		t.Fatal(err)
	}
	principalResult, _ := artifactFor(result, "principal-Q4_K_M.gguf")
	projectorResult, _ := artifactFor(result, "mmproj-BF16.gguf")
	if principalResult.Outcome != RepositoryReconciled || principalResult.Partial {
		t.Fatalf("principal=%+v", principalResult)
	}
	if projectorResult.Outcome != RepositoryReconciled || !projectorResult.Partial {
		t.Fatalf("projector=%+v", projectorResult)
	}
	if !sidecarExists(principal) || !sidecarExists(projector) {
		t.Fatal("both independently reconciled artifacts must publish sidecars")
	}
}

// A local artifact the repository does not publish is reported, never adopted:
// reconciliation resolves identity against the repository, and a file it does
// not contain has none.
func TestReconcileRepositoryReportsForeignLocalArtifact(t *testing.T) {
	files := map[string][]byte{"principal-Q4_K_M.gguf": principalGGUF()}
	m := repositoryFixture(t, files, nil)
	writeArtifact(t, m.ModelsDir, "org/repo/principal-Q4_K_M.gguf", files["principal-Q4_K_M.gguf"])
	stranger := writeArtifact(t, m.ModelsDir, "org/repo/renamed-by-hand.gguf", projectorGGUF())

	result, err := m.ReconcileRepository("org/repo:Q4_K_M")
	if err != nil {
		t.Fatal(err)
	}
	artifact, ok := artifactFor(result, "renamed-by-hand.gguf")
	if !ok || artifact.Outcome != RepositoryForeign {
		t.Fatalf("artifact=%+v ok=%v want foreign", artifact, ok)
	}
	if sidecarExists(stranger) {
		t.Fatal("a foreign artifact must not be adopted")
	}
	if artifact.Classification.Type != TypeProjector {
		t.Fatalf("a foreign artifact is still reported with its declared type: %+v", artifact.Classification)
	}
}

// --accept stays a single-artifact operation with no network request: it
// asserts an identity from the path alone, so it is never applied to a set the
// operator did not name.
func TestReconcileAcceptStaysSingleArtifact(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	principal := writeArtifact(t, m.ModelsDir, "org/repo/principal-Q4_K_M.gguf", principalGGUF())
	projector := writeArtifact(t, m.ModelsDir, "org/repo/mmproj-BF16.gguf", projectorGGUF())

	entry, err := m.ResolveUnreconciled("org/repo:Q4_K_M")
	if err != nil {
		t.Fatal(err)
	}
	result, err := m.ReconcileAccept(entry.TargetPath)
	if err != nil {
		t.Fatal(err)
	}
	if result.Record.Source.Provider != "local" {
		t.Fatalf("source=%+v", result.Record.Source)
	}
	if !sidecarExists(principal) {
		t.Fatal("no sidecar written for the referenced artifact")
	}
	if sidecarExists(projector) {
		t.Fatal("--accept must not reach beyond the artifact it was given")
	}
}
