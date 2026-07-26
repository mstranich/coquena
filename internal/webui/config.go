package webui

import (
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"strings"
)

type configAPI struct{ service ConfigService }

func newConfigAPI(service ConfigService) *configAPI { return &configAPI{service: service} }
func (a *configAPI) serve(w http.ResponseWriter, r *http.Request) bool {
	isModels := r.URL.Path == "/api/config/models-directory"
	isRuntimes := r.URL.Path == "/api/config/runtimes-directory"
	if !isModels && !isRuntimes {
		return false
	}
	if r.Method != http.MethodGet && r.Method != http.MethodPut {
		w.WriteHeader(http.StatusMethodNotAllowed)
		return true
	}
	var path string
	var err error
	if r.Method == http.MethodGet {
		if isRuntimes {
			if runtimeService, ok := a.service.(interface{ RuntimesDirectory() (string, error) }); ok {
				path, err = runtimeService.RuntimesDirectory()
			} else {
				err = errors.New("runtime directory configuration is unavailable")
			}
		} else {
			path, err = a.service.ModelsDirectory()
		}
	} else {
		var body struct {
			Path string `json:"path"`
		}
		d := json.NewDecoder(r.Body)
		d.DisallowUnknownFields()
		if d.Decode(&body) != nil || d.Decode(&struct{}{}) != io.EOF {
			writeConfigError(w, http.StatusBadRequest, "path is required")
			return true
		}
		if isRuntimes {
			if runtimeService, ok := a.service.(interface{ SetRuntimesDirectory(string) (string, error) }); ok {
				path, err = runtimeService.SetRuntimesDirectory(body.Path)
			} else {
				err = errors.New("runtime directory configuration is unavailable")
			}
		} else {
			path, err = a.service.SetModelsDirectory(body.Path)
		}
	}
	if err != nil {
		status := http.StatusBadRequest
		if strings.Contains(err.Error(), "not initialized") {
			status = http.StatusConflict
		}
		writeConfigError(w, status, err.Error())
		return true
	}
	writeRuntimeJSON(w, struct {
		Path string `json:"path"`
	}{path})
	return true
}
func writeConfigError(w http.ResponseWriter, status int, message string) {
	if message == "" {
		message = "configuration request failed"
	}
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(struct {
		Error string `json:"error"`
	}{message})
}
