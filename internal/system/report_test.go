package system

import (
	"bytes"
	"encoding/json"
	"errors"
	"strings"
	"testing"
)

type fakeInventory struct{}

func (fakeInventory) Detect(r *Report) []Diagnostic {
	total := uint64(16 << 30)
	r.OS.Name = "Test OS"
	r.OS.NativeArchitecture = "amd64"
	r.CPU.Model = "Test CPU"
	r.Memory.TotalBytes = &total
	r.Graphics = []GraphicsDevice{{ID: "gpu-b", Name: "GPU B", Source: "fixture"}, {ID: "gpu-a", Name: "GPU A", Source: "fixture"}}
	return nil
}

type fakeProbe struct{ capability BackendCapability }

func (p fakeProbe) Probe(Report) BackendCapability { return p.capability }

func TestInspectNormalizesAndPreservesUnknownValues(t *testing.T) {
	r, err := inspectWith(fakeInventory{}, fakeProbe{BackendCapability{Type: "vulkan", State: Unknown, Diagnostics: []Diagnostic{{Code: "probe-failed", Message: "unknown"}}}}, fakeProbe{BackendCapability{Type: "cuda", State: Unavailable}})
	if err != nil {
		t.Fatal(err)
	}
	if r.Memory.TotalBytes == nil || r.Graphics[0].ID != "gpu-a" || r.Backends[0].Type != "cpu" {
		t.Fatalf("unexpected normalized report: %+v", r)
	}
}

func TestNormalizeRejectsInvalidState(t *testing.T) {
	r := validReport()
	r.Backends = []BackendCapability{{Type: "cuda", State: "maybe"}}
	if err := r.Normalize(); err == nil {
		t.Fatal("expected invalid state")
	}
}

func TestWriteJSONIsOneDeterministicNewlineTerminatedDocument(t *testing.T) {
	r := validReport()
	var a, b bytes.Buffer
	if err := WriteJSON(&a, r); err != nil {
		t.Fatal(err)
	}
	if err := WriteJSON(&b, r); err != nil {
		t.Fatal(err)
	}
	if a.String() != b.String() || !strings.HasSuffix(a.String(), "\n") || strings.Count(a.String(), "\n") != 1 {
		t.Fatalf("unexpected JSON %q", a.String())
	}
	var decoded Report
	if err := json.Unmarshal(a.Bytes(), &decoded); err != nil {
		t.Fatal(err)
	}
}

func TestWriteTextIncludesFactsAndStates(t *testing.T) {
	r := validReport()
	var out bytes.Buffer
	if err := WriteText(&out, r); err != nil {
		t.Fatal(err)
	}
	for _, want := range []string{"Test OS", "Test CPU", "cpu", "available", "cuda", "unavailable"} {
		if !strings.Contains(out.String(), want) {
			t.Fatalf("output %q missing %q", out.String(), want)
		}
	}
}

type failingWriter struct{}

func (failingWriter) Write([]byte) (int, error) { return 0, errors.New("write failed") }
func TestRenderersReportWriteFailure(t *testing.T) {
	r := validReport()
	if err := WriteJSON(failingWriter{}, r); err == nil {
		t.Fatal("expected JSON write error")
	}
	if err := WriteText(failingWriter{}, r); err == nil {
		t.Fatal("expected text write error")
	}
}

func validReport() Report {
	return Report{SchemaVersion: 1, OS: OperatingSystem{Family: "windows", Name: "Test OS", NativeArchitecture: "amd64"}, ExecutableArchitecture: "amd64", CPU: CPU{Model: "Test CPU", LogicalProcessors: 8}, Graphics: []GraphicsDevice{}, Backends: []BackendCapability{{Type: "cpu", State: Available}, {Type: "cuda", State: Unavailable}}, Diagnostics: []Diagnostic{}}
}
