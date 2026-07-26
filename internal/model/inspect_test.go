package model

import (
	"fmt"
	"net/http"
	"net/http/httptest"
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
