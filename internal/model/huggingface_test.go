package model

import (
	"errors"
	"net/http"
	"net/http/httptest"
	"slices"
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
		if r.URL.Query().Get("blobs") != "true" {
			t.Fatalf("query=%s", r.URL.RawQuery)
		}
		_, _ = w.Write([]byte(`{"sha":"012345","siblings":[{"rfilename":"a.txt","size":1},{"rfilename":"dir/model.Q4.gguf","size":42,"lfs":{"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}},{"rfilename":"model.Q8.GGUF","size":84}]}`))
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
	if got.Artifact.SizeBytes != 43 || got.ETag != `"artifact"` {
		t.Fatalf("size=%d etag=%q", got.Artifact.SizeBytes, got.ETag)
	}
	if got.ContentSHA256 != strings.Repeat("a", 64) {
		t.Fatalf("content sha256=%q", got.ContentSHA256)
	}
}

func TestResolverDistinguishesAbsentAndMalformedContentSHA256(t *testing.T) {
	t.Run("absent", func(t *testing.T) {
		server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			if r.Method == http.MethodHead {
				return
			}
			_, _ = w.Write([]byte(`{"sha":"abc","siblings":[{"rfilename":"model.gguf"}]}`))
		}))
		defer server.Close()
		candidates, err := (Resolver{Client: server.Client(), BaseURL: server.URL}).Resolve("org/repo", "model.gguf")
		if err != nil {
			t.Fatal(err)
		}
		if len(candidates) != 1 || candidates[0].ContentSHA256 != "" {
			t.Fatalf("candidates=%+v", candidates)
		}
	})

	t.Run("malformed", func(t *testing.T) {
		server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			_, _ = w.Write([]byte(`{"sha":"abc","siblings":[{"rfilename":"model.gguf","lfs":{"sha256":"not-a-sha256"}}]}`))
		}))
		defer server.Close()
		_, err := (Resolver{Client: server.Client(), BaseURL: server.URL}).Resolve("org/repo", "model.gguf")
		if err == nil || !strings.Contains(err.Error(), `artifact "model.gguf"`) || !strings.Contains(err.Error(), "invalid lfs.sha256") {
			t.Fatalf("err=%v", err)
		}
	})
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
	// The diagnostic must name the artifacts so a caller can list them.
	var candidates *ErrSelectorCandidates
	if !errors.As(err, &candidates) {
		t.Fatalf("err=%T", err)
	}
	if !slices.Equal(candidates.Matched, []string{"a.gguf", "b.gguf"}) {
		t.Fatalf("matched=%v", candidates.Matched)
	}
	for _, want := range []string{"a.gguf", "b.gguf"} {
		if !strings.Contains(candidates.Error(), want) {
			t.Fatalf("error=%q missing %q", candidates.Error(), want)
		}
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
	// A zero-match selector still names what the repository does offer.
	var candidates *ErrSelectorCandidates
	if !errors.As(err, &candidates) {
		t.Fatalf("err=%T", err)
	}
	if len(candidates.Matched) != 0 || !slices.Equal(candidates.Available, []string{"model.Q4.gguf"}) {
		t.Fatalf("matched=%v available=%v", candidates.Matched, candidates.Available)
	}
	if !strings.Contains(candidates.Error(), "model.Q4.gguf") {
		t.Fatalf("error=%q", candidates.Error())
	}
}
