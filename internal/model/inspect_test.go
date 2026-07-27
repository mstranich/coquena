package model

import (
	"fmt"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"reflect"
	"strconv"
	"strings"
	"testing"
)

func TestInspectRemoteUsesBoundedRanges(t *testing.T) {
	data := emptyGGUF()
	calls := 0
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", strconv.Itoa(len(data)))
			return
		}
		calls++
		rangeHeader := r.Header.Get("Range")
		if rangeHeader == "" {
			t.Fatal("missing range")
		}
		parts := strings.TrimPrefix(rangeHeader, "bytes=")
		var start, end int
		if _, err := fmt.Sscanf(parts, "%d-%d", &start, &end); err != nil {
			t.Fatal(err)
		}
		w.WriteHeader(http.StatusPartialContent)
		_, _ = w.Write(data[start : end+1])
	}))
	defer server.Close()
	m := NewManager(t.TempDir())
	c := Candidate{Source: Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: server.URL}}
	record, err := m.InspectRemote(c)
	if err != nil {
		t.Fatal(err)
	}
	if calls == 0 || record.Source.ResolvedRevision != "abc" {
		t.Fatalf("calls=%d record=%+v", calls, record)
	}
}

// Local and remote inspection read the same header through different readers,
// so an artifact must classify identically either way: nothing about the
// classification may depend on where the bytes came from.
func TestLocalAndRemoteInspectionAgreeOnClassification(t *testing.T) {
	data := classifiableGGUF()
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", strconv.Itoa(len(data)))
			return
		}
		var start, end int
		if _, err := fmt.Sscanf(strings.TrimPrefix(r.Header.Get("Range"), "bytes="), "%d-%d", &start, &end); err != nil {
			t.Fatal(err)
		}
		if end >= len(data) {
			end = len(data) - 1
		}
		w.WriteHeader(http.StatusPartialContent)
		_, _ = w.Write(data[start : end+1])
	}))
	defer server.Close()

	root := t.TempDir()
	partial := filepath.Join(root, "partial")
	if err := os.WriteFile(partial, data, 0600); err != nil {
		t.Fatal(err)
	}
	source := Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: server.URL}
	m := NewManager(filepath.Join(root, "models"))
	published, err := Publish(m.ModelsDir, partial, Record{Source: source, Artifact: Artifact{SizeBytes: int64(len(data))}})
	if err != nil {
		t.Fatal(err)
	}
	local, err := m.InspectLocal(published)
	if err != nil {
		t.Fatal(err)
	}
	remote, err := m.InspectRemote(Candidate{Source: source})
	if err != nil {
		t.Fatal(err)
	}
	if !reflect.DeepEqual(local.Classification, remote.Classification) {
		t.Fatalf("local=%#v remote=%#v", local.Classification, remote.Classification)
	}
	for _, inspection := range []Inspection{local, remote} {
		if _, ok := inspection.Metadata["tokenizer.ggml.tokens"]; ok {
			t.Fatalf("vocabulary array present in %+v", inspection.Metadata)
		}
	}
}
