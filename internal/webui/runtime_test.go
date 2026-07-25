package webui

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	runtimeapp "github.com/mstranich/coquena/internal/runtime"
	runtimestore "github.com/mstranich/coquena/internal/runtime/store"
	hostsystem "github.com/mstranich/coquena/internal/system"
)

type fakeRuntimeService struct {
	remote   func(context.Context, runtimeapp.Query) ([]runtimeapp.Record, error)
	list     func(context.Context, string) ([]runtimeapp.Record, error)
	download func(context.Context, runtimeapp.DownloadRequest, func(runtimeapp.Progress)) (runtimeapp.Mutation, error)
	remove   func(context.Context, string) (runtimeapp.Mutation, error)
}

func (f fakeRuntimeService) Remote(ctx context.Context, q runtimeapp.Query) ([]runtimeapp.Record, error) {
	return f.remote(ctx, q)
}
func (f fakeRuntimeService) List(ctx context.Context, selector string) ([]runtimeapp.Record, error) {
	return f.list(ctx, selector)
}
func (f fakeRuntimeService) Download(ctx context.Context, request runtimeapp.DownloadRequest, progress func(runtimeapp.Progress)) (runtimeapp.Mutation, error) {
	return f.download(ctx, request, progress)
}
func (f fakeRuntimeService) Remove(ctx context.Context, id string) (runtimeapp.Mutation, error) {
	return f.remove(ctx, id)
}

func runtimeHandler(service RuntimeService) http.Handler {
	return HandlerWithServices(func() (hostsystem.Report, error) { return hostsystem.Report{}, nil }, service)
}

func completeFakeRuntime() fakeRuntimeService {
	record := runtimeapp.Record{Release: "b1", Backend: "cpu", AssetID: "llama-b1-bin-win-cpu-x64.zip", Compatibility: "compatible"}
	return fakeRuntimeService{
		remote: func(context.Context, runtimeapp.Query) ([]runtimeapp.Record, error) {
			return []runtimeapp.Record{record}, nil
		},
		list: func(context.Context, string) ([]runtimeapp.Record, error) { return []runtimeapp.Record{record}, nil },
		download: func(_ context.Context, request runtimeapp.DownloadRequest, progress func(runtimeapp.Progress)) (runtimeapp.Mutation, error) {
			progress(runtimeapp.Progress{Phase: "downloading", AssetID: request.AssetID, AssetTransferred: 5, AssetTotal: 10, TotalTransferred: 5, TotalBytes: 10})
			return runtimeapp.Mutation{Runtime: record, Changed: true}, nil
		},
		remove: func(_ context.Context, id string) (runtimeapp.Mutation, error) {
			record.AssetID = id
			return runtimeapp.Mutation{Runtime: record, Changed: true}, nil
		},
	}
}

func TestRuntimeAPIListsRemoteAndLocal(t *testing.T) {
	handler := runtimeHandler(completeFakeRuntime())
	for _, path := range []string{"/api/runtimes/llama.cpp/remote", "/api/runtimes/llama.cpp"} {
		recorder := httptest.NewRecorder()
		handler.ServeHTTP(recorder, httptest.NewRequest(http.MethodGet, path, nil))
		if recorder.Code != http.StatusOK || !strings.Contains(recorder.Body.String(), `"assetId":"llama-b1-bin-win-cpu-x64.zip"`) {
			t.Fatalf("%s = (%d, %s)", path, recorder.Code, recorder.Body.String())
		}
	}
}

func TestRuntimeAPIReportsInitializationGuidanceAndLogsDetail(t *testing.T) {
	service := completeFakeRuntime()
	service.list = func(context.Context, string) ([]runtimeapp.Record, error) {
		return nil, fmt.Errorf("open config: %w", runtimestore.ErrNotInitialized)
	}
	var logs bytes.Buffer
	handler := HandlerWithServicesAndLogger(func() (hostsystem.Report, error) { return hostsystem.Report{}, nil }, service, &logs)
	recorder := httptest.NewRecorder()
	handler.ServeHTTP(recorder, httptest.NewRequest(http.MethodGet, "/api/runtimes/llama.cpp", nil))
	if recorder.Code != http.StatusConflict || !strings.Contains(recorder.Body.String(), "run coquena init") {
		t.Fatalf("response = (%d, %s)", recorder.Code, recorder.Body.String())
	}
	if !strings.Contains(logs.String(), "local listing failed: open config") {
		t.Fatalf("logs = %q", logs.String())
	}
}

func TestRuntimeAPIDownloadStreamsTerminalSSE(t *testing.T) {
	handler := runtimeHandler(completeFakeRuntime())
	post := httptest.NewRecorder()
	handler.ServeHTTP(post, httptest.NewRequest(http.MethodPost, "/api/runtimes/llama.cpp/downloads", bytes.NewBufferString(`{"assetId":"llama-b1-bin-win-cpu-x64.zip"}`)))
	if post.Code != http.StatusAccepted {
		t.Fatalf("post = (%d, %s)", post.Code, post.Body.String())
	}
	operationID := strings.Split(strings.Split(post.Body.String(), `"operationId":"`)[1], `"`)[0]
	events := httptest.NewRecorder()
	handler.ServeHTTP(events, httptest.NewRequest(http.MethodGet, "/api/runtimes/llama.cpp/downloads/"+operationID+"/events", nil))
	if !strings.Contains(events.Body.String(), "event: completed") || !strings.Contains(events.Body.String(), `"changed":true`) {
		t.Fatalf("events = %s", events.Body.String())
	}
}

func TestRuntimeAPIDownloadReportsRepeatAndFailure(t *testing.T) {
	service := completeFakeRuntime()
	service.download = func(context.Context, runtimeapp.DownloadRequest, func(runtimeapp.Progress)) (runtimeapp.Mutation, error) {
		return runtimeapp.Mutation{Changed: false}, nil
	}
	assertTerminalEvent(t, runtimeHandler(service), "completed", `"changed":false`)

	service.download = func(context.Context, runtimeapp.DownloadRequest, func(runtimeapp.Progress)) (runtimeapp.Mutation, error) {
		return runtimeapp.Mutation{}, errors.New("upstream secret must not escape")
	}
	assertTerminalEvent(t, runtimeHandler(service), "failed", `"error":"runtime operation failed; see server logs"`)
}

func assertTerminalEvent(t *testing.T, handler http.Handler, event, content string) {
	t.Helper()
	post := httptest.NewRecorder()
	handler.ServeHTTP(post, httptest.NewRequest(http.MethodPost, "/api/runtimes/llama.cpp/downloads", bytes.NewBufferString(`{"assetId":"llama-b1-bin-win-cpu-x64.zip"}`)))
	if post.Code != http.StatusAccepted {
		t.Fatalf("post = (%d, %s)", post.Code, post.Body.String())
	}
	operationID := strings.Split(strings.Split(post.Body.String(), `"operationId":"`)[1], `"`)[0]
	events := httptest.NewRecorder()
	handler.ServeHTTP(events, httptest.NewRequest(http.MethodGet, "/api/runtimes/llama.cpp/downloads/"+operationID+"/events", nil))
	if !strings.Contains(events.Body.String(), "event: "+event) || !strings.Contains(events.Body.String(), content) {
		t.Fatalf("events = %s", events.Body.String())
	}
}

func TestRuntimeOperationRegistryRejectsBusyReconnectsAndExpiresTerminalRecords(t *testing.T) {
	operations := newRuntimeOperations()
	op, err := operations.create()
	if err != nil {
		t.Fatal(err)
	}
	if _, err = operations.create(); err == nil {
		t.Fatal("expected active-operation conflict")
	}
	operations.update(op.ID, runtimeapp.Progress{Phase: "downloading", TotalTransferred: 7}, nil, nil)
	snapshot, watcher, found := operations.snapshot(op.ID)
	operations.removeWatcher(op.ID, watcher)
	if !found || snapshot.Progress.TotalTransferred != 7 {
		t.Fatalf("reconnect snapshot = %+v, found=%v", snapshot, found)
	}
	operations.update(op.ID, runtimeapp.Progress{Phase: "completed"}, &runtimeapp.Mutation{Changed: true}, nil)
	operations.mu.Lock()
	operations.items[op.ID].Updated = time.Now().Add(-11 * time.Minute)
	operations.mu.Unlock()
	if _, err = operations.create(); err != nil {
		t.Fatal(err)
	}
	if _, _, found = operations.snapshot(op.ID); found {
		t.Fatal("expected expired terminal operation to be removed")
	}
}

func TestRuntimeAPIRejectsRemovalWhileDownloadOperationIsActive(t *testing.T) {
	service := completeFakeRuntime()
	releaseDownload := make(chan struct{})
	service.download = func(context.Context, runtimeapp.DownloadRequest, func(runtimeapp.Progress)) (runtimeapp.Mutation, error) {
		<-releaseDownload
		return runtimeapp.Mutation{}, nil
	}
	api := newRuntimeAPI(service)
	handler := http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { api.serve(w, r) })
	post := httptest.NewRecorder()
	handler.ServeHTTP(post, httptest.NewRequest(http.MethodPost, "/api/runtimes/llama.cpp/downloads", bytes.NewBufferString(`{"assetId":"llama-b1-bin-win-cpu-x64.zip"}`)))
	remove := httptest.NewRecorder()
	handler.ServeHTTP(remove, httptest.NewRequest(http.MethodDelete, "/api/runtimes/llama.cpp/llama-b1-bin-win-cpu-x64.zip", nil))
	close(releaseDownload)
	if remove.Code != http.StatusConflict {
		t.Fatalf("remove during download = (%d, %s)", remove.Code, remove.Body.String())
	}
}

func TestRuntimeAPIRejectsInvalidDownloadAndAngularFallback(t *testing.T) {
	handler := runtimeHandler(completeFakeRuntime())
	for _, body := range []string{`{}`, `{"assetId":"cuda"}`, `{"assetId":"llama-b1.zip","url":"https://invalid"}`} {
		recorder := httptest.NewRecorder()
		handler.ServeHTTP(recorder, httptest.NewRequest(http.MethodPost, "/api/runtimes/llama.cpp/downloads", bytes.NewBufferString(body)))
		if recorder.Code != http.StatusBadRequest || strings.Contains(recorder.Body.String(), "<app-root>") {
			t.Fatalf("body %s = (%d, %s)", body, recorder.Code, recorder.Body.String())
		}
	}
}

func TestRuntimeAPIRemovesExactIDAndReportsFailure(t *testing.T) {
	service := completeFakeRuntime()
	handler := runtimeHandler(service)
	recorder := httptest.NewRecorder()
	handler.ServeHTTP(recorder, httptest.NewRequest(http.MethodDelete, "/api/runtimes/llama.cpp/llama-b1-bin-win-cpu-x64.zip", nil))
	if recorder.Code != http.StatusOK || !strings.Contains(recorder.Body.String(), `"changed":true`) {
		t.Fatalf("remove = (%d, %s)", recorder.Code, recorder.Body.String())
	}
	service.remove = func(context.Context, string) (runtimeapp.Mutation, error) {
		return runtimeapp.Mutation{}, errors.New("busy")
	}
	recorder = httptest.NewRecorder()
	runtimeHandler(service).ServeHTTP(recorder, httptest.NewRequest(http.MethodDelete, "/api/runtimes/llama.cpp/llama-b1-bin-win-cpu-x64.zip", nil))
	if recorder.Code != http.StatusConflict {
		t.Fatalf("busy remove = %d", recorder.Code)
	}
}

func TestRuntimeAPIRejectsMethodsAndUnknownOperation(t *testing.T) {
	handler := runtimeHandler(completeFakeRuntime())
	method := httptest.NewRecorder()
	handler.ServeHTTP(method, httptest.NewRequest(http.MethodPost, "/api/runtimes/llama.cpp/remote", nil))
	if method.Code != http.StatusMethodNotAllowed {
		t.Fatalf("method = %d", method.Code)
	}
	unknown := httptest.NewRecorder()
	handler.ServeHTTP(unknown, httptest.NewRequest(http.MethodGet, "/api/runtimes/llama.cpp/downloads/missing/events", nil))
	if unknown.Code != http.StatusNotFound || strings.Contains(unknown.Body.String(), "<app-root>") {
		t.Fatalf("unknown = (%d, %s)", unknown.Code, unknown.Body.String())
	}
}
