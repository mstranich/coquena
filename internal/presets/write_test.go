package presets

import (
	"os"
	"path/filepath"
	"testing"
)

func TestDefaultRenderPath(t *testing.T) {
	got := DefaultRenderPath("/home", "llama-b6234-bin-win-cuda-12.4-x64.zip")
	want := filepath.Join("/home", "data", "servers", "llama-b6234-bin-win-cuda-12.4-x64.zip.ini")
	if got != want {
		t.Fatalf("got=%q want=%q", got, want)
	}
}

func TestWriteRenderingCreatesParentDir(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "servers", "runtime.ini")
	if err := WriteRendering(path, "[*]\n"); err != nil {
		t.Fatal(err)
	}
	data, err := os.ReadFile(path)
	if err != nil || string(data) != "[*]\n" {
		t.Fatalf("data=%q err=%v", data, err)
	}
}

func TestWriteRenderingToSuppliedPath(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "custom.ini")
	if err := WriteRendering(path, "[*]\n"); err != nil {
		t.Fatal(err)
	}
	if _, err := os.Stat(filepath.Join(dir, "data")); !os.IsNotExist(err) {
		t.Fatal("expected nothing written beneath a default data/ directory")
	}
}
