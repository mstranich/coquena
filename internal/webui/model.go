package webui

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"

	"github.com/mstranich/coquena/internal/installation"
	modelapp "github.com/mstranich/coquena/internal/model"
)

type modelAPI struct {
	mu      sync.Mutex
	service *modelapp.Manager
	fixed   bool
	logs    io.Writer
}

func newModelAPI(logs ...io.Writer) *modelAPI {
	a := &modelAPI{logs: io.Discard}
	if len(logs) > 0 && logs[0] != nil {
		a.logs = logs[0]
	}
	return a
}
func newModelAPIWithManager(manager *modelapp.Manager) *modelAPI {
	return &modelAPI{service: manager, fixed: true}
}
func (a *modelAPI) manager() (*modelapp.Manager, error) {
	if a.fixed {
		return a.service, nil
	}
	models, e := installation.ModelsDirectory()
	if e != nil {
		return nil, e
	}
	a.mu.Lock()
	defer a.mu.Unlock()
	if a.service != nil && a.service.ModelsDir == models {
		return a.service, nil
	}
	a.service = modelapp.NewManager(models)
	a.service.LogWriter = a.logs
	return a.service, nil
}
func (a *modelAPI) serve(w http.ResponseWriter, r *http.Request) bool {
	if !strings.HasPrefix(r.URL.Path, "/api/models") {
		return false
	}
	m, e := a.manager()
	if e != nil {
		writeRuntimeError(w, http.StatusConflict, e.Error())
		return true
	}
	s := strings.TrimPrefix(r.URL.Path, "/api/models")
	if strings.HasPrefix(s, "/publishers/") && strings.HasSuffix(s, "/avatar") {
		if r.Method != http.MethodGet {
			w.WriteHeader(http.StatusMethodNotAllowed)
			return true
		}
		publisher := strings.TrimSuffix(strings.TrimPrefix(s, "/publishers/"), "/avatar")
		path, avatarErr := m.PublisherAvatar(publisher)
		if avatarErr != nil {
			writeRuntimeError(w, http.StatusNotFound, "publisher avatar not found")
			return true
		}
		file, avatarErr := os.Open(path)
		if avatarErr != nil {
			writeRuntimeError(w, http.StatusNotFound, "publisher avatar not found")
			return true
		}
		defer file.Close()
		info, avatarErr := file.Stat()
		if avatarErr != nil {
			writeRuntimeError(w, http.StatusInternalServerError, "publisher avatar is unreadable")
			return true
		}
		http.ServeContent(w, r, filepath.Base(path), info.ModTime(), file)
		return true
	}
	if s == "/library" {
		if r.Method != http.MethodGet {
			w.WriteHeader(http.StatusMethodNotAllowed)
			return true
		}
		entries, listErr := m.Library()
		if listErr != nil {
			writeRuntimeError(w, http.StatusInternalServerError, listErr.Error())
		} else {
			writeRuntimeJSON(w, entries)
		}
		return true
	}
	if s == "/metadata" {
		if r.Method != http.MethodPost {
			w.WriteHeader(http.StatusMethodNotAllowed)
			return true
		}
		var b struct {
			Repository string `json:"repository"`
			Filename   string `json:"filename"`
		}
		decoder := json.NewDecoder(r.Body)
		decoder.DisallowUnknownFields()
		if decoder.Decode(&b) != nil || b.Repository == "" || b.Filename == "" || decoder.Decode(&struct{}{}) != io.EOF {
			writeRuntimeError(w, http.StatusBadRequest, "repository and filename are required")
			return true
		}
		inspection, metadataErr := m.InstalledMetadata(b.Repository, b.Filename)
		if metadataErr != nil {
			writeRuntimeError(w, http.StatusNotFound, "installed model metadata not found")
			return true
		}
		writeRuntimeJSON(w, inspection)
		return true
	}
	if s == "/resolve" || s == "/inspect" {
		if r.Method != http.MethodPost {
			w.WriteHeader(405)
			return true
		}
		var b struct {
			Reference  string `json:"reference"`
			Selector   string `json:"selector,omitempty"`
			IncludeRaw bool   `json:"includeRaw,omitempty"`
		}
		decoder := json.NewDecoder(r.Body)
		decoder.DisallowUnknownFields()
		if decoder.Decode(&b) != nil || b.Reference == "" || decoder.Decode(&struct{}{}) != io.EOF {
			writeRuntimeError(w, 400, "reference is required")
			return true
		}
		c, e := m.Resolve(b.Reference, b.Selector)
		if e == nil && s == "/inspect" {
			if len(c) != 1 {
				e = errors.New("inspection requires one GGUF artifact")
			} else {
				var x modelapp.Inspection
				x, e = m.InspectRemote(c[0])
				if e == nil {
					// includeRaw is accepted but no longer changes the payload:
					// inspection never returns the raw map. Task 9.1 removes the
					// property outright.
					writeRuntimeJSON(w, x)
					return true
				}
			}
		}
		if e != nil {
			writeRuntimeError(w, 400, e.Error())
			return true
		}
		writeRuntimeJSON(w, c)
		return true
	}
	if s == "/downloads" {
		if r.Method == http.MethodGet {
			v, e := m.List()
			if e != nil {
				writeRuntimeError(w, 500, e.Error())
			} else {
				writeRuntimeJSON(w, v)
			}
			return true
		}
		if r.Method != http.MethodPost {
			w.WriteHeader(405)
			return true
		}
		var b struct {
			Reference string `json:"reference"`
			Selector  string `json:"selector,omitempty"`
		}
		decoder := json.NewDecoder(r.Body)
		decoder.DisallowUnknownFields()
		if decoder.Decode(&b) != nil || b.Reference == "" || decoder.Decode(&struct{}{}) != io.EOF {
			writeRuntimeError(w, 400, "reference is required")
			return true
		}
		c, e := m.Resolve(b.Reference, b.Selector)
		if e == nil && len(c) != 1 {
			e = errors.New("download requires one GGUF artifact")
		}
		if e != nil {
			writeRuntimeError(w, 400, e.Error())
			return true
		}
		j, e := m.Create(c[0])
		if e != nil {
			writeRuntimeError(w, 409, e.Error())
			return true
		}
		go func() { _, _ = m.Resume(context.Background(), j.ID, nil) }()
		writeRuntimeJSONStatus(w, http.StatusAccepted, j)
		return true
	}
	if strings.HasPrefix(s, "/downloads/") {
		parts := strings.Split(strings.TrimPrefix(s, "/downloads/"), "/")
		id := parts[0]
		if id == "" {
			writeRuntimeError(w, 404, "model download not found")
			return true
		}
		if len(parts) == 1 && r.Method == http.MethodGet {
			j, e := m.Status(id)
			if e != nil {
				writeRuntimeError(w, 404, "model download not found")
			} else {
				writeRuntimeJSON(w, j)
			}
			return true
		}
		if len(parts) == 2 && parts[1] == "events" {
			if r.Method != http.MethodGet {
				w.WriteHeader(http.StatusMethodNotAllowed)
				return true
			}
			if _, e := m.Status(id); e != nil {
				writeRuntimeError(w, http.StatusNotFound, "model download not found")
				return true
			}
			a.events(w, r, m, id)
			return true
		}
		if len(parts) == 2 && r.Method == http.MethodPost {
			var j modelapp.Job
			switch parts[1] {
			case "pause":
				j, e = m.Pause(id)
			case "resume":
				j, e = m.Status(id)
				if e == nil {
					go func() { _, _ = m.Resume(context.Background(), id, nil) }()
					for attempt := 0; attempt < 10; attempt++ {
						time.Sleep(10 * time.Millisecond)
						current, statusErr := m.Status(id)
						if statusErr == nil {
							j = current
							if j.Status != modelapp.Paused {
								break
							}
						}
					}
				}
			case "cancel":
				j, e = m.Status(id)
				if e == nil {
					j.Status = modelapp.Cancelled
					e = m.Cancel(id)
				}
			default:
				writeRuntimeError(w, 404, "model action not found")
				return true
			}
			if e != nil {
				writeRuntimeError(w, 409, e.Error())
			} else {
				writeRuntimeJSON(w, j)
			}
			return true
		}
	}
	writeRuntimeError(w, 404, "model API not found")
	return true
}
func (a *modelAPI) events(w http.ResponseWriter, r *http.Request, m *modelapp.Manager, id string) {
	f, ok := w.(http.Flusher)
	if !ok {
		writeRuntimeError(w, http.StatusInternalServerError, "streaming is unavailable")
		return
	}
	w.Header().Set("Content-Type", "text/event-stream")
	w.Header().Set("Cache-Control", "no-cache")
	ticker := time.NewTicker(250 * time.Millisecond)
	defer ticker.Stop()
	for {
		j, err := m.Status(id)
		if err != nil {
			return
		}
		b, _ := json.Marshal(j)
		event := "progress"
		if j.Status == modelapp.Completed {
			event = "completed"
		}
		if j.Status == modelapp.Failed || j.Status == modelapp.Cancelled {
			event = "failed"
		}
		if j.Status == modelapp.Paused {
			event = "paused"
		}
		_, _ = w.Write([]byte("event: " + event + "\ndata: " + string(b) + "\n\n"))
		f.Flush()
		if event != "progress" {
			if event == "completed" {
				m.ForgetTerminal(id)
			}
			return
		}
		select {
		case <-r.Context().Done():
			return
		case <-ticker.C:
		}
	}
}
