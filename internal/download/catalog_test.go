package download

import (
	"os"
	"path/filepath"
	"testing"
)

func TestListScansModelAndRuntimeSidecars(t *testing.T) {
	root := t.TempDir()
	models := filepath.Join(root, "models")
	runtimes := filepath.Join(root, "runtimes")
	if err := os.MkdirAll(models, 0755); err != nil {
		t.Fatal(err)
	}
	if err := os.MkdirAll(runtimes, 0755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(models, "m.gguf.download.json"), []byte(`{"id":"m1","source":{"repository":"org/repo","filename":"m.gguf"},"status":"paused","downloadedBytes":2,"expectedSizeBytes":4}`), 0644); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(runtimes, "r.download.json"), []byte(`{"id":"r1","name":"r","status":"downloading","downloadedBytes":3,"expectedBytes":6}`), 0644); err != nil {
		t.Fatal(err)
	}
	entries, err := List(models, runtimes)
	if err != nil {
		t.Fatal(err)
	}
	if len(entries) != 2 || entries[0].Type != "model" || entries[1].Type != "runtime" {
		t.Fatalf("entries=%+v", entries)
	}
	if entries[0].Progress != 2 || entries[1].Total != 6 {
		t.Fatalf("progress=%+v", entries)
	}
	if entries[0].Total != 4 || entries[0].Name != "org/repo:m.gguf" {
		t.Fatalf("model entry=%+v", entries[0])
	}
}

func TestListReturnsJSONArrayWhenCatalogIsEmpty(t *testing.T) {
	entries, err := List("", "")
	if err != nil {
		t.Fatal(err)
	}
	if entries == nil || len(entries) != 0 {
		t.Fatalf("entries = %#v, want non-nil empty slice", entries)
	}
}
