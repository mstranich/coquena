package webui

import (
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/mstranich/coquena/internal/fields"
	"github.com/mstranich/coquena/internal/presets"
)

func newPresetsAPIWith(t *testing.T) (*presetsAPI, string) {
	t.Helper()
	home := t.TempDir()
	if err := os.MkdirAll(filepath.Join(home, "config"), 0o755); err != nil {
		t.Fatal(err)
	}
	models := filepath.Join(home, "models")
	if err := os.MkdirAll(models, 0o755); err != nil {
		t.Fatal(err)
	}
	fieldStore := fields.NewStore(home)
	if _, err := fieldStore.Define(fields.Field{Name: "context-size", Kind: fields.KindInteger}); err != nil {
		t.Fatal(err)
	}
	return &presetsAPI{
		presetStore: func() (*presets.Store, error) { return presets.NewStore(home), nil },
		fieldStore:  func() (*fields.Store, error) { return fieldStore, nil },
		modelDir:    func() (string, error) { return models, nil },
	}, home
}

func TestPresetsAPIDefineAndRead(t *testing.T) {
	api, _ := newPresetsAPIWith(t)
	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodPost, "/api/presets", strings.NewReader(`{"name":"qwen3-fast","title":"Qwen3 Fast"}`)))
	if rec.Code != http.StatusOK {
		t.Fatalf("define = %d %q", rec.Code, rec.Body.String())
	}
	rec = httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodGet, "/api/presets/qwen3-fast", nil))
	if rec.Code != http.StatusOK || !strings.Contains(rec.Body.String(), "Qwen3 Fast") {
		t.Fatalf("get = %d %q", rec.Code, rec.Body.String())
	}
}

func TestPresetsAPIRefusesReservedName(t *testing.T) {
	api, _ := newPresetsAPIWith(t)
	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodPost, "/api/presets", strings.NewReader(`{"name":"render"}`)))
	if rec.Code != http.StatusBadRequest {
		t.Fatalf("define render = %d %q", rec.Code, rec.Body.String())
	}
}

func TestPresetsAPICollectionSeparatesGlobal(t *testing.T) {
	api, _ := newPresetsAPIWith(t)
	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodPost, "/api/presets", strings.NewReader(`{"name":"qwen3-fast"}`)))
	if rec.Code != http.StatusOK {
		t.Fatal(rec.Body.String())
	}
	rec = httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodGet, "/api/presets", nil))
	if rec.Code != http.StatusOK || !strings.Contains(rec.Body.String(), `"global"`) || !strings.Contains(rec.Body.String(), `"kind":"global"`) {
		t.Fatalf("collection = %d %q", rec.Code, rec.Body.String())
	}
}

func TestPresetsAPISetAndUnsetValueRoundtrip(t *testing.T) {
	api, _ := newPresetsAPIWith(t)
	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodPost, "/api/presets", strings.NewReader(`{"name":"qwen3-fast"}`)))
	if rec.Code != http.StatusOK {
		t.Fatal(rec.Body.String())
	}
	rec = httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodPut, "/api/presets/qwen3-fast/values/context-size", strings.NewReader(`{"value":8192}`)))
	if rec.Code != http.StatusOK {
		t.Fatalf("set value = %d %q", rec.Code, rec.Body.String())
	}
	rec = httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodGet, "/api/presets/qwen3-fast", nil))
	if !strings.Contains(rec.Body.String(), "8192") {
		t.Fatalf("expected value present, got %q", rec.Body.String())
	}
	rec = httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodDelete, "/api/presets/qwen3-fast/values/context-size", nil))
	if rec.Code != http.StatusNoContent {
		t.Fatalf("unset = %d %q", rec.Code, rec.Body.String())
	}
	rec = httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodGet, "/api/presets/qwen3-fast", nil))
	if strings.Contains(rec.Body.String(), "8192") {
		t.Fatalf("expected value gone, got %q", rec.Body.String())
	}
}

func TestPresetsAPIRejectsGetOnSingleValue(t *testing.T) {
	api, _ := newPresetsAPIWith(t)
	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodGet, "/api/presets/qwen3-fast/values/context-size", nil))
	if rec.Code != http.StatusMethodNotAllowed {
		t.Fatalf("get single value = %d", rec.Code)
	}
}

func TestPresetsAPIRejectsPutOnWholePreset(t *testing.T) {
	api, _ := newPresetsAPIWith(t)
	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodPut, "/api/presets/qwen3-fast", nil))
	if rec.Code != http.StatusMethodNotAllowed {
		t.Fatalf("put whole preset = %d", rec.Code)
	}
}

func TestPresetsAPIMoveRenames(t *testing.T) {
	api, _ := newPresetsAPIWith(t)
	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodPost, "/api/presets", strings.NewReader(`{"name":"qwen3-fst"}`)))
	if rec.Code != http.StatusOK {
		t.Fatal(rec.Body.String())
	}
	req := httptest.NewRequest("MOVE", "/api/presets/qwen3-fst", nil)
	req.Header.Set("Destination", "/api/presets/qwen3-fast")
	rec = httptest.NewRecorder()
	api.serve(rec, req)
	if rec.Code != http.StatusNoContent {
		t.Fatalf("move = %d %q", rec.Code, rec.Body.String())
	}
	rec = httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodGet, "/api/presets/qwen3-fast", nil))
	if rec.Code != http.StatusOK {
		t.Fatalf("get renamed = %d", rec.Code)
	}
	rec = httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodGet, "/api/presets/qwen3-fst", nil))
	if rec.Code != http.StatusNotFound {
		t.Fatalf("expected old name gone, got %d", rec.Code)
	}
}

func TestPresetsAPIMoveConflict(t *testing.T) {
	api, _ := newPresetsAPIWith(t)
	for _, name := range []string{"a", "b"} {
		rec := httptest.NewRecorder()
		api.serve(rec, httptest.NewRequest(http.MethodPost, "/api/presets", strings.NewReader(`{"name":"`+name+`"}`)))
		if rec.Code != http.StatusOK {
			t.Fatal(rec.Body.String())
		}
	}
	req := httptest.NewRequest("MOVE", "/api/presets/a", nil)
	req.Header.Set("Destination", "/api/presets/b")
	rec := httptest.NewRecorder()
	api.serve(rec, req)
	if rec.Code != http.StatusBadRequest {
		t.Fatalf("move conflict = %d %q", rec.Code, rec.Body.String())
	}
}

func TestPresetsAPIRefusesOperationsOnGlobal(t *testing.T) {
	api, _ := newPresetsAPIWith(t)
	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodDelete, "/api/presets/global", nil))
	if rec.Code != http.StatusBadRequest {
		t.Fatalf("delete global = %d", rec.Code)
	}
	rec = httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodPut, "/api/presets/global/model", strings.NewReader(`{"reference":"org/repo:Q4_K_M","allowMissing":true}`)))
	if rec.Code != http.StatusBadRequest {
		t.Fatalf("bind model on global = %d %q", rec.Code, rec.Body.String())
	}
}

func TestPresetsAPIRenderReturnsTextWithoutWriting(t *testing.T) {
	api, home := newPresetsAPIWith(t)
	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodPost, "/api/presets", strings.NewReader(`{"name":"qwen3-fast"}`)))
	if rec.Code != http.StatusOK {
		t.Fatal(rec.Body.String())
	}
	rec = httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodGet, "/api/presets/render?runtime=b6234", nil))
	if rec.Code != http.StatusOK || !strings.Contains(rec.Body.String(), "[*]") {
		t.Fatalf("render = %d %q", rec.Code, rec.Body.String())
	}
	if _, err := os.Stat(filepath.Join(home, "data")); !os.IsNotExist(err) {
		t.Fatal("expected render to not write any file")
	}
}
