package main

import (
	"bytes"
	"encoding/json"
	"errors"
	"strings"
	"testing"

	"github.com/mstranich/coquena/internal/installation"
	hostsystem "github.com/mstranich/coquena/internal/system"
)

func fixtureReport() hostsystem.Report {
	return hostsystem.Report{SchemaVersion: 1, OS: hostsystem.OperatingSystem{Family: "windows", Name: "Windows", NativeArchitecture: "amd64"}, ExecutableArchitecture: "amd64", CPU: hostsystem.CPU{Model: "CPU", LogicalProcessors: 8}, Graphics: []hostsystem.GraphicsDevice{}, Backends: []hostsystem.BackendCapability{{Type: "cpu", State: hostsystem.Available}, {Type: "cuda", State: hostsystem.Unavailable}, {Type: "vulkan", State: hostsystem.Unknown}}, Diagnostics: []hostsystem.Diagnostic{}}
}

func TestExecuteSystemHumanDoesNotInitialize(t *testing.T) {
	var stdout, stderr bytes.Buffer
	code := execute([]string{"system"}, &stdout, &stderr, func() (installation.Result, error) { t.Fatal("initialize called"); return installation.Result{}, nil }, func() (hostsystem.Report, error) { return fixtureReport(), nil })
	if code != 0 || stderr.Len() != 0 || !strings.Contains(stdout.String(), "Backends") {
		t.Fatalf("execute = %d, %q, %q", code, stdout.String(), stderr.String())
	}
}

func TestExecuteSystemJSONSucceedsWithMissingBackends(t *testing.T) {
	var stdout, stderr bytes.Buffer
	code := execute([]string{"system", "--json"}, &stdout, &stderr, func() (installation.Result, error) { return installation.Result{}, nil }, func() (hostsystem.Report, error) { return fixtureReport(), nil })
	var value map[string]any
	err := json.Unmarshal(stdout.Bytes(), &value)
	if code != 0 || err != nil || stderr.Len() != 0 {
		t.Fatalf("execute = %d, json error %v, stderr %q", code, err, stderr.String())
	}
}

func TestExecuteSystemRejectsFlag(t *testing.T) {
	var stdout, stderr bytes.Buffer
	code := execute([]string{"system", "--yaml"}, &stdout, &stderr, func() (installation.Result, error) { return installation.Result{}, nil })
	if code == 0 || !strings.Contains(stderr.String(), "usage:") {
		t.Fatalf("execute = %d, stderr %q", code, stderr.String())
	}
}

type commandFailWriter struct{}

func (commandFailWriter) Write([]byte) (int, error) { return 0, errors.New("closed") }
func TestExecuteSystemReportsOutputFailure(t *testing.T) {
	var stderr bytes.Buffer
	code := execute([]string{"system", "--json"}, commandFailWriter{}, &stderr, func() (installation.Result, error) { return installation.Result{}, nil }, func() (hostsystem.Report, error) { return fixtureReport(), nil })
	if code == 0 || !strings.Contains(stderr.String(), "write system JSON") {
		t.Fatalf("execute = %d, stderr %q", code, stderr.String())
	}
}
