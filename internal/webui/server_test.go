package webui

import (
	"net/http"
	"net/http/httptest"
	"regexp"
	"strings"
	"testing"
)

func TestHandlerHealth(t *testing.T) {
	rec := httptest.NewRecorder()
	Handler().ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/api/health", nil))
	if rec.Code != http.StatusOK || rec.Body.String() != "ok\n" || !strings.HasPrefix(rec.Header().Get("Content-Type"), "text/plain") {
		t.Fatalf("health response = (%d, %q, %q)", rec.Code, rec.Body.String(), rec.Header().Get("Content-Type"))
	}
}

func TestHandlerRejectsHealthMethods(t *testing.T) {
	rec := httptest.NewRecorder()
	Handler().ServeHTTP(rec, httptest.NewRequest(http.MethodPost, "/api/health", nil))
	if rec.Code != http.StatusMethodNotAllowed || strings.Contains(rec.Body.String(), "<!doctype html>") {
		t.Fatalf("health POST response = (%d, %q)", rec.Code, rec.Body.String())
	}
}

func TestHandlerServesRoutesAndAssets(t *testing.T) {
	index := httptest.NewRecorder()
	Handler().ServeHTTP(index, httptest.NewRequest(http.MethodGet, "/", nil))
	for _, path := range []string{"/", "/settings"} {
		rec := httptest.NewRecorder()
		Handler().ServeHTTP(rec, httptest.NewRequest(http.MethodGet, path, nil))
		if rec.Code != http.StatusOK || !strings.Contains(rec.Body.String(), "<app-root>") {
			t.Fatalf("route %s response = (%d, %q)", path, rec.Code, rec.Body.String())
		}
	}
	rec := httptest.NewRecorder()
	Handler().ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/missing.js", nil))
	if rec.Code != http.StatusNotFound {
		t.Fatalf("missing asset status = %d, want 404", rec.Code)
	}
	match := regexp.MustCompile(`src="([^"]+\.js)"`).FindStringSubmatch(index.Body.String())
	if len(match) != 2 {
		t.Fatalf("index does not reference a JavaScript asset")
	}
	asset := httptest.NewRecorder()
	Handler().ServeHTTP(asset, httptest.NewRequest(http.MethodGet, "/"+match[1], nil))
	if asset.Code != http.StatusOK || !strings.HasPrefix(asset.Header().Get("Content-Type"), "text/javascript") {
		t.Fatalf("javascript asset response = (%d, %q)", asset.Code, asset.Header().Get("Content-Type"))
	}
}

func TestHandlerRejectsTraversal(t *testing.T) {
	rec := httptest.NewRecorder()
	req := httptest.NewRequest(http.MethodGet, "/secret", nil)
	req.URL.Path = "/../secret"
	Handler().ServeHTTP(rec, req)
	if rec.Code != http.StatusNotFound {
		t.Fatalf("traversal status = %d, want 404", rec.Code)
	}
}

func TestParsePort(t *testing.T) {
	if port, err := ParsePort(nil); err != nil || port != defaultPort {
		t.Fatalf("default port = (%d, %v)", port, err)
	}
	if _, err := ParsePort([]string{"--host", "0.0.0.0"}); err == nil {
		t.Fatal("host flag unexpectedly accepted")
	}
	if _, err := ParsePort([]string{"--port", "65536"}); err == nil {
		t.Fatal("invalid port unexpectedly accepted")
	}
}
