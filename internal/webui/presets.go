package webui

import (
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"strings"

	"github.com/mstranich/coquena/internal/fields"
	"github.com/mstranich/coquena/internal/installation"
	modelapp "github.com/mstranich/coquena/internal/model"
	"github.com/mstranich/coquena/internal/presets"
)

type presetsAPI struct {
	presetStore func() (*presets.Store, error)
	fieldStore  func() (*fields.Store, error)
	modelDir    func() (string, error)
}

func newPresetsAPI() *presetsAPI {
	return &presetsAPI{
		presetStore: func() (*presets.Store, error) {
			home, err := installation.Home()
			if err != nil {
				return nil, err
			}
			return presets.NewStore(home), nil
		},
		fieldStore: func() (*fields.Store, error) {
			home, err := installation.Home()
			if err != nil {
				return nil, err
			}
			return fields.NewStore(home), nil
		},
		modelDir: installation.ModelsDirectory,
	}
}

func (a *presetsAPI) serve(w http.ResponseWriter, r *http.Request) bool {
	if r.URL.Path == "/api/presets/render" {
		a.serveRender(w, r)
		return true
	}
	if r.URL.Path != "/api/presets" && !strings.HasPrefix(r.URL.Path, "/api/presets/") {
		return false
	}
	store, err := a.presetStore()
	if err != nil {
		writePresetsError(w, http.StatusConflict, err.Error())
		return true
	}
	if r.URL.Path == "/api/presets" {
		a.serveCollection(w, r, store)
		return true
	}
	rest := strings.TrimPrefix(r.URL.Path, "/api/presets/")
	parts := strings.SplitN(rest, "/", 2)
	name := parts[0]
	if name == "" {
		http.NotFound(w, r)
		return true
	}
	if len(parts) == 1 {
		a.servePreset(w, r, store, name)
		return true
	}
	switch parts[1] {
	case "title":
		a.serveTitle(w, r, store, name)
	case "model":
		a.serveModel(w, r, store, name)
	case "runtime":
		a.serveRuntime(w, r, store, name)
	default:
		if strings.HasPrefix(parts[1], "values/") {
			a.serveValue(w, r, store, name, strings.TrimPrefix(parts[1], "values/"))
			return true
		}
		http.NotFound(w, r)
	}
	return true
}

type presetView struct {
	Name    string                   `json:"name"`
	Kind    string                   `json:"kind"`
	Title   string                   `json:"title,omitempty"`
	Model   *presets.ModelBinding    `json:"model,omitempty"`
	Runtime *presetRuntimeView       `json:"runtime,omitempty"`
	Values  []presets.EffectiveValue `json:"values"`
}

type presetRuntimeView struct {
	Declared *presets.RuntimeConstraint `json:"declared,omitempty"`
	Computed presets.ComputedConstraint `json:"computed"`
}

func (a *presetsAPI) view(catalog fields.Catalog, global, p presets.Preset) presetView {
	kind := "model"
	if p.IsGlobal() {
		kind = "global"
	}
	effective := presets.Effective(global, p)
	view := presetView{Name: p.Name, Kind: kind, Title: p.Title, Model: p.Model, Values: effective}
	if !p.IsGlobal() {
		provider := fields.ProviderOfficial
		if p.Runtime != nil && p.Runtime.Provider != "" {
			provider = fields.Provider(p.Runtime.Provider)
		}
		computed := presets.ComputeConstraint(catalog, provider, effective)
		view.Runtime = &presetRuntimeView{Declared: p.Runtime, Computed: computed}
	}
	return view
}

func (a *presetsAPI) catalog() (fields.Catalog, error) {
	store, err := a.fieldStore()
	if err != nil {
		return fields.Catalog{}, err
	}
	return store.Load()
}

func (a *presetsAPI) serveCollection(w http.ResponseWriter, r *http.Request, store *presets.Store) {
	switch r.Method {
	case http.MethodGet:
		doc, err := store.Load()
		if err != nil {
			writePresetsError(w, http.StatusInternalServerError, err.Error())
			return
		}
		catalog, err := a.catalog()
		if err != nil {
			writePresetsError(w, http.StatusInternalServerError, err.Error())
			return
		}
		items := make([]presetView, 0, len(doc.Presets))
		for _, p := range doc.Presets {
			items = append(items, a.view(catalog, doc.Global, p))
		}
		writeRuntimeJSON(w, struct {
			Global  presetView   `json:"global"`
			Presets []presetView `json:"presets"`
		}{a.view(catalog, doc.Global, doc.Global), items})
	case http.MethodPost:
		var body struct {
			Name  string `json:"name"`
			Title string `json:"title,omitempty"`
		}
		if !decodeStrict(w, r, &body) {
			return
		}
		if _, err := store.Define(presets.Preset{Name: body.Name, Title: body.Title}); err != nil {
			writePresetsError(w, http.StatusBadRequest, err.Error())
			return
		}
		doc, err := store.Load()
		if err != nil {
			writePresetsError(w, http.StatusInternalServerError, err.Error())
			return
		}
		catalog, _ := a.catalog()
		p, _ := doc.Find(body.Name)
		writeRuntimeJSON(w, a.view(catalog, doc.Global, p))
	default:
		w.WriteHeader(http.StatusMethodNotAllowed)
	}
}

func (a *presetsAPI) servePreset(w http.ResponseWriter, r *http.Request, store *presets.Store, name string) {
	switch r.Method {
	case http.MethodGet:
		doc, err := store.Load()
		if err != nil {
			writePresetsError(w, http.StatusInternalServerError, err.Error())
			return
		}
		p, ok := doc.Find(name)
		if !ok {
			writePresetsError(w, http.StatusNotFound, "preset not found")
			return
		}
		catalog, _ := a.catalog()
		writeRuntimeJSON(w, a.view(catalog, doc.Global, p))
	case http.MethodDelete:
		if _, err := store.Remove(name); err != nil {
			writePresetsError(w, http.StatusBadRequest, err.Error())
			return
		}
		w.WriteHeader(http.StatusNoContent)
	case "MOVE":
		destination := r.Header.Get("Destination")
		if destination == "" {
			writePresetsError(w, http.StatusBadRequest, "Destination header is required")
			return
		}
		destination = strings.TrimPrefix(destination, "/api/presets/")
		if _, err := store.Rename(name, destination); err != nil {
			writePresetsError(w, http.StatusBadRequest, err.Error())
			return
		}
		w.Header().Set("Location", "/api/presets/"+destination)
		w.WriteHeader(http.StatusNoContent)
	default:
		w.WriteHeader(http.StatusMethodNotAllowed)
	}
}

func (a *presetsAPI) serveTitle(w http.ResponseWriter, r *http.Request, store *presets.Store, name string) {
	if r.Method != http.MethodPut {
		w.WriteHeader(http.StatusMethodNotAllowed)
		return
	}
	var body struct {
		Title string `json:"title"`
	}
	if !decodeStrict(w, r, &body) {
		return
	}
	if _, err := store.SetTitle(name, body.Title); err != nil {
		writePresetsError(w, http.StatusBadRequest, err.Error())
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

func (a *presetsAPI) serveModel(w http.ResponseWriter, r *http.Request, store *presets.Store, name string) {
	if r.Method != http.MethodPut {
		w.WriteHeader(http.StatusMethodNotAllowed)
		return
	}
	var body struct {
		Reference    string `json:"reference"`
		AllowMissing bool   `json:"allowMissing,omitempty"`
	}
	if !decodeStrict(w, r, &body) {
		return
	}
	models, err := a.modelDir()
	if err != nil {
		writePresetsError(w, http.StatusConflict, err.Error())
		return
	}
	manager := modelapp.NewManager(models)
	binding, err := presets.BindModel(manager, body.Reference, body.AllowMissing)
	if err != nil {
		writePresetsError(w, http.StatusBadRequest, err.Error())
		return
	}
	if _, err := store.SetModel(name, binding); err != nil {
		writePresetsError(w, http.StatusBadRequest, err.Error())
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

func (a *presetsAPI) serveRuntime(w http.ResponseWriter, r *http.Request, store *presets.Store, name string) {
	switch r.Method {
	case http.MethodPut:
		var constraint presets.RuntimeConstraint
		if !decodeStrict(w, r, &constraint) {
			return
		}
		if _, err := store.SetRuntime(name, constraint); err != nil {
			writePresetsError(w, http.StatusBadRequest, err.Error())
			return
		}
		w.WriteHeader(http.StatusNoContent)
	case http.MethodDelete:
		if _, err := store.UnsetRuntime(name); err != nil {
			writePresetsError(w, http.StatusBadRequest, err.Error())
			return
		}
		w.WriteHeader(http.StatusNoContent)
	default:
		w.WriteHeader(http.StatusMethodNotAllowed)
	}
}

func (a *presetsAPI) serveValue(w http.ResponseWriter, r *http.Request, store *presets.Store, name, field string) {
	if field == "" {
		http.NotFound(w, r)
		return
	}
	catalog, err := a.catalog()
	if err != nil {
		writePresetsError(w, http.StatusInternalServerError, err.Error())
		return
	}
	switch r.Method {
	case http.MethodPut:
		var body struct {
			Value any `json:"value"`
		}
		if !decodeStrict(w, r, &body) {
			return
		}
		_, validated, err := store.SetValue(catalog, name, field, body.Value)
		if err != nil {
			writePresetsError(w, http.StatusBadRequest, err.Error())
			return
		}
		writeRuntimeJSON(w, struct {
			Field      string `json:"field"`
			Diagnostic string `json:"diagnostic,omitempty"`
		}{validated.Field, validated.Diagnostic})
	case http.MethodDelete:
		if _, err := store.UnsetValue(catalog, name, field); err != nil {
			writePresetsError(w, http.StatusBadRequest, err.Error())
			return
		}
		w.WriteHeader(http.StatusNoContent)
	default:
		w.WriteHeader(http.StatusMethodNotAllowed)
	}
}

func (a *presetsAPI) serveRender(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodGet {
		w.WriteHeader(http.StatusMethodNotAllowed)
		return
	}
	store, err := a.presetStore()
	if err != nil {
		writePresetsError(w, http.StatusConflict, err.Error())
		return
	}
	doc, err := store.Load()
	if err != nil {
		writePresetsError(w, http.StatusInternalServerError, err.Error())
		return
	}
	catalog, err := a.catalog()
	if err != nil {
		writePresetsError(w, http.StatusInternalServerError, err.Error())
		return
	}
	models, err := a.modelDir()
	if err != nil {
		writePresetsError(w, http.StatusConflict, err.Error())
		return
	}
	release := r.URL.Query().Get("runtime")
	result := presets.Render(doc, catalog, fields.ProviderOfficial, release, models)
	for _, diagnostic := range result.Diagnostics {
		w.Header().Add("X-Coquena-Diagnostic", diagnostic)
	}
	w.Header().Set("Content-Type", "text/plain; charset=utf-8")
	_, _ = io.WriteString(w, presets.Format(result))
}

func decodeStrict(w http.ResponseWriter, r *http.Request, v any) bool {
	decoder := json.NewDecoder(r.Body)
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(v); err != nil {
		writePresetsError(w, http.StatusBadRequest, "invalid request body")
		return false
	}
	var extra any
	if err := decoder.Decode(&extra); !errors.Is(err, io.EOF) {
		writePresetsError(w, http.StatusBadRequest, "invalid request body")
		return false
	}
	return true
}

func writePresetsError(w http.ResponseWriter, status int, message string) {
	if message == "" {
		message = "preset request failed"
	}
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(struct {
		Error string `json:"error"`
	}{message})
}
