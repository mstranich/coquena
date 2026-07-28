package main

import (
	"bytes"
	"context"
	"crypto/sha256"
	"encoding/hex"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"

	modelapp "github.com/mstranich/coquena/internal/model"
)

func withModelManager(t *testing.T) *modelapp.Manager {
	t.Helper()
	s := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path == "/api/models/org/repo/revision/main" {
			_, _ = w.Write([]byte(`{"sha":"abc","siblings":[{"rfilename":"m.gguf","size":24}]}`))
			return
		}
		http.NotFound(w, r)
	}))
	t.Cleanup(s.Close)
	m := modelapp.NewManager(filepath.Join(t.TempDir(), "models"))
	m.Resolver = modelapp.Resolver{Client: s.Client(), BaseURL: s.URL}
	return m
}
func TestRunModelListEmpty(t *testing.T) {
	m := withModelManager(t)
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	var out bytes.Buffer
	if err := runModel([]string{"list", "--json"}, &out); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(out.String(), `"result":[]`) {
		t.Fatalf("out=%s", out.String())
	}
}

func TestRunModelListInstalled(t *testing.T) {
	m := withModelManager(t)
	partial := filepath.Join(t.TempDir(), "partial")
	if err := os.WriteFile(partial, []byte("bytes"), 0600); err != nil {
		t.Fatal(err)
	}
	r := modelapp.Record{Source: modelapp.Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m-Q4_K_M.gguf", URL: "https://example.test/m.gguf"}, Artifact: modelapp.Artifact{SizeBytes: 5}}
	if _, err := modelapp.Publish(m.ModelsDir, partial, r); err != nil {
		t.Fatal(err)
	}
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	var out bytes.Buffer
	if err := runModel([]string{"list"}, &out); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(out.String(), "org/repo") || !strings.Contains(out.String(), "m-Q4_K_M.gguf") {
		t.Fatalf("out=%s", out.String())
	}
}

func TestRunModelResolveJSON(t *testing.T) {
	m := withModelManager(t)
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	var out bytes.Buffer
	if err := runModel([]string{"resolve", "org/repo", "--json"}, &out); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(out.String(), `"schemaVersion":1`) || !strings.Contains(out.String(), `"resolvedRevision":"abc"`) {
		t.Fatalf("out=%s", out.String())
	}
}
func TestRunModelRejectsInvalidDownloadReference(t *testing.T) {
	m := withModelManager(t)
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	if err := runModel([]string{"download", "not-a-repository"}, &bytes.Buffer{}); err == nil || !strings.Contains(err.Error(), "repository must") {
		t.Fatalf("err=%v", err)
	}
}
func TestRunDownloadRejectsMalformedInput(t *testing.T) {
	if err := runDownload([]string{"status"}, &bytes.Buffer{}); err == nil {
		t.Fatal("input accepted")
	}
}
func TestRunModelRevisionJSON(t *testing.T) {
	root := t.TempDir()
	source := modelapp.Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: "https://example.test"}
	partial := filepath.Join(root, "partial")
	if err := os.WriteFile(partial, []byte("data"), 0600); err != nil {
		t.Fatal(err)
	}
	if _, err := modelapp.Publish(filepath.Join(root, "models"), partial, modelapp.Record{Source: source, Artifact: modelapp.Artifact{SizeBytes: 4}}); err != nil {
		t.Fatal(err)
	}
	m := modelapp.NewManager(filepath.Join(root, "models"))
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	var out bytes.Buffer
	// Named by its logical reference, not by a filesystem path.
	if err := runModel([]string{"revision", "org/repo", "--json"}, &out); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(out.String(), `"resolvedRevision":"abc"`) {
		t.Fatalf("out=%s", out.String())
	}
}
func TestRunDownloadListJSON(t *testing.T) {
	m := modelapp.NewManager(filepath.Join(t.TempDir(), "models"))
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	var out bytes.Buffer
	if err := runDownload([]string{"list", "--json"}, &out); err != nil {
		t.Fatal(err)
	}
	if out.String() != "{\"schemaVersion\":1,\"result\":[]}\n" {
		t.Fatalf("out=%s", out.String())
	}
}

func TestRunDownloadListHumanTable(t *testing.T) {
	m := modelapp.NewManager(filepath.Join(t.TempDir(), "models"))
	job, err := m.Create(modelapp.Candidate{Source: modelapp.Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: "https://example.test"}, Artifact: modelapp.Artifact{SizeBytes: 4 << 30}})
	if err != nil {
		t.Fatal(err)
	}
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	var out bytes.Buffer
	if err = runDownload([]string{"list"}, &out); err != nil {
		t.Fatal(err)
	}
	for _, expected := range []string{"ID", "TYPE", "NAME", "STATUS", "PROGRESS", "0.0% (0 B / 4.0 GiB)", "PATH", job.ID, "org/repo:m.gguf", job.TargetPath} {
		if !strings.Contains(out.String(), expected) {
			t.Fatalf("missing %q in %s", expected, out.String())
		}
	}
}

func TestDownloadHumanTableShowsPausedProgress(t *testing.T) {
	job := modelapp.Job{
		ID: "job", Source: modelapp.Source{Repository: "org/repo", Filename: "m.gguf"}, Status: modelapp.Paused,
		DownloadedBytes: 2 << 30, ExpectedSizeBytes: 4 << 30, TargetPath: `D:\models\org\repo\m.gguf`,
	}
	var out bytes.Buffer
	if err := writeDownloadHuman(&out, []modelapp.Job{job}, false); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(out.String(), "50.0% (2.0 GiB / 4.0 GiB)") {
		t.Fatalf("out=%s", out.String())
	}
}

func TestRunDownloadResumeReportsInterruptAsPaused(t *testing.T) {
	m := modelapp.NewManager(filepath.Join(t.TempDir(), "models"))
	job, err := m.Create(modelapp.Candidate{Source: modelapp.Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: "https://example.test"}})
	if err != nil {
		t.Fatal(err)
	}
	oldManager := modelManagerFactory
	oldContext := downloadContextFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	downloadContextFactory = func() (context.Context, context.CancelFunc) {
		ctx, cancel := context.WithCancel(context.Background())
		cancel()
		return ctx, func() {}
	}
	t.Cleanup(func() {
		modelManagerFactory = oldManager
		downloadContextFactory = oldContext
	})
	var out bytes.Buffer
	if err = runDownload([]string{"resume", job.ID}, &out); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(out.String(), "paused") || !strings.Contains(out.String(), "Download paused after interrupt.") {
		t.Fatalf("out=%s", out.String())
	}
	persisted, err := m.Status(job.ID)
	if err != nil || persisted.Status != modelapp.Paused {
		t.Fatalf("job=%+v err=%v", persisted, err)
	}
}

func TestRunModelSupportsNestedDownloadStatus(t *testing.T) {
	m := modelapp.NewManager(filepath.Join(t.TempDir(), "models"))
	job, err := m.Create(modelapp.Candidate{Source: modelapp.Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: "https://example.test"}})
	if err != nil {
		t.Fatal(err)
	}
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	var out bytes.Buffer
	if err = runModel([]string{"download", "status", job.ID, "--json"}, &out); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(out.String(), `"id":"`+job.ID+`"`) || !strings.Contains(out.String(), `"status":"pending"`) {
		t.Fatalf("out=%s", out.String())
	}
}

func TestRunModelNestedDownloadStatusLists(t *testing.T) {
	m := modelapp.NewManager(filepath.Join(t.TempDir(), "models"))
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	if err := runModel([]string{"download", "status"}, &bytes.Buffer{}); err != nil {
		t.Fatalf("err=%v", err)
	}
}

func TestRunModelListReportsUnreconciledAndInstalled(t *testing.T) {
	m := withModelManager(t)
	partial := filepath.Join(t.TempDir(), "partial")
	if err := os.WriteFile(partial, []byte("bytes"), 0600); err != nil {
		t.Fatal(err)
	}
	r := modelapp.Record{Source: modelapp.Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m-Q4_K_M.gguf", URL: "https://example.test/m.gguf"}, Artifact: modelapp.Artifact{SizeBytes: 5}}
	if _, err := modelapp.Publish(m.ModelsDir, partial, r); err != nil {
		t.Fatal(err)
	}
	unreconciledDir := filepath.Join(m.ModelsDir, "other", "repo")
	if err := os.MkdirAll(unreconciledDir, 0755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(unreconciledDir, "u.gguf"), []byte("data"), 0600); err != nil {
		t.Fatal(err)
	}
	if _, err := m.Create(modelapp.Candidate{Source: modelapp.Source{Provider: "huggingface", Repository: "org/repo2", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "job.gguf", URL: "https://example.test"}}); err != nil {
		t.Fatal(err)
	}
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })

	var human bytes.Buffer
	if err := runModel([]string{"list"}, &human); err != nil {
		t.Fatal(err)
	}
	for _, expected := range []string{"installed", "org/repo", "m-Q4_K_M.gguf", "unreconciled", "other/repo", "u.gguf"} {
		if !strings.Contains(human.String(), expected) {
			t.Fatalf("missing %q in %s", expected, human.String())
		}
	}
	if strings.Contains(human.String(), "job.gguf") {
		t.Fatalf("expected in-progress job to be excluded from model list: %s", human.String())
	}

	var jsonOut bytes.Buffer
	if err := runModel([]string{"list", "--json"}, &jsonOut); err != nil {
		t.Fatal(err)
	}
	for _, expected := range []string{`"state":"installed"`, `"state":"unreconciled"`, `"repository":"other/repo"`} {
		if !strings.Contains(jsonOut.String(), expected) {
			t.Fatalf("missing %q in %s", expected, jsonOut.String())
		}
	}
	if strings.Contains(jsonOut.String(), "job.gguf") {
		t.Fatalf("expected in-progress job to be excluded from model list json: %s", jsonOut.String())
	}
}

func TestRunModelReconcileRequiresExactlyOneMode(t *testing.T) {
	m := withModelManager(t)
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })

	if err := runModel([]string{"reconcile", "somepath"}, &bytes.Buffer{}); err == nil {
		t.Fatal("expected usage error when neither flag given")
	}
	if err := runModel([]string{"reconcile", "somepath", "--online", "--accept"}, &bytes.Buffer{}); err == nil {
		t.Fatal("expected usage error when both flags given")
	}
}

func TestRunModelReconcileAccept(t *testing.T) {
	m := withModelManager(t)
	// Two artifacts, because reconciling one moves it out of the unreconciled
	// state the reference resolves against: the second run needs its own
	// subject rather than re-reconciling the first.
	for _, repo := range []string{"one", "two"} {
		dir := filepath.Join(m.ModelsDir, "org", repo)
		if err := os.MkdirAll(dir, 0755); err != nil {
			t.Fatal(err)
		}
		if err := os.WriteFile(filepath.Join(dir, "m.gguf"), []byte("bytes"), 0600); err != nil {
			t.Fatal(err)
		}
	}
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })

	var out bytes.Buffer
	if err := runModel([]string{"reconcile", "org/one", "--accept"}, &out); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(out.String(), "Reconciled org/one") {
		t.Fatalf("out=%s", out.String())
	}

	var human bytes.Buffer
	if err := runModel([]string{"reconcile", "org/two", "--accept"}, &human); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(human.String(), "org/two") || !strings.Contains(human.String(), "m.gguf") {
		t.Fatalf("out=%s", human.String())
	}
}

func TestRunModelVerifyOutcomes(t *testing.T) {
	root := t.TempDir()
	source := modelapp.Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: "https://example.test"}
	partial := filepath.Join(root, "partial")
	body := []byte("gguf-bytes")
	if err := os.WriteFile(partial, body, 0600); err != nil {
		t.Fatal(err)
	}
	digest := sha256.Sum256(body)
	target, err := modelapp.Publish(filepath.Join(root, "models"), partial, modelapp.Record{Source: source, Artifact: modelapp.Artifact{SizeBytes: int64(len(body)), SHA256: hex.EncodeToString(digest[:])}})
	if err != nil {
		t.Fatal(err)
	}
	m := modelapp.NewManager(filepath.Join(root, "models"))
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })

	var matched bytes.Buffer
	if err := runModel([]string{"verify", "org/repo:m.gguf"}, &matched); err != nil {
		t.Fatalf("expected matched verify to succeed, err=%v out=%s", err, matched.String())
	}
	if !strings.Contains(matched.String(), "verified") {
		t.Fatalf("out=%s", matched.String())
	}

	if err = os.WriteFile(target, append(body, 'X'), 0600); err != nil {
		t.Fatal(err)
	}
	var mismatch bytes.Buffer
	if err = runModel([]string{"verify", "org/repo:m.gguf", "--json"}, &mismatch); err == nil {
		t.Fatal("expected non-zero exit on mismatch")
	}
	if !strings.Contains(mismatch.String(), `"Outcome":"mismatched"`) {
		t.Fatalf("out=%s", mismatch.String())
	}
}

func TestRunModelCheckUpdatesOutcomes(t *testing.T) {
	m := withModelManager(t)
	source := modelapp.Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: "https://example.test"}
	partial := filepath.Join(t.TempDir(), "partial")
	if err := os.WriteFile(partial, []byte("bytes"), 0600); err != nil {
		t.Fatal(err)
	}
	if _, err := modelapp.Publish(m.ModelsDir, partial, modelapp.Record{Source: source, Artifact: modelapp.Artifact{SizeBytes: 5}}); err != nil {
		t.Fatal(err)
	}
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })

	var out bytes.Buffer
	if err := runModel([]string{"check-updates", "org/repo:m.gguf"}, &out); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(out.String(), "partial comparison") {
		t.Fatalf("out=%s", out.String())
	}

	localPath := filepath.Join(t.TempDir(), "local")
	if err := os.WriteFile(localPath, []byte("bytes"), 0600); err != nil {
		t.Fatal(err)
	}
	localDir := filepath.Join(m.ModelsDir, "local", "repo")
	if err := os.MkdirAll(localDir, 0755); err != nil {
		t.Fatal(err)
	}
	localTarget := filepath.Join(localDir, "l.gguf")
	if err := os.WriteFile(localTarget, []byte("bytes"), 0600); err != nil {
		t.Fatal(err)
	}
	if _, err := m.ReconcileAccept(localTarget); err != nil {
		t.Fatal(err)
	}
	if err := runModel([]string{"check-updates", "local/repo:l.gguf"}, &bytes.Buffer{}); err == nil {
		t.Fatal("expected refusal for local-provider entry")
	}
}
