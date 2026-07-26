package webui

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

func modelTestAPI(t *testing.T) (*modelAPI, *httptest.Server) {
	t.Helper()
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path == "/api/models/org/repo/revision/main" {
			_, _ = w.Write([]byte(`{"sha":"abc","siblings":[{"rfilename":"m.gguf","size":24}]}`))
			return
		}
		http.NotFound(w, r)
	}))
	m := modelapp.NewManager(filepath.Join(t.TempDir(), "models"))
	m.Resolver = modelapp.Resolver{Client: server.Client(), BaseURL: server.URL}
	return newModelAPIWithManager(m), server
}
func TestModelAPIResolveAndRejectsMalformedInput(t *testing.T) {
	api, server := modelTestAPI(t)
	defer server.Close()
	ok := httptest.NewRecorder()
	api.serve(ok, httptest.NewRequest(http.MethodPost, "/api/models/resolve", bytes.NewBufferString(`{"reference":"org/repo"}`)))
	if ok.Code != http.StatusOK {
		t.Fatalf("status=%d body=%s", ok.Code, ok.Body.String())
	}
	bad := httptest.NewRecorder()
	api.serve(bad, httptest.NewRequest(http.MethodPost, "/api/models/resolve", bytes.NewBufferString(`{"reference":"org/repo","unknown":true}`)))
	if bad.Code != http.StatusBadRequest {
		t.Fatalf("status=%d", bad.Code)
	}
}
func TestModelAPIDownloadPathsDoNotFallBack(t *testing.T) {
	api, server := modelTestAPI(t)
	defer server.Close()
	for _, request := range []*http.Request{httptest.NewRequest(http.MethodGet, "/api/models/downloads/missing", nil), httptest.NewRequest(http.MethodPost, "/api/models/downloads/missing/events", nil), httptest.NewRequest(http.MethodGet, "/api/models/nope", nil)} {
		rec := httptest.NewRecorder()
		if !api.serve(rec, request) {
			t.Fatal("route not claimed")
		}
		if rec.Code == http.StatusOK || bytes.Contains(rec.Body.Bytes(), []byte("<html")) {
			t.Fatalf("unexpected fallback: %d %s", rec.Code, rec.Body.String())
		}
	}
}
func TestModelAPISendsImmediateTerminalSSE(t *testing.T) {
	api, server := modelTestAPI(t)
	defer server.Close()
	m, _ := api.manager()
	candidate := modelapp.Candidate{Source: modelapp.Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: server.URL}, Artifact: modelapp.Artifact{SizeBytes: 24}}
	job, err := m.Create(candidate)
	if err != nil {
		t.Fatal(err)
	}
	if _, err = m.Resume(context.Background(), job.ID, nil); err == nil {
		t.Fatal("expected failure")
	}
	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodGet, "/api/models/downloads/"+job.ID+"/events", nil))
	if rec.Code != http.StatusOK || !strings.Contains(rec.Body.String(), "event: failed") || !strings.Contains(rec.Body.String(), job.ID) {
		t.Fatalf("status=%d body=%s", rec.Code, rec.Body.String())
	}
}

func TestModelAPISendsCompletedSSEAfterJobRecordIsRemoved(t *testing.T) {
	body := append([]byte{'G', 'G', 'U', 'F', 3, 0, 0, 0}, make([]byte, 16)...)
	download := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Length", "24")
		_, _ = w.Write(body)
	}))
	defer download.Close()
	api, resolver := modelTestAPI(t)
	defer resolver.Close()
	m, _ := api.manager()
	job, err := m.Create(modelapp.Candidate{
		Source:   modelapp.Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: download.URL},
		Artifact: modelapp.Artifact{SizeBytes: 24},
	})
	if err != nil {
		t.Fatal(err)
	}
	if _, err = m.Resume(context.Background(), job.ID, nil); err != nil {
		t.Fatal(err)
	}
	if _, err = os.Stat(job.TargetPath + ".download.json"); !os.IsNotExist(err) {
		t.Fatalf("completed job record still exists: %v", err)
	}

	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodGet, "/api/models/downloads/"+job.ID+"/events", nil))
	if rec.Code != http.StatusOK || !strings.Contains(rec.Body.String(), "event: completed") || !strings.Contains(rec.Body.String(), job.ID) {
		t.Fatalf("status=%d body=%s", rec.Code, rec.Body.String())
	}
	if _, err = m.Status(job.ID); !os.IsNotExist(err) {
		t.Fatalf("terminal snapshot was not released: %v", err)
	}
}
func TestModelAPISSERejectsUnknownAndMethod(t *testing.T) {
	api, server := modelTestAPI(t)
	defer server.Close()
	for _, request := range []*http.Request{httptest.NewRequest(http.MethodGet, "/api/models/downloads/missing/events", nil), httptest.NewRequest(http.MethodPost, "/api/models/downloads/missing/events", nil)} {
		rec := httptest.NewRecorder()
		api.serve(rec, request)
		if rec.Code != http.StatusNotFound && rec.Code != http.StatusMethodNotAllowed {
			t.Fatalf("status=%d", rec.Code)
		}
	}
}
func TestModelAPICancelRemovesModelScopedDownload(t *testing.T) {
	api, server := modelTestAPI(t)
	defer server.Close()
	m, _ := api.manager()
	job, err := m.Create(modelapp.Candidate{Source: modelapp.Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: server.URL}})
	if err != nil {
		t.Fatal(err)
	}
	if err = os.WriteFile(job.PartialPath, []byte("partial"), 0600); err != nil {
		t.Fatal(err)
	}
	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodPost, "/api/models/downloads/"+job.ID+"/cancel", bytes.NewReader([]byte(`{}`))))
	if rec.Code != http.StatusOK {
		t.Fatalf("status=%d body=%s", rec.Code, rec.Body.String())
	}
	if _, err = os.Stat(job.PartialPath); !os.IsNotExist(err) {
		t.Fatalf("partial still exists: %v", err)
	}
	if _, err = m.Status(job.ID); !os.IsNotExist(err) {
		t.Fatalf("job still exists: %v", err)
	}
}
func TestModelAPILibraryListsColdRecoveredDownload(t *testing.T) {
	api, server := modelTestAPI(t)
	defer server.Close()
	m, _ := api.manager()
	job, err := m.Create(modelapp.Candidate{Source: modelapp.Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: server.URL}})
	if err != nil {
		t.Fatal(err)
	}
	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodGet, "/api/models/library", nil))
	if rec.Code != http.StatusOK || !strings.Contains(rec.Body.String(), job.ID) || !strings.Contains(rec.Body.String(), "m.gguf.partial") {
		t.Fatalf("status=%d body=%s", rec.Code, rec.Body.String())
	}
}

func TestModelAPIInstalledMetadataIsLazy(t *testing.T) {
	api, server := modelTestAPI(t)
	defer server.Close()
	m, _ := api.manager()
	partial := filepath.Join(t.TempDir(), "m.partial")
	if err := os.WriteFile(partial, []byte("model"), 0600); err != nil {
		t.Fatal(err)
	}
	source := modelapp.Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: server.URL}
	if _, err := modelapp.Publish(m.ModelsDir, partial, modelapp.Record{Source: source, Artifact: modelapp.Artifact{SizeBytes: 5}, GGUF: map[string]any{"general.name": "Example"}}); err != nil {
		t.Fatal(err)
	}
	library := httptest.NewRecorder()
	api.serve(library, httptest.NewRequest(http.MethodGet, "/api/models/library", nil))
	if strings.Contains(library.Body.String(), "general.name") {
		t.Fatalf("library eagerly included metadata: %s", library.Body.String())
	}
	metadata := httptest.NewRecorder()
	api.serve(metadata, httptest.NewRequest(http.MethodPost, "/api/models/metadata", bytes.NewBufferString(`{"repository":"org/repo","filename":"m.gguf"}`)))
	if metadata.Code != http.StatusOK || !strings.Contains(metadata.Body.String(), "general.name") {
		t.Fatalf("status=%d body=%s", metadata.Code, metadata.Body.String())
	}
}
