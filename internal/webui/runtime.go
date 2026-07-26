package webui

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"log"
	"net/http"
	"os"
	"strings"
	"sync"
	"time"

	"github.com/mstranich/coquena/internal/download"
	"github.com/mstranich/coquena/internal/installation"
	runtimeapp "github.com/mstranich/coquena/internal/runtime"
	runtimestore "github.com/mstranich/coquena/internal/runtime/store"
)

type RuntimeService = runtimeapp.Service
type RuntimeRecord = runtimeapp.Record
type RuntimeMutation = runtimeapp.Mutation
type RuntimeProgress = runtimeapp.Progress

type runtimeOperation struct {
	ID        string
	Progress  RuntimeProgress
	Mutation  *RuntimeMutation
	Err       string
	Done      bool
	Updated   time.Time
	watchers  map[chan struct{}]struct{}
	cancel    context.CancelFunc
	cancelled bool
}

type runtimeOperations struct {
	mu     sync.Mutex
	active string
	items  map[string]*runtimeOperation
}

func newRuntimeOperations() *runtimeOperations {
	return &runtimeOperations{items: map[string]*runtimeOperation{}}
}

func (o *runtimeOperations) create() (*runtimeOperation, error) {
	o.mu.Lock()
	defer o.mu.Unlock()
	for id, operation := range o.items {
		if operation.Done && time.Since(operation.Updated) > 10*time.Minute {
			delete(o.items, id)
		}
	}
	if o.active != "" {
		return nil, errors.New("a runtime download is already in progress")
	}
	b := make([]byte, 16)
	if _, err := rand.Read(b); err != nil {
		return nil, err
	}
	op := &runtimeOperation{ID: hex.EncodeToString(b), Progress: RuntimeProgress{Phase: "queued"}, Updated: time.Now(), watchers: map[chan struct{}]struct{}{}}
	o.items[op.ID] = op
	o.active = op.ID
	return op, nil
}

func (o *runtimeOperations) busy() bool {
	o.mu.Lock()
	defer o.mu.Unlock()
	return o.active != ""
}

func (o *runtimeOperations) update(id string, progress RuntimeProgress, mutation *RuntimeMutation, err error) {
	o.mu.Lock()
	defer o.mu.Unlock()
	op := o.items[id]
	if op == nil {
		return
	}
	terminal := progress.Phase == "completed" || progress.Phase == "failed" || progress.Phase == "paused" || progress.Phase == "cancelled"
	// Asset reads can be very small; retain the latest value but wake SSE
	// subscribers at most four times per second until a terminal state.
	if !terminal && progress.Phase == "downloading" && op.Progress.Phase == "downloading" && time.Since(op.Updated) < 250*time.Millisecond {
		op.Progress = progress
		return
	}
	op.Progress = progress
	op.Mutation = mutation
	if err != nil {
		op.Err = "runtime operation failed; see server logs"
	}
	op.Done = terminal
	op.Updated = time.Now()
	if op.Done && o.active == id {
		o.active = ""
	}
	for ch := range op.watchers {
		close(ch)
		delete(op.watchers, ch)
	}
}

func (o *runtimeOperations) setCancel(id string, cancel context.CancelFunc) {
	o.mu.Lock()
	defer o.mu.Unlock()
	if op := o.items[id]; op != nil {
		op.cancel = cancel
	}
}

func (o *runtimeOperations) interrupt(id string, cancelled bool) bool {
	o.mu.Lock()
	defer o.mu.Unlock()
	op := o.items[id]
	if op == nil || op.Done || op.cancel == nil {
		return false
	}
	op.cancelled = cancelled
	op.cancel()
	return true
}

func (o *runtimeOperations) wasCancelled(id string) bool {
	o.mu.Lock()
	defer o.mu.Unlock()
	return o.items[id] != nil && o.items[id].cancelled
}

func (o *runtimeOperations) snapshot(id string) (*runtimeOperation, chan struct{}, bool) {
	o.mu.Lock()
	defer o.mu.Unlock()
	op := o.items[id]
	if op == nil {
		return nil, nil, false
	}
	copy := *op
	ch := make(chan struct{})
	if !op.Done {
		op.watchers[ch] = struct{}{}
	}
	return &copy, ch, true
}

func (o *runtimeOperations) removeWatcher(id string, ch chan struct{}) {
	if ch == nil {
		return
	}
	o.mu.Lock()
	defer o.mu.Unlock()
	if op := o.items[id]; op != nil {
		delete(op.watchers, ch)
	}
}

type runtimeAPI struct {
	service    RuntimeService
	operations *runtimeOperations
	logger     *log.Logger
}

func newRuntimeAPI(service RuntimeService, logs ...io.Writer) *runtimeAPI {
	output := io.Discard
	if len(logs) > 0 && logs[0] != nil {
		output = logs[0]
	}
	return &runtimeAPI{service: service, operations: newRuntimeOperations(), logger: log.New(output, "runtime API: ", log.LstdFlags)}
}

func (a *runtimeAPI) serve(w http.ResponseWriter, r *http.Request) bool {
	if !strings.HasPrefix(r.URL.Path, "/api/runtimes/llama.cpp") {
		return false
	}
	if a.service == nil {
		writeRuntimeError(w, http.StatusServiceUnavailable, "runtime management is unavailable")
		return true
	}
	suffix := strings.TrimPrefix(r.URL.Path, "/api/runtimes/llama.cpp")
	switch {
	case suffix == "/remote":
		if r.Method != http.MethodGet {
			w.WriteHeader(http.StatusMethodNotAllowed)
			return true
		}
		result, err := a.service.Remote(r.Context(), runtimeapp.Query{Release: "latest"})
		if err != nil {
			a.logger.Printf("remote discovery failed: %v", err)
			writeRuntimeError(w, http.StatusBadGateway, "runtime discovery failed")
			return true
		}
		writeRuntimeJSON(w, result)
	case suffix == "":
		if r.Method != http.MethodGet {
			w.WriteHeader(http.StatusMethodNotAllowed)
			return true
		}
		result, err := a.service.List(r.Context(), "")
		if err != nil {
			a.logger.Printf("local listing failed: %v", err)
			if errors.Is(err, runtimestore.ErrNotInitialized) {
				writeRuntimeError(w, http.StatusConflict, "Coquena is not initialized; run coquena init")
				return true
			}
			writeRuntimeError(w, http.StatusInternalServerError, "runtime listing failed; see server logs")
			return true
		}
		writeRuntimeJSON(w, result)
	case suffix == "/downloads":
		if r.Method == http.MethodGet {
			root, err := installation.RuntimesDirectory()
			if err != nil {
				writeRuntimeError(w, http.StatusConflict, "Coquena is not initialized; run coquena init")
				return true
			}
			entries, err := download.List("", root)
			if err != nil {
				a.logger.Printf("runtime download listing failed: %v", err)
				writeRuntimeError(w, http.StatusInternalServerError, "runtime download listing failed; see server logs")
				return true
			}
			writeRuntimeJSON(w, entries)
			return true
		}
		if r.Method != http.MethodPost {
			w.WriteHeader(http.StatusMethodNotAllowed)
			return true
		}
		var body struct {
			AssetID string `json:"assetId"`
		}
		decoder := json.NewDecoder(r.Body)
		decoder.DisallowUnknownFields()
		if decoder.Decode(&body) != nil || decoder.Decode(&struct{}{}) != io.EOF || runtimeapp.ValidateExactAssetID(body.AssetID) != nil {
			writeRuntimeError(w, http.StatusBadRequest, "assetId is required")
			return true
		}
		op, err := a.operations.create()
		if err != nil {
			a.logger.Printf("download operation creation failed: %v", err)
			writeRuntimeError(w, http.StatusConflict, err.Error())
			return true
		}
		go a.download(op.ID, body.AssetID)
		writeRuntimeJSONStatus(w, http.StatusAccepted, struct {
			OperationID string `json:"operationId"`
		}{op.ID})
	case strings.HasPrefix(suffix, "/downloads/") && strings.HasSuffix(suffix, "/actions"):
		if r.Method != http.MethodPost {
			w.WriteHeader(http.StatusMethodNotAllowed)
			return true
		}
		id := strings.TrimSuffix(strings.TrimPrefix(suffix, "/downloads/"), "/actions")
		a.downloadAction(w, r, id)
	case strings.HasPrefix(suffix, "/downloads/") && strings.HasSuffix(suffix, "/events"):
		if r.Method != http.MethodGet {
			w.WriteHeader(http.StatusMethodNotAllowed)
			return true
		}
		a.events(w, r, strings.TrimSuffix(strings.TrimPrefix(suffix, "/downloads/"), "/events"))
	case strings.HasPrefix(suffix, "/"):
		if r.Method != http.MethodDelete {
			w.WriteHeader(http.StatusMethodNotAllowed)
			return true
		}
		id := strings.TrimPrefix(suffix, "/")
		if runtimeapp.ValidateExactAssetID(id) != nil {
			writeRuntimeError(w, http.StatusBadRequest, "invalid runtime asset ID")
			return true
		}
		if a.operations.busy() {
			writeRuntimeError(w, http.StatusConflict, "a runtime download is already in progress")
			return true
		}
		mutation, err := a.service.Remove(r.Context(), id)
		if err != nil {
			a.logger.Printf("remove %q failed: %v", id, err)
			status := http.StatusConflict
			if errors.Is(err, os.ErrNotExist) {
				status = http.StatusNotFound
			}
			writeRuntimeError(w, status, "runtime removal failed")
			return true
		}
		writeRuntimeJSON(w, mutation)
	default:
		writeRuntimeError(w, http.StatusNotFound, "runtime API not found")
	}
	return true
}

func (a *runtimeAPI) download(id, assetID string) {
	a.logger.Printf("download operation %s for %q started", id, assetID)
	a.operations.update(id, RuntimeProgress{Phase: "downloading", AssetID: assetID}, nil, nil)
	ctx, cancel := context.WithCancel(context.Background())
	a.operations.setCancel(id, cancel)
	defer cancel()
	lastPhase := "downloading"
	mutation, err := a.service.Download(ctx, runtimeapp.DownloadRequest{AssetID: assetID}, func(p RuntimeProgress) {
		if p.Phase != lastPhase {
			a.logger.Printf("download operation %s for %q entered phase %s", id, assetID, p.Phase)
			lastPhase = p.Phase
		}
		a.operations.update(id, p, nil, nil)
	})
	if err != nil {
		if errors.Is(err, context.Canceled) {
			if a.operations.wasCancelled(id) {
				_ = a.removeRuntimeDownload(assetID)
				a.logger.Printf("download operation %s for %q cancelled", id, assetID)
				a.operations.update(id, RuntimeProgress{Phase: "cancelled", AssetID: assetID}, nil, nil)
			} else {
				a.logger.Printf("download operation %s for %q paused", id, assetID)
				a.operations.update(id, RuntimeProgress{Phase: "paused", AssetID: assetID}, nil, nil)
			}
			return
		}
		a.logger.Printf("download operation %s for %q failed: %v", id, assetID, err)
		a.operations.update(id, RuntimeProgress{Phase: "failed", AssetID: assetID}, nil, err)
		return
	}
	a.logger.Printf("download operation %s for %q completed", id, assetID)
	a.operations.update(id, RuntimeProgress{Phase: "completed", AssetID: assetID}, &mutation, nil)
}

func (a *runtimeAPI) downloadAction(w http.ResponseWriter, r *http.Request, id string) {
	var body struct {
		Action string `json:"action"`
	}
	if json.NewDecoder(r.Body).Decode(&body) != nil {
		writeRuntimeError(w, http.StatusBadRequest, "action is required")
		return
	}
	switch body.Action {
	case "pause":
		if !a.operations.interrupt(id, false) {
			writeRuntimeError(w, http.StatusConflict, "runtime download is not active")
			return
		}
		writeRuntimeJSON(w, map[string]string{"status": "pausing"})
	case "cancel":
		if a.operations.interrupt(id, true) {
			writeRuntimeJSON(w, map[string]string{"status": "cancelling"})
			return
		}
		if err := a.removeRuntimeDownload(id); err != nil {
			writeRuntimeError(w, http.StatusNotFound, "runtime download not found")
			return
		}
		writeRuntimeJSON(w, map[string]string{"status": "cancelled"})
	case "resume":
		op, err := a.operations.create()
		if err != nil {
			writeRuntimeError(w, http.StatusConflict, err.Error())
			return
		}
		go a.download(op.ID, id)
		writeRuntimeJSONStatus(w, http.StatusAccepted, struct {
			OperationID string `json:"operationId"`
		}{op.ID})
	default:
		writeRuntimeError(w, http.StatusBadRequest, "unsupported runtime download action")
	}
}

func (a *runtimeAPI) removeRuntimeDownload(id string) error {
	root, err := installation.RuntimesDirectory()
	if err != nil {
		return err
	}
	entries, err := download.List("", root)
	if err != nil {
		return err
	}
	for _, entry := range entries {
		if entry.ID == id {
			if err := os.Remove(entry.Path); err != nil && !errors.Is(err, os.ErrNotExist) {
				return err
			}
			if err := os.Remove(strings.TrimSuffix(entry.Path, ".download.json") + ".partial"); err != nil && !errors.Is(err, os.ErrNotExist) {
				return err
			}
			return nil
		}
	}
	return os.ErrNotExist
}

func (a *runtimeAPI) events(w http.ResponseWriter, r *http.Request, id string) {
	op, wake, found := a.operations.snapshot(id)
	if !found {
		writeRuntimeError(w, http.StatusNotFound, "runtime operation not found")
		return
	}
	w.Header().Set("Content-Type", "text/event-stream")
	w.Header().Set("Cache-Control", "no-cache")
	flusher, ok := w.(http.Flusher)
	if !ok {
		writeRuntimeError(w, http.StatusInternalServerError, "streaming is unavailable")
		return
	}
	for {
		event := "progress"
		if op.Done {
			if op.Err != "" {
				event = "failed"
			} else {
				event = "completed"
			}
		}
		payload := struct {
			RuntimeProgress
			OperationID string           `json:"operationId"`
			Mutation    *RuntimeMutation `json:"mutation,omitempty"`
			Error       string           `json:"error,omitempty"`
		}{op.Progress, op.ID, op.Mutation, op.Err}
		b, _ := json.Marshal(payload)
		_, _ = fmt.Fprintf(w, "event: %s\ndata: %s\n\n", event, b)
		flusher.Flush()
		if op.Done {
			return
		}
		select {
		case <-r.Context().Done():
			a.operations.removeWatcher(id, wake)
			return
		case <-wake:
			op, wake, found = a.operations.snapshot(id)
			if !found {
				return
			}
		}
	}
}

func writeRuntimeJSON(w http.ResponseWriter, result any) {
	writeRuntimeJSONStatus(w, http.StatusOK, result)
}

func writeRuntimeJSONStatus(w http.ResponseWriter, status int, result any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(struct {
		SchemaVersion int `json:"schemaVersion"`
		Result        any `json:"result"`
	}{1, result})
}
func writeRuntimeError(w http.ResponseWriter, status int, message string) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(struct {
		Error string `json:"error"`
	}{message})
}
