package store

import (
	"archive/tar"
	"archive/zip"
	"compress/gzip"
	"context"
	"crypto/sha256"
	"encoding/json"
	"errors"
	"fmt"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
)

func layout(t *testing.T) Layout {
	t.Helper()
	r := t.TempDir()
	for _, p := range []string{"config", "downloads", "tmp", "runtimes"} {
		if e := os.MkdirAll(filepath.Join(r, p), 0755); e != nil {
			t.Fatal(e)
		}
	}
	os.WriteFile(filepath.Join(r, "config", "coquena.json"), []byte("{\"schemaVersion\":1}\n"), 0644)
	l, e := Open(r)
	if e != nil {
		t.Fatal(e)
	}
	return l
}

func TestDownloadVerifiesDigestAndBound(t *testing.T) {
	l := layout(t)
	body := []byte("runtime")
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { w.Write(body) }))
	defer srv.Close()
	sum := sha256.Sum256(body)
	a := Asset{Name: "x.zip", URL: srv.URL, Digest: fmt.Sprintf("sha256:%x", sum), Size: int64(len(body))}
	p, e := Download(context.Background(), srv.Client(), l, a, Limits{MaxDownloadBytes: 20})
	if e != nil {
		t.Fatal(e)
	}
	defer os.Remove(p)
	a.Digest = "sha256:" + strings.Repeat("0", 64)
	if _, e = Download(context.Background(), srv.Client(), l, a, Limits{}); e == nil {
		t.Fatal("wanted digest failure")
	}
	a.Digest = fmt.Sprintf("sha256:%x", sum)
	if _, e = Download(context.Background(), srv.Client(), l, a, Limits{MaxDownloadBytes: 2}); e == nil {
		t.Fatal("wanted bound failure")
	}
}

func TestDownloadReportsWrittenBytes(t *testing.T) {
	l := layout(t)
	body := []byte("runtime-progress")
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) { _, _ = w.Write(body) }))
	defer server.Close()
	sum := sha256.Sum256(body)
	asset := Asset{Name: "x.zip", URL: server.URL, Digest: fmt.Sprintf("sha256:%x", sum), Size: int64(len(body))}
	var reports []int64
	path, err := DownloadWithProgress(context.Background(), server.Client(), l, asset, Limits{}, func(written int64) {
		reports = append(reports, written)
	})
	if err != nil {
		t.Fatal(err)
	}
	defer os.Remove(path)
	if len(reports) == 0 || reports[len(reports)-1] != int64(len(body)) {
		t.Fatalf("reports=%v", reports)
	}
}

func TestZIPRejectsTraversalDuplicateAndLink(t *testing.T) {
	for _, tc := range []struct {
		name string
		add  func(*zip.Writer)
	}{{"traversal", func(w *zip.Writer) { f, _ := w.Create("../escape"); f.Write([]byte("x")) }}, {"duplicate", func(w *zip.Writer) {
		for i := 0; i < 2; i++ {
			f, _ := w.Create("x")
			f.Write([]byte("x"))
		}
	}}, {"symlink", func(w *zip.Writer) {
		h := &zip.FileHeader{Name: "x"}
		h.SetMode(os.ModeSymlink | 0777)
		w.CreateHeader(h)
	}}} {
		t.Run(tc.name, func(t *testing.T) {
			p := filepath.Join(t.TempDir(), "a.zip")
			f, _ := os.Create(p)
			w := zip.NewWriter(f)
			tc.add(w)
			w.Close()
			f.Close()
			if e := ExtractZIP(p, t.TempDir(), Limits{}); e == nil {
				t.Fatal("wanted rejection")
			}
		})
	}
}

func TestTarRejectsTraversalAndLinks(t *testing.T) {
	for _, typ := range []byte{tar.TypeReg, tar.TypeSymlink} {
		t.Run(fmt.Sprint(typ), func(t *testing.T) {
			p := filepath.Join(t.TempDir(), "a.tgz")
			f, _ := os.Create(p)
			gz := gzip.NewWriter(f)
			w := tar.NewWriter(gz)
			name := "link"
			if typ == tar.TypeReg {
				name = "../escape"
			}
			w.WriteHeader(&tar.Header{Name: name, Typeflag: typ, Size: 0, Mode: 0644})
			w.Close()
			gz.Close()
			f.Close()
			if e := ExtractTarGZ(p, t.TempDir(), Limits{}); e == nil {
				t.Fatal("wanted rejection")
			}
		})
	}
}

func TestPublishScanIdempotenceConflictAndRemove(t *testing.T) {
	l := layout(t)
	stage, _ := NewStage(l)
	os.WriteFile(filepath.Join(stage, "llama-server"), []byte("x"), 0755)
	m := Metadata{ID: "llama-b1-bin-win-cpu-x64.zip", Identity: Identity{Release: "b1"}}
	already, e := Publish(l, stage, m)
	if e != nil || already {
		t.Fatalf("publish=%v,%v", already, e)
	}
	stage2, _ := NewStage(l)
	if already, e = Publish(l, stage2, m); e != nil || !already {
		t.Fatalf("idempotent=%v,%v", already, e)
	}
	os.RemoveAll(stage2)
	entries, diags, e := Scan(l)
	if e != nil || len(entries) != 1 || len(diags) != 0 {
		t.Fatalf("scan=%+v %+v %v", entries, diags, e)
	}
	if e = Remove(l, m.ID); e != nil {
		t.Fatal(e)
	}
	entries, _, _ = Scan(l)
	if len(entries) != 0 {
		t.Fatal("not removed")
	}
}

func TestPublishPreservesConflict(t *testing.T) {
	l := layout(t)
	id := "asset.zip"
	dest := filepath.Join(l.Runtimes, dirName(id))
	os.MkdirAll(dest, 0755)
	os.WriteFile(filepath.Join(dest, "keep"), []byte("yes"), 0644)
	stage, _ := NewStage(l)
	if _, e := Publish(l, stage, Metadata{ID: id}); e == nil {
		t.Fatal("wanted conflict")
	}
	if _, e := os.Stat(filepath.Join(dest, "keep")); e != nil {
		t.Fatal("conflict was changed")
	}
}

func TestLockIsFailFast(t *testing.T) {
	l := layout(t)
	a, e := Acquire(l)
	if e != nil {
		t.Fatal(e)
	}
	defer a.Release()
	if _, e = Acquire(l); e != ErrBusy {
		t.Fatalf("got %v", e)
	}
}

func TestValidateID(t *testing.T) {
	for _, id := range []string{"", "../x", "a/b", "a\\b", ".."} {
		if ValidateID(id) == nil {
			t.Fatalf("accepted %q", id)
		}
	}
	if e := ValidateID("llama-b1.zip"); e != nil {
		t.Fatal(e)
	}
}

func FuzzRuntimePathAndMetadataInputs(f *testing.F) {
	f.Add("llama-b1-bin-win-cpu-x64.zip")
	f.Add("../escape")
	f.Fuzz(func(t *testing.T, value string) {
		_ = ValidateID(value)
		_, _ = safeArchivePath(t.TempDir(), value)
		var metadata Metadata
		_ = json.Unmarshal([]byte(value), &metadata)
	})
}

func TestExtractSuccess(t *testing.T) {
	p := filepath.Join(t.TempDir(), "a.zip")
	f, _ := os.Create(p)
	w := zip.NewWriter(f)
	x, _ := w.Create("bin/llama-server" + map[bool]string{true: ".exe"}[runtime.GOOS == "windows"])
	x.Write([]byte("x"))
	w.Close()
	f.Close()
	d := t.TempDir()
	if e := ExtractZIP(p, d, Limits{}); e != nil {
		t.Fatal(e)
	}
	if _, e := os.Stat(filepath.Join(d, "bin", "llama-server"+map[bool]string{true: ".exe"}[runtime.GOOS == "windows"])); e != nil {
		t.Fatal(e)
	}
}

func TestProbeServerRetriesTransientFailure(t *testing.T) {
	attempts := 0
	err := probeServer(context.Background(), "llama-server", 3, func(context.Context, string) (string, error) {
		attempts++
		if attempts < 3 {
			return "temporary loader failure", errors.New("exit status 1")
		}
		return "version 1", nil
	})
	if err != nil || attempts != 3 {
		t.Fatalf("probe = (%v, %d attempts)", err, attempts)
	}
}

func TestProbeServerIncludesBoundedCommandOutput(t *testing.T) {
	err := probeServer(context.Background(), "llama-server", 1, func(context.Context, string) (string, error) {
		return strings.Repeat("x", 5000), errors.New("exit status 1")
	})
	if err == nil || !strings.Contains(err.Error(), "output:") || len(err.Error()) > 4300 {
		t.Fatalf("probe error = %v", err)
	}
}
