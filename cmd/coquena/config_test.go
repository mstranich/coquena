package main

import (
	"bytes"
	"strings"
	"testing"
)

func TestConfigModelsDirectoryReadsWritesAndJSON(t *testing.T) {
	get := func() (string, error) { return "C:/Models", nil }
	set := func(path string) (string, error) { return path, nil }
	var out bytes.Buffer
	if err := runConfig([]string{"models-directory"}, &out, get, set); err != nil || out.String() != "C:/Models\n" {
		t.Fatalf("read = %q, %v", out.String(), err)
	}
	out.Reset()
	if err := runConfig([]string{"models-directory", "D:/Models", "--json"}, &out, get, set); err != nil || !strings.Contains(out.String(), `"path":"D:/Models"`) {
		t.Fatalf("write = %q, %v", out.String(), err)
	}
}
