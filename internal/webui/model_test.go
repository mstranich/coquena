package webui

import (
	"bytes"
	"context"
	"encoding/binary"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"testing"
	"time"

	modelapp "github.com/mstranich/coquena/internal/model"
)

// namedGGUF builds a publishable artifact carrying general.name. It declares
// general.type because publication refuses an artifact it cannot classify, so a
// fixture that only carries a name is no longer a model Coquena will record.
func namedGGUF(name string) []byte {
	return stringGGUF("general.type", "model", "general.name", name)
}

// stringGGUF builds a minimal valid GGUF header carrying the given string
// key-value pairs, so a test can declare general.type and have the artifact
// classify as something other than unknown.
func stringGGUF(pairs ...string) []byte {
	b := make([]byte, 24)
	copy(b, "GGUF")
	binary.LittleEndian.PutUint32(b[4:], 3)
	binary.LittleEndian.PutUint64(b[16:], uint64(len(pairs)/2))
	appendString := func(b []byte, s string) []byte {
		var n [8]byte
		binary.LittleEndian.PutUint64(n[:], uint64(len(s)))
		return append(append(b, n[:]...), []byte(s)...)
	}
	for i := 0; i+1 < len(pairs); i += 2 {
		b = appendString(b, pairs[i])
		var t [4]byte
		binary.LittleEndian.PutUint32(t[:], 8)
		b = append(b, t[:]...)
		b = appendString(b, pairs[i+1])
	}
	return b
}

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
	body := stringGGUF("general.type", "model")
	download := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Length", strconv.Itoa(len(body)))
		_, _ = w.Write(body)
	}))
	defer download.Close()
	api, resolver := modelTestAPI(t)
	defer resolver.Close()
	m, _ := api.manager()
	job, err := m.Create(modelapp.Candidate{
		Source:   modelapp.Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: download.URL},
		Artifact: modelapp.Artifact{SizeBytes: int64(len(body))},
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
	// A real GGUF header: the sidecar no longer caches metadata, so the lazy
	// metadata endpoint reads general.name from the artifact itself.
	body := namedGGUF("Example")
	if err := os.WriteFile(partial, body, 0600); err != nil {
		t.Fatal(err)
	}
	source := modelapp.Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: server.URL}
	if _, err := modelapp.Publish(m.ModelsDir, partial, modelapp.Record{Source: source, Artifact: modelapp.Artifact{SizeBytes: int64(len(body))}}); err != nil {
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

// TestModelAPIDiscoverReportsCandidateClassification asserts remote discovery
// reaches the API as the manager computes it: every candidate the repository
// holds, each carrying the classification read from its own header.
func TestModelAPIDiscoverReportsCandidateClassification(t *testing.T) {
	body := stringGGUF("general.type", "model", "general.architecture", "gemma4")
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		switch r.URL.Path {
		case "/api/models/org/repo/revision/main":
			_, _ = w.Write([]byte(`{"sha":"abc","siblings":[{"rfilename":"m.gguf","size":` + strconv.Itoa(len(body)) + `}]}`))
		case "/org/repo/resolve/abc/m.gguf":
			http.ServeContent(w, r, "m.gguf", time.Time{}, bytes.NewReader(body))
		default:
			http.NotFound(w, r)
		}
	}))
	defer server.Close()
	m := modelapp.NewManager(filepath.Join(t.TempDir(), "models"))
	m.Resolver = modelapp.Resolver{Client: server.Client(), BaseURL: server.URL}
	api := newModelAPIWithManager(m)

	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodPost, "/api/models/discover", bytes.NewBufferString(`{"reference":"org/repo"}`)))
	if rec.Code != http.StatusOK || !strings.Contains(rec.Body.String(), `"type":"model"`) || !strings.Contains(rec.Body.String(), `"architecture":"gemma4"`) || !strings.Contains(rec.Body.String(), `"candidates"`) || !strings.Contains(rec.Body.String(), `"quantizations":[{"label":"m"`) || !strings.Contains(rec.Body.String(), `"totalSizeKnown":true`) {
		t.Fatalf("status=%d body=%s", rec.Code, rec.Body.String())
	}
	if strings.Contains(rec.Body.String(), `"modelCard"`) {
		t.Fatalf("API discovery fetched a model card by default: %s", rec.Body.String())
	}
	bad := httptest.NewRecorder()
	api.serve(bad, httptest.NewRequest(http.MethodPost, "/api/models/discover", bytes.NewBufferString(`{"reference":"org/repo","includeRaw":true}`)))
	if bad.Code != http.StatusBadRequest {
		t.Fatalf("status=%d body=%s", bad.Code, bad.Body.String())
	}
	method := httptest.NewRecorder()
	api.serve(method, httptest.NewRequest(http.MethodGet, "/api/models/discover", nil))
	if method.Code != http.StatusMethodNotAllowed {
		t.Fatalf("status=%d", method.Code)
	}
}

// TestModelAPIInspectRejectsRawMetadataOption asserts the removed includeRaw
// option is now an unknown property rather than an accepted no-op: the API
// offers no way to ask for the raw GGUF map.
func TestModelAPIInspectRejectsRawMetadataOption(t *testing.T) {
	api, server := modelTestAPI(t)
	defer server.Close()
	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodPost, "/api/models/inspect", bytes.NewBufferString(`{"reference":"org/repo","includeRaw":true}`)))
	if rec.Code != http.StatusBadRequest {
		t.Fatalf("status=%d body=%s", rec.Code, rec.Body.String())
	}
	if strings.Contains(rec.Body.String(), "tokenizer.ggml.tokens") {
		t.Fatalf("raw metadata leaked: %s", rec.Body.String())
	}
}

// TestModelAPIReportsClassification asserts an installed artifact's declared
// type reaches both the library listing — read from the sidecar, without the
// metadata the lazy endpoint carries — and the inspection response the WebUI
// expands into its table.
func TestModelAPIReportsClassification(t *testing.T) {
	api, server := modelTestAPI(t)
	defer server.Close()
	m, _ := api.manager()
	partial := filepath.Join(t.TempDir(), "m.partial")
	body := stringGGUF("general.name", "Example", "general.type", "model", "general.architecture", "gemma4")
	if err := os.WriteFile(partial, body, 0600); err != nil {
		t.Fatal(err)
	}
	source := modelapp.Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: server.URL}
	if _, err := modelapp.Publish(m.ModelsDir, partial, modelapp.Record{Source: source, Artifact: modelapp.Artifact{SizeBytes: int64(len(body))}}); err != nil {
		t.Fatal(err)
	}
	library := httptest.NewRecorder()
	api.serve(library, httptest.NewRequest(http.MethodGet, "/api/models/library", nil))
	if library.Code != http.StatusOK || !strings.Contains(library.Body.String(), `"classification"`) || !strings.Contains(library.Body.String(), `"type":"model"`) {
		t.Fatalf("status=%d body=%s", library.Code, library.Body.String())
	}
	inspection := httptest.NewRecorder()
	api.serve(inspection, httptest.NewRequest(http.MethodPost, "/api/models/metadata", bytes.NewBufferString(`{"repository":"org/repo","filename":"m.gguf"}`)))
	if inspection.Code != http.StatusOK || !strings.Contains(inspection.Body.String(), `"type":"model"`) || !strings.Contains(inspection.Body.String(), `"architecture":"gemma4"`) {
		t.Fatalf("status=%d body=%s", inspection.Code, inspection.Body.String())
	}
}
