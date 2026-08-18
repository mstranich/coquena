package model

import (
	"crypto/sha256"
	"encoding/hex"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"testing"
)

func setupInstalledCheckUpdatesFixture(t *testing.T, body []byte, resolvedRevision, sha256Hex, provider string) (*Manager, string) {
	t.Helper()
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	path := writeReconcileFixture(t, root, body)
	source := Source{
		Provider:          provider,
		Repository:        "org/repo",
		RequestedRevision: "main",
		ResolvedRevision:  resolvedRevision,
		Filename:          "m.gguf",
	}
	if provider == "huggingface" {
		source.URL = "https://huggingface.co/org/repo/resolve/" + resolvedRevision + "/m.gguf"
	}
	record := Record{SchemaVersion: RecordSchemaVersion, Source: source, Artifact: Artifact{SizeBytes: int64(len(body))}}
	record.SetIntegrity(sha256Hex, "2026-01-01T00:00:00Z")
	if _, err := PublishInPlace(m.ModelsDir, record); err != nil {
		t.Fatal(err)
	}
	return m, path
}

func TestCheckUpdatesReportsUpToDate(t *testing.T) {
	body := emptyGGUF()
	digest := sha256.Sum256(body)
	sha256 := hex.EncodeToString(digest[:])
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", "24")
			return
		}
		if r.URL.Path != "/api/models/org/repo/revision/main" {
			t.Fatalf("path=%s", r.URL.Path)
		}
		_, _ = w.Write([]byte(`{"sha":"deadbeef","siblings":[{"rfilename":"m.gguf","size":24,"lfs":{"sha256":"` + sha256 + `"}}]}`))
	}))
	defer server.Close()
	m, _ := setupInstalledCheckUpdatesFixture(t, body, "deadbeef", sha256, "huggingface")
	m.Resolver = Resolver{Client: server.Client(), BaseURL: server.URL}

	result, err := m.CheckUpdates("org/repo:m.gguf")
	if err != nil {
		t.Fatal(err)
	}
	if result.Outcome != UpdateCurrent {
		t.Fatalf("outcome=%s want=%s (%+v)", result.Outcome, UpdateCurrent, result)
	}
}

func TestCheckUpdatesReportsUpdateAvailableOnRevisionChange(t *testing.T) {
	body := emptyGGUF()
	digest := sha256.Sum256(body)
	sha256 := hex.EncodeToString(digest[:])
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", "24")
			return
		}
		_, _ = w.Write([]byte(`{"sha":"newsha","siblings":[{"rfilename":"m.gguf","size":24,"lfs":{"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}]}`))
	}))
	defer server.Close()
	m, _ := setupInstalledCheckUpdatesFixture(t, body, "deadbeef", sha256, "huggingface")
	m.Resolver = Resolver{Client: server.Client(), BaseURL: server.URL}

	result, err := m.CheckUpdates("org/repo:m.gguf")
	if err != nil {
		t.Fatal(err)
	}
	if result.Outcome != UpdateAvailable {
		t.Fatalf("outcome=%s want=%s (%+v)", result.Outcome, UpdateAvailable, result)
	}
	if result.CurrentRevision != "newsha" {
		t.Fatalf("current revision=%q want=newsha", result.CurrentRevision)
	}
}

func TestCheckUpdatesReportsUpdateAvailableOnSameRevisionContentChange(t *testing.T) {
	body := emptyGGUF()
	digest := sha256.Sum256(body)
	localSHA256 := hex.EncodeToString(digest[:])
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", "24")
			return
		}
		_, _ = w.Write([]byte(`{"sha":"deadbeef","siblings":[{"rfilename":"m.gguf","size":24,"lfs":{"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}]}`))
	}))
	defer server.Close()
	m, _ := setupInstalledCheckUpdatesFixture(t, body, "deadbeef", localSHA256, "huggingface")
	m.Resolver = Resolver{Client: server.Client(), BaseURL: server.URL}

	result, err := m.CheckUpdates("org/repo:m.gguf")
	if err != nil {
		t.Fatal(err)
	}
	if result.Outcome != UpdateAvailable || result.RecordedRevision != result.CurrentRevision {
		t.Fatalf("result=%+v", result)
	}
}

func TestCheckUpdatesReportsPartialWhenNoSHA256Available(t *testing.T) {
	body := emptyGGUF()
	digest := sha256.Sum256(body)
	sha256 := hex.EncodeToString(digest[:])
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", "24")
			return
		}
		_, _ = w.Write([]byte(`{"sha":"deadbeef","siblings":[{"rfilename":"m.gguf","size":24}]}`))
	}))
	defer server.Close()
	m, _ := setupInstalledCheckUpdatesFixture(t, body, "deadbeef", sha256, "huggingface")
	m.Resolver = Resolver{Client: server.Client(), BaseURL: server.URL}

	result, err := m.CheckUpdates("org/repo:m.gguf")
	if err != nil {
		t.Fatal(err)
	}
	if result.Outcome != UpdatePartial {
		t.Fatalf("outcome=%s want=%s (%+v)", result.Outcome, UpdatePartial, result)
	}
}

func TestCheckUpdatesRefusesLocalProviderWithoutNetworkAccess(t *testing.T) {
	body := emptyGGUF()
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		t.Fatal("check-updates must not make any HTTP request for a local-provider entry")
	}))
	defer server.Close()
	m, _ := setupInstalledCheckUpdatesFixture(t, body, "", "", "local")
	m.Resolver = Resolver{Client: server.Client(), BaseURL: server.URL}

	_, err := m.CheckUpdates("org/repo:m.gguf")
	if err == nil {
		t.Fatal("expected refusal for local-provider entry")
	}
}
