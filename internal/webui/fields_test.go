package webui

import (
	"errors"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/mstranich/coquena/internal/fields"
)

type fakeFieldStore struct {
	catalog fields.Catalog
	initErr error
}

func (f *fakeFieldStore) Load() (fields.Catalog, error) { return f.catalog, nil }
func (f *fakeFieldStore) Define(field fields.Field) (fields.Catalog, error) {
	if _, ok := f.catalog.Find(field.Name); ok {
		return fields.Catalog{}, errors.New("field already exists")
	}
	f.catalog.Fields = append(f.catalog.Fields, field)
	return f.catalog, nil
}
func (f *fakeFieldStore) Upsert(field fields.Field) (fields.Catalog, error) {
	for i, existing := range f.catalog.Fields {
		if existing.Name == field.Name {
			f.catalog.Fields[i] = field
			return f.catalog, nil
		}
	}
	f.catalog.Fields = append(f.catalog.Fields, field)
	return f.catalog, nil
}
func (f *fakeFieldStore) Remove(name string) (fields.Catalog, error) {
	remaining := make([]fields.Field, 0, len(f.catalog.Fields))
	found := false
	for _, existing := range f.catalog.Fields {
		if existing.Name == name {
			found = true
			continue
		}
		remaining = append(remaining, existing)
	}
	if !found {
		return fields.Catalog{}, errors.New("field not found")
	}
	f.catalog.Fields = remaining
	return f.catalog, nil
}

func newFieldsAPIWith(store fieldStore, err error) *fieldsAPI {
	return &fieldsAPI{store: func() (fieldStore, error) { return store, err }}
}

func TestFieldsAPIListsCatalog(t *testing.T) {
	store := &fakeFieldStore{catalog: fields.Catalog{Fields: []fields.Field{{Name: "ctx-size", Kind: fields.KindInteger}}}}
	api := newFieldsAPIWith(store, nil)
	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodGet, "/api/fields", nil))
	if rec.Code != http.StatusOK || !strings.Contains(rec.Body.String(), "ctx-size") {
		t.Fatalf("get = %d %q", rec.Code, rec.Body.String())
	}
}

func TestFieldsAPIDefinesAndRefusesDuplicate(t *testing.T) {
	store := &fakeFieldStore{}
	api := newFieldsAPIWith(store, nil)
	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodPost, "/api/fields", strings.NewReader(`{"name":"ctx-size","kind":"integer"}`)))
	if rec.Code != http.StatusOK {
		t.Fatalf("define = %d %q", rec.Code, rec.Body.String())
	}
	rec = httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodPost, "/api/fields", strings.NewReader(`{"name":"ctx-size","kind":"integer"}`)))
	if rec.Code != http.StatusBadRequest {
		t.Fatalf("duplicate define = %d", rec.Code)
	}
}

func TestFieldsAPIReadsOneField(t *testing.T) {
	store := &fakeFieldStore{catalog: fields.Catalog{Fields: []fields.Field{{Name: "ctx-size", Kind: fields.KindInteger}}}}
	api := newFieldsAPIWith(store, nil)
	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodGet, "/api/fields/ctx-size", nil))
	if rec.Code != http.StatusOK || !strings.Contains(rec.Body.String(), "ctx-size") {
		t.Fatalf("get one = %d %q", rec.Code, rec.Body.String())
	}
}

func TestFieldsAPIUpsertCreatesAndReplaces(t *testing.T) {
	store := &fakeFieldStore{}
	api := newFieldsAPIWith(store, nil)
	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodPut, "/api/fields/ctx-size", strings.NewReader(`{"kind":"integer","default":4096}`)))
	if rec.Code != http.StatusOK {
		t.Fatalf("create via put = %d %q", rec.Code, rec.Body.String())
	}
	rec = httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodPut, "/api/fields/ctx-size", strings.NewReader(`{"kind":"integer","default":8192}`)))
	if rec.Code != http.StatusOK || !strings.Contains(rec.Body.String(), "8192") {
		t.Fatalf("replace via put = %d %q", rec.Code, rec.Body.String())
	}
}

func TestFieldsAPIDeletesWithSubtree(t *testing.T) {
	store := &fakeFieldStore{catalog: fields.Catalog{Fields: []fields.Field{{Name: "ctx-size", Kind: fields.KindInteger}}}}
	api := newFieldsAPIWith(store, nil)
	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodDelete, "/api/fields/ctx-size", nil))
	if rec.Code != http.StatusOK {
		t.Fatalf("delete = %d %q", rec.Code, rec.Body.String())
	}
	rec = httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodGet, "/api/fields/ctx-size", nil))
	if rec.Code != http.StatusNotFound {
		t.Fatalf("expected field absent after delete, got %d", rec.Code)
	}
}

func TestFieldsAPIRejectsUnsupportedMethodAndMalformedJSON(t *testing.T) {
	store := &fakeFieldStore{}
	api := newFieldsAPIWith(store, nil)
	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodPatch, "/api/fields", nil))
	if rec.Code != http.StatusMethodNotAllowed {
		t.Fatalf("unsupported method = %d", rec.Code)
	}
	rec = httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodPost, "/api/fields", strings.NewReader(`{"unknown":true}`)))
	if rec.Code != http.StatusBadRequest {
		t.Fatalf("unknown field = %d", rec.Code)
	}
	rec = httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodPost, "/api/fields", strings.NewReader(`not json`)))
	if rec.Code != http.StatusBadRequest {
		t.Fatalf("malformed json = %d", rec.Code)
	}
}

func TestFieldsAPIReportsUninitializedInstallation(t *testing.T) {
	api := newFieldsAPIWith(nil, errors.New("portable installation is not initialized; run coquena init"))
	rec := httptest.NewRecorder()
	api.serve(rec, httptest.NewRequest(http.MethodGet, "/api/fields", nil))
	if rec.Code != http.StatusConflict {
		t.Fatalf("preinit = %d", rec.Code)
	}
}

func TestFieldsAPIIgnoresUnrelatedPath(t *testing.T) {
	api := newFieldsAPIWith(&fakeFieldStore{}, nil)
	if api.serve(httptest.NewRecorder(), httptest.NewRequest(http.MethodGet, "/api/models", nil)) {
		t.Fatal("expected fieldsAPI to ignore unrelated paths")
	}
}
