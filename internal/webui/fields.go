package webui

import (
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"strings"

	"github.com/mstranich/coquena/internal/fields"
	"github.com/mstranich/coquena/internal/installation"
	"github.com/mstranich/coquena/internal/presets"
)

type fieldStore interface {
	Load() (fields.Catalog, error)
	Define(fields.Field) (fields.Catalog, error)
	Upsert(fields.Field) (fields.Catalog, error)
	Remove(string) (fields.Catalog, error)
}

type fieldsAPI struct {
	store   func() (fieldStore, error)
	presets func() (*presets.Store, error)
}

func newFieldsAPI() *fieldsAPI {
	return &fieldsAPI{
		store: func() (fieldStore, error) {
			home, err := installation.Home()
			if err != nil {
				return nil, err
			}
			return fields.NewStore(home), nil
		},
		presets: func() (*presets.Store, error) {
			home, err := installation.Home()
			if err != nil {
				return nil, err
			}
			return presets.NewStore(home), nil
		},
	}
}

func (a *fieldsAPI) serve(w http.ResponseWriter, r *http.Request) bool {
	if r.URL.Path != "/api/fields" && !strings.HasPrefix(r.URL.Path, "/api/fields/") {
		return false
	}
	store, err := a.store()
	if err != nil {
		writeFieldsError(w, http.StatusConflict, err.Error())
		return true
	}
	if r.URL.Path == "/api/fields" {
		a.serveCollection(w, r, store)
		return true
	}
	name := strings.TrimPrefix(r.URL.Path, "/api/fields/")
	if name == "" {
		http.NotFound(w, r)
		return true
	}
	a.serveField(w, r, store, name)
	return true
}

func (a *fieldsAPI) serveCollection(w http.ResponseWriter, r *http.Request, store fieldStore) {
	switch r.Method {
	case http.MethodGet:
		catalog, err := store.Load()
		if err != nil {
			writeFieldsError(w, http.StatusInternalServerError, err.Error())
			return
		}
		writeRuntimeJSON(w, catalog)
	case http.MethodPost:
		f, ok := decodeField(w, r)
		if !ok {
			return
		}
		catalog, err := store.Define(f)
		if err != nil {
			writeFieldsError(w, http.StatusBadRequest, err.Error())
			return
		}
		field, _ := catalog.Find(f.Name)
		writeRuntimeJSON(w, field)
	default:
		w.WriteHeader(http.StatusMethodNotAllowed)
	}
}

func (a *fieldsAPI) serveField(w http.ResponseWriter, r *http.Request, store fieldStore, name string) {
	switch r.Method {
	case http.MethodGet:
		catalog, err := store.Load()
		if err != nil {
			writeFieldsError(w, http.StatusInternalServerError, err.Error())
			return
		}
		field, ok := catalog.Resolve(name)
		if !ok {
			writeFieldsError(w, http.StatusNotFound, "field not found")
			return
		}
		writeRuntimeJSON(w, field)
	case http.MethodPut:
		f, ok := decodeField(w, r)
		if !ok {
			return
		}
		f.Name = name
		catalog, err := store.Upsert(f)
		if err != nil {
			writeFieldsError(w, http.StatusBadRequest, err.Error())
			return
		}
		field, _ := catalog.Find(name)
		writeRuntimeJSON(w, field)
	case http.MethodDelete:
		catalog, err := store.Load()
		if err != nil {
			writeFieldsError(w, http.StatusInternalServerError, err.Error())
			return
		}
		resolved, ok := catalog.Resolve(name)
		if !ok {
			writeFieldsError(w, http.StatusNotFound, "field not found")
			return
		}
		force := r.URL.Query().Get("force") == "true" || r.URL.Query().Get("force") == "1"
		presetStore, err := a.presets()
		if err != nil {
			writeFieldsError(w, http.StatusConflict, err.Error())
			return
		}
		doc, err := presetStore.Load()
		if err != nil {
			writeFieldsError(w, http.StatusInternalServerError, err.Error())
			return
		}
		referencing := presets.ReferencingPresets(doc, catalog, resolved.Name)
		if len(referencing) > 0 && !force {
			writeFieldsError(w, http.StatusBadRequest, fmt.Sprintf("field %q is referenced by preset(s) %s; pass force to remove it and their values", resolved.Name, strings.Join(referencing, ", ")))
			return
		}
		if len(referencing) > 0 {
			cascaded := presets.CascadeRemoveFieldValues(doc, catalog.Subtree(resolved.Name))
			if _, err := presetStore.PublishDocument(cascaded); err != nil {
				writeFieldsError(w, http.StatusInternalServerError, err.Error())
				return
			}
		}
		newCatalog, err := store.Remove(resolved.Name)
		if err != nil {
			writeFieldsError(w, http.StatusInternalServerError, err.Error())
			return
		}
		writeRuntimeJSON(w, newCatalog)
	default:
		w.WriteHeader(http.StatusMethodNotAllowed)
	}
}

func decodeField(w http.ResponseWriter, r *http.Request) (fields.Field, bool) {
	var f fields.Field
	decoder := json.NewDecoder(r.Body)
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(&f); err != nil {
		writeFieldsError(w, http.StatusBadRequest, "invalid field definition")
		return fields.Field{}, false
	}
	var extra any
	if err := decoder.Decode(&extra); !errors.Is(err, io.EOF) {
		writeFieldsError(w, http.StatusBadRequest, "invalid field definition")
		return fields.Field{}, false
	}
	return f, true
}

func writeFieldsError(w http.ResponseWriter, status int, message string) {
	if message == "" {
		message = "field request failed"
	}
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(struct {
		Error string `json:"error"`
	}{message})
}
