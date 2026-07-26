// Package download contains the resource-neutral view of durable transfers.
package download

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
)

type Entry struct {
	ID       string `json:"id"`
	Type     string `json:"type"`
	Name     string `json:"name"`
	Status   string `json:"status"`
	Progress int64  `json:"downloadedBytes"`
	Total    int64  `json:"expectedBytes"`
	Path     string `json:"path"`
	Error    string `json:"error,omitempty"`
}

func List(modelRoot, runtimeRoot string) ([]Entry, error) {
	// Public JSON APIs must encode an empty catalog as [] rather than null;
	// Angular iterates this value and a null result aborts rendering of the
	// remainder of the Runtimes page, including remote distributions.
	out := make([]Entry, 0)
	if err := scan(modelRoot, "model", ".gguf.download.json", &out); err != nil {
		return nil, err
	}
	if err := scan(runtimeRoot, "runtime", ".download.json", &out); err != nil {
		return nil, err
	}
	return out, nil
}

func scan(root, kind, suffix string, out *[]Entry) error {
	if root == "" {
		return nil
	}
	return filepath.WalkDir(root, func(path string, d os.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if d.IsDir() || !strings.HasSuffix(strings.ToLower(d.Name()), suffix) {
			return nil
		}
		b, err := os.ReadFile(path)
		if err != nil {
			return nil
		}
		var raw map[string]any
		if json.Unmarshal(b, &raw) != nil {
			return nil
		}
		e := Entry{Type: kind, Path: path}
		if v, ok := raw["id"].(string); ok {
			e.ID = v
		}
		if v, ok := raw["name"].(string); ok {
			e.Name = v
		}
		if v, ok := raw["status"].(string); ok {
			e.Status = v
		}
		if v, ok := raw["error"].(string); ok {
			e.Error = v
		}
		if v, ok := raw["downloadedBytes"].(float64); ok {
			e.Progress = int64(v)
		}
		if v, ok := raw["expectedBytes"].(float64); ok {
			e.Total = int64(v)
		}
		// Model jobs predate the shared catalog and retain their public
		// `expectedSizeBytes` field. Normalize it at the catalog boundary so
		// global and resource-specific views render identical progress.
		if e.Total == 0 {
			if v, ok := raw["expectedSizeBytes"].(float64); ok {
				e.Total = int64(v)
			}
		}
		if kind == "model" {
			if source, ok := raw["source"].(map[string]any); ok {
				repository, _ := source["repository"].(string)
				filename, _ := source["filename"].(string)
				if repository != "" && filename != "" {
					e.Name = repository + ":" + filename
				}
			}
		}
		if e.Name == "" {
			e.Name = filepath.Base(strings.TrimSuffix(path, suffix))
		}
		if e.ID == "" {
			e.ID = e.Name
		}
		*out = append(*out, e)
		return nil
	})
}
