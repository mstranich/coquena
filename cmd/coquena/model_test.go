package main

import (
	"bytes"
	"context"
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
	r := modelapp.Record{Source: modelapp.Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m-Q4_K_M.gguf", URL: "https://example.test/m.gguf"}, Artifact: modelapp.Artifact{SizeBytes: 5}, GGUF: map[string]any{}}
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
	target, err := modelapp.Publish(filepath.Join(root, "models"), partial, modelapp.Record{Source: source, Artifact: modelapp.Artifact{SizeBytes: 4}, GGUF: map[string]any{}})
	if err != nil {
		t.Fatal(err)
	}
	m := modelapp.NewManager(filepath.Join(root, "models"))
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	var out bytes.Buffer
	if err := runModel([]string{"revision", target, "--json"}, &out); err != nil {
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
