package model

import (
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"testing"
)

func TestManagerStoresPublisherAvatarOnceAtNamespaceLevel(t *testing.T) {
	requests := 0
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/api/avatars/org" {
			http.NotFound(w, r)
			return
		}
		requests++
		_, _ = w.Write(append([]byte("RIFF\x04\x00\x00\x00WEBP"), []byte("data")...))
	}))
	defer server.Close()
	m := NewManager(filepath.Join(t.TempDir(), "models"))
	m.Resolver.BaseURL = server.URL
	m.Client = server.Client()
	first, err := m.EnsurePublisherAvatar("org/one")
	if err != nil {
		t.Fatal(err)
	}
	second, err := m.EnsurePublisherAvatar("org/two")
	if err != nil {
		t.Fatal(err)
	}
	if first != second || filepath.Base(first) != "publisher-avatar.webp" || requests != 1 {
		t.Fatalf("first=%q second=%q requests=%d", first, second, requests)
	}
	if _, err = os.Stat(first); err != nil {
		t.Fatal(err)
	}
}
