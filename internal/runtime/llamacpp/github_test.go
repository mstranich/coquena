package llamacpp

import (
	"context"
	"fmt"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

func TestGitHubClientLatestTagAuthAndFailure(t *testing.T) {
	var paths []string
	s := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		paths = append(paths, r.URL.RequestURI())
		if r.Header.Get("Authorization") != "Bearer secret" {
			t.Error("missing auth")
		}
		if strings.Contains(r.URL.Path, "bad") {
			http.Error(w, "boom", 500)
			return
		}
		fmt.Fprint(w, `{"tag_name":"b10068","draft":false,"prerelease":false,"assets":[]}`)
	}))
	defer s.Close()
	c := NewGitHubClient("secret")
	c.BaseURL = s.URL
	c.HTTP = s.Client()
	if _, e := c.Latest(context.Background()); e != nil {
		t.Fatal(e)
	}
	if _, e := c.ByTag(context.Background(), "b10068"); e != nil {
		t.Fatal(e)
	}
	if len(paths) != 2 || paths[0] != "/releases/latest" || paths[1] != "/releases/tags/b10068" {
		t.Fatalf("%v", paths)
	}
	c.BaseURL = s.URL + "/bad"
	if _, e := c.Latest(context.Background()); e == nil || !strings.Contains(e.Error(), "500") {
		t.Fatalf("%v", e)
	}
}

func TestGitHubClientBoundedHistoryAndPagination(t *testing.T) {
	calls := 0
	s := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		calls++
		if calls == 1 {
			fmt.Fprint(w, `[{"tag_name":"b3"},{"tag_name":"draft","draft":true},{"tag_name":"b2"}]`)
		} else {
			fmt.Fprint(w, `[{"tag_name":"b1"}]`)
		}
	}))
	defer s.Close()
	c := NewGitHubClient("")
	c.BaseURL = s.URL
	c.HTTP = s.Client()
	got, e := c.History(context.Background(), 3)
	if e != nil {
		t.Fatal(e)
	}
	if len(got) != 3 || calls != 2 {
		t.Fatalf("len=%d calls=%d", len(got), calls)
	}
	for _, n := range []int{0, -1, MaxHistory + 1} {
		if _, e = c.History(context.Background(), n); e == nil {
			t.Errorf("accepted %d", n)
		}
	}
}

func TestGitHubClientHonorsContextTimeout(t *testing.T) {
	s := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { time.Sleep(100 * time.Millisecond) }))
	defer s.Close()
	c := NewGitHubClient("")
	c.BaseURL = s.URL
	c.HTTP = s.Client()
	ctx, cancel := context.WithTimeout(context.Background(), time.Millisecond)
	defer cancel()
	if _, e := c.Latest(ctx); e == nil {
		t.Fatal("expected timeout")
	}
}
