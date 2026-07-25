package webui

import (
	"errors"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
)

type fakeConfig struct {
	path string
	err  error
}

func (f *fakeConfig) ModelsDirectory() (string, error) { return f.path, f.err }
func (f *fakeConfig) SetModelsDirectory(path string) (string, error) {
	if f.err != nil {
		return "", f.err
	}
	f.path = path
	return path, nil
}

func TestConfigAPIReadsAndWritesDirectory(t *testing.T) {
	f := &fakeConfig{path: "C:/Models"}
	api := newConfigAPI(f)
	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodGet, "/api/config/models-directory", nil))
	if rec.Code != http.StatusOK || !strings.Contains(rec.Body.String(), `"path":"C:/Models"`) {
		t.Fatalf("get = %d %q", rec.Code, rec.Body.String())
	}
	rec = httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodPut, "/api/config/models-directory", strings.NewReader(`{"path":"D:/Models"}`)))
	if rec.Code != http.StatusOK || f.path != "D:/Models" {
		t.Fatalf("put = %d %q", rec.Code, rec.Body.String())
	}
}
func TestConfigAPIRejectsBadInputAndPreinit(t *testing.T) {
	api := newConfigAPI(&fakeConfig{})
	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodPut, "/api/config/models-directory", strings.NewReader(`{"other":true}`)))
	if rec.Code != http.StatusBadRequest {
		t.Fatalf("bad body = %d", rec.Code)
	}
	api = newConfigAPI(&fakeConfig{err: errors.New("portable installation is not initialized; run coquena init")})
	rec = httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodGet, "/api/config/models-directory", nil))
	if rec.Code != http.StatusConflict {
		t.Fatalf("preinit = %d", rec.Code)
	}
}
