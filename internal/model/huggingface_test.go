package model

import (
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
)

func TestResolverFiltersPinsAndSelectsGGUF(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", "43")
			w.Header().Set("ETag", `"artifact"`)
			return
		}
		if r.URL.Path != "/api/models/org/repo/revision/main" {
			t.Fatalf("path=%s", r.URL.Path)
		}
		_, _ = w.Write([]byte(`{"sha":"012345","siblings":[{"rfilename":"a.txt","size":1},{"rfilename":"dir/model.Q4.gguf","size":42},{"rfilename":"model.Q8.GGUF","size":84}]}`))
	}))
	defer server.Close()
	resolver := Resolver{Client: server.Client(), BaseURL: server.URL}
	candidates, err := resolver.Resolve("org/repo", "q4")
	if err != nil {
		t.Fatal(err)
	}
	if len(candidates) != 1 {
		t.Fatalf("candidates=%+v", candidates)
	}
	got := candidates[0]
	if got.Source.RequestedRevision != "main" || got.Source.ResolvedRevision != "012345" || !strings.Contains(got.Source.URL, "/resolve/012345/dir/model.Q4.gguf") {
		t.Fatalf("source=%+v", got.Source)
	}
	if got.Artifact.SizeBytes != 43 || got.Artifact.ETag != `"artifact"` {
		t.Fatalf("size=%d", got.Artifact.SizeBytes)
	}
}

func TestResolverRejectsAmbiguousSelector(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_, _ = w.Write([]byte(`{"sha":"abc","siblings":[{"rfilename":"a.gguf"},{"rfilename":"b.gguf"}]}`))
	}))
	defer server.Close()
	_, err := (Resolver{Client: server.Client(), BaseURL: server.URL}).Resolve("org/repo", "gguf")
	if err == nil || !strings.Contains(err.Error(), "resolves to 2") {
		t.Fatalf("err=%v", err)
	}
}
func TestParseCanonicalFileURL(t *testing.T) {
	repo, revision, file, err := parseRef("https://huggingface.co/org/repo/resolve/v1/path/model.gguf", "")
	if err != nil || repo != "org/repo" || revision != "v1" || file != "path/model.gguf" {
		t.Fatalf("%q %q %q %v", repo, revision, file, err)
	}
}

func TestParseRefShortFormPinnedRevision(t *testing.T) {
	repo, revision, file, err := parseRef("org/repo@v2.1:Q4", "")
	if err != nil || repo != "org/repo" || revision != "v2.1" || file != "Q4" {
		t.Fatalf("%q %q %q %v", repo, revision, file, err)
	}
}

func TestParseRefShortFormDefaultsToMain(t *testing.T) {
	repo, revision, file, err := parseRef("org/repo:Q4", "")
	if err != nil || repo != "org/repo" || revision != "main" || file != "Q4" {
		t.Fatalf("%q %q %q %v", repo, revision, file, err)
	}
}

func TestResolverExactFilenameSelectorMatchesOne(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			return
		}
		_, _ = w.Write([]byte(`{"sha":"abc","siblings":[{"rfilename":"model.Q4.gguf"},{"rfilename":"model.Q4-imatrix.gguf"}]}`))
	}))
	defer server.Close()
	candidates, err := (Resolver{Client: server.Client(), BaseURL: server.URL}).Resolve("org/repo", "model.Q4.gguf")
	if err != nil {
		t.Fatal(err)
	}
	if len(candidates) != 1 || candidates[0].Source.Filename != "model.Q4.gguf" {
		t.Fatalf("candidates=%+v", candidates)
	}
}

func TestResolverExactFilenameSelectorMatchesNone(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_, _ = w.Write([]byte(`{"sha":"abc","siblings":[{"rfilename":"model.Q4.gguf"}]}`))
	}))
	defer server.Close()
	_, err := (Resolver{Client: server.Client(), BaseURL: server.URL}).Resolve("org/repo", "model.Q8_0.gguf")
	if err == nil || !strings.Contains(err.Error(), "resolves to 0") {
		t.Fatalf("err=%v", err)
	}
}
