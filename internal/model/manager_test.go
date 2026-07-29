package model

import (
	"bytes"
	"context"
	"crypto/sha256"
	"encoding/binary"
	"encoding/hex"
	"fmt"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

func emptyGGUF() []byte {
	b := make([]byte, 24)
	copy(b, "GGUF")
	binary.LittleEndian.PutUint32(b[4:], 3)
	return b
}
func TestManagerCreatesAndRecoversJob(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	c := Candidate{Source: Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: "https://example.test/abc/m.gguf"}, Artifact: Artifact{SizeBytes: 24}}
	j, e := m.Create(c)
	if e != nil {
		t.Fatal(e)
	}
	j.Status = Downloading
	if e = m.save(j); e != nil {
		t.Fatal(e)
	}
	m = NewManager(filepath.Join(root, "models"))
	jobs, e := m.List()
	if e != nil || len(jobs) != 1 || jobs[0].Status != Paused {
		t.Fatalf("List=%+v err=%v", jobs, e)
	}
}
func TestManagerDownloadsAndPublishes(t *testing.T) {
	body := emptyGGUF()
	s := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", "24")
			return
		}
		w.Write(body)
	}))
	defer s.Close()
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	c := Candidate{Source: Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: s.URL}, Artifact: Artifact{SizeBytes: 24}}
	j, e := m.Create(c)
	if e != nil {
		t.Fatal(e)
	}
	got, e := m.Resume(context.Background(), j.ID, nil)
	if e != nil {
		t.Fatal(e)
	}
	if got.Status != Completed {
		t.Fatalf("status=%s", got.Status)
	}
	target := filepath.Join(root, "models", "org", "repo", "m.gguf")
	if _, e = os.Stat(target); e != nil {
		t.Fatal(e)
	}
	if _, e = ReadRecord(target); e != nil {
		t.Fatal(e)
	}
}
func TestManagerResumeRejectsChangedValidator(t *testing.T) {
	body := emptyGGUF()
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Header.Get("Range") == "bytes=4-" {
			w.Header().Set("ETag", "new")
			w.Header().Set("Content-Range", "bytes 4-23/24")
			w.WriteHeader(http.StatusPartialContent)
			_, _ = w.Write(body[4:])
			return
		}
		w.Header().Set("ETag", "old")
		_, _ = w.Write(body)
	}))
	defer server.Close()
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	c := Candidate{Source: Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: server.URL}, Artifact: Artifact{SizeBytes: 24, ETag: "old"}}
	j, err := m.Create(c)
	if err != nil {
		t.Fatal(err)
	}
	if err = os.WriteFile(j.PartialPath, body[:4], 0600); err != nil {
		t.Fatal(err)
	}
	j.DownloadedBytes = 4
	j.Status = Paused
	if err = m.save(j); err != nil {
		t.Fatal(err)
	}
	got, err := m.Resume(context.Background(), j.ID, nil)
	if err == nil || got.Status != Failed || !strings.Contains(got.Error, "validator changed") {
		t.Fatalf("job=%+v err=%v", got, err)
	}
}
func TestManagerResumeRejectsUnsupportedRange(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { _, _ = w.Write(emptyGGUF()) }))
	defer server.Close()
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	c := Candidate{Source: Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: server.URL}, Artifact: Artifact{SizeBytes: 24}}
	j, err := m.Create(c)
	if err != nil {
		t.Fatal(err)
	}
	if err = os.WriteFile(j.PartialPath, []byte("GGUF"), 0600); err != nil {
		t.Fatal(err)
	}
	j.DownloadedBytes = 4
	j.Status = Paused
	if err = m.save(j); err != nil {
		t.Fatal(err)
	}
	got, err := m.Resume(context.Background(), j.ID, nil)
	if err == nil || got.Status != Failed {
		t.Fatalf("job=%+v err=%v", got, err)
	}
}
func TestManagerRetriesTransientResponse(t *testing.T) {
	body := emptyGGUF()
	attempts := 0
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		attempts++
		if attempts < 3 {
			http.Error(w, "temporary", http.StatusServiceUnavailable)
			return
		}
		_, _ = w.Write(body)
	}))
	defer server.Close()
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	c := Candidate{Source: Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: server.URL}, Artifact: Artifact{SizeBytes: int64(len(body))}}
	j, err := m.Create(c)
	if err != nil {
		t.Fatal(err)
	}
	got, err := m.Resume(context.Background(), j.ID, nil)
	if err != nil || got.Status != Completed || attempts != 3 {
		t.Fatalf("job=%+v err=%v attempts=%d", got, err, attempts)
	}
}
func TestManagerRejectsPartialSizeMismatch(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	c := Candidate{Source: Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: "https://example.test"}, Artifact: Artifact{SizeBytes: 24}}
	j, err := m.Create(c)
	if err != nil {
		t.Fatal(err)
	}
	if err = os.WriteFile(j.PartialPath, []byte("bad"), 0600); err != nil {
		t.Fatal(err)
	}
	j.DownloadedBytes = 4
	j.Status = Paused
	_ = m.save(j)
	got, err := m.Resume(context.Background(), j.ID, nil)
	if err == nil || got.Status != Failed {
		t.Fatalf("job=%+v err=%v", got, err)
	}
}
func TestManagerPauseAndCancel(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	c := Candidate{Source: Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: "https://example.test"}}
	j, err := m.Create(c)
	if err != nil {
		t.Fatal(err)
	}
	paused, err := m.Pause(j.ID)
	if err != nil || paused.Status != Paused {
		t.Fatalf("paused=%+v err=%v", paused, err)
	}
	if err = m.Cancel(j.ID); err != nil {
		t.Fatal(err)
	}
	if _, err = m.Status(j.ID); !os.IsNotExist(err) {
		t.Fatalf("status error=%v", err)
	}
}
func TestManagerPersistsProgressWithoutCallback(t *testing.T) {
	body := append(emptyGGUF(), make([]byte, 1024)...)
	second := make(chan struct{})
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Length", fmt.Sprint(len(body)))
		w.WriteHeader(http.StatusOK)
		_, _ = w.Write(body[:24])
		if f, ok := w.(http.Flusher); ok {
			f.Flush()
		}
		time.Sleep(300 * time.Millisecond)
		_, _ = w.Write(body[24:100])
		if f, ok := w.(http.Flusher); ok {
			f.Flush()
		}
		close(second)
		time.Sleep(500 * time.Millisecond)
		_, _ = w.Write(body[100:])
	}))
	defer server.Close()
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	job, err := m.Create(Candidate{Source: Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: server.URL}, Artifact: Artifact{SizeBytes: int64(len(body))}})
	if err != nil {
		t.Fatal(err)
	}
	done := make(chan struct{})
	go func() { _, _ = m.Resume(context.Background(), job.ID, nil); close(done) }()
	<-second
	time.Sleep(50 * time.Millisecond)
	persisted, err := m.Status(job.ID)
	if err != nil {
		t.Fatal(err)
	}
	if persisted.DownloadedBytes == 0 {
		t.Fatalf("progress was not persisted: %+v", persisted)
	}
	_, _ = m.Pause(job.ID)
	<-done
}
func TestManagerDiscoversMissingExpectedSize(t *testing.T) {
	body := emptyGGUF()
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { _, _ = w.Write(body) }))
	defer server.Close()
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	job, err := m.Create(Candidate{Source: Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: server.URL}})
	if err != nil {
		t.Fatal(err)
	}
	completed, err := m.Resume(context.Background(), job.ID, nil)
	if err != nil {
		t.Fatal(err)
	}
	if completed.ExpectedSizeBytes != int64(len(body)) {
		t.Fatalf("expectedSizeBytes=%d", completed.ExpectedSizeBytes)
	}
}
func TestManagerWritesSharedOperationLogs(t *testing.T) {
	root := t.TempDir()
	var logs bytes.Buffer
	m := NewManager(filepath.Join(root, "models"))
	m.LogWriter = &logs
	job, err := m.Create(Candidate{Source: Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: "https://example.test"}})
	if err != nil {
		t.Fatal(err)
	}
	if _, err = m.Pause(job.ID); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(logs.String(), "model download created") || !strings.Contains(logs.String(), "pause requested") {
		t.Fatalf("logs=%q", logs.String())
	}
}
func TestHumanDownloadProgressFormatting(t *testing.T) {
	if got := HumanBytes(3202867216); got != "3.0 GiB" {
		t.Fatalf("humanBytes=%q", got)
	}
	if got := DownloadPercent(3202867216, 21166757760); got < 15.1 || got >= 15.2 {
		t.Fatalf("downloadPercent=%f", got)
	}
}
func TestManagerStoresAndRecoversDownloadBesideModel(t *testing.T) {
	root := t.TempDir()
	models := filepath.Join(root, "models")
	m := NewManager(models)
	candidate := Candidate{Source: Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: "https://example.test"}, Artifact: Artifact{SizeBytes: 24}}
	job, err := m.Create(candidate)
	if err != nil {
		t.Fatal(err)
	}
	wantTarget := filepath.Join(models, "org", "repo", "m.gguf")
	if job.PartialPath != wantTarget+".partial" {
		t.Fatalf("partial=%q", job.PartialPath)
	}
	if _, err = os.Stat(wantTarget + ".download.json"); err != nil {
		t.Fatal(err)
	}
	job.Status = Downloading
	if err = m.save(job); err != nil {
		t.Fatal(err)
	}
	restarted := NewManager(models)
	entries, err := restarted.Library()
	if err != nil || len(entries) != 1 || entries[0].State != string(Paused) || entries[0].Job == nil {
		t.Fatalf("entries=%+v err=%v", entries, err)
	}
}
func TestManagerListingDoesNotPauseActiveJob(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	job, err := m.Create(Candidate{Source: Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: "https://example.test"}})
	if err != nil {
		t.Fatal(err)
	}
	job.Status = Downloading
	if err = m.save(job); err != nil {
		t.Fatal(err)
	}
	jobs, err := m.List()
	if err != nil || len(jobs) != 1 || jobs[0].Status != Downloading {
		t.Fatalf("jobs=%+v err=%v", jobs, err)
	}
}
func TestManagerActiveCancelWaitsAndDoesNotRecreateJob(t *testing.T) {
	started := make(chan struct{})
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusOK)
		if f, ok := w.(http.Flusher); ok {
			f.Flush()
		}
		close(started)
		<-r.Context().Done()
	}))
	defer server.Close()
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	job, err := m.Create(Candidate{Source: Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: server.URL}})
	if err != nil {
		t.Fatal(err)
	}
	finished := make(chan struct{})
	go func() { _, _ = m.Resume(context.Background(), job.ID, nil); close(finished) }()
	<-started
	if err = m.Cancel(job.ID); err != nil {
		t.Fatal(err)
	}
	<-finished
	if _, err = os.Stat(job.TargetPath + ".download.json"); !os.IsNotExist(err) {
		t.Fatalf("job record recreated: %v", err)
	}
}
func TestManagerResumeReconcilesPartialAheadOfCheckpoint(t *testing.T) {
	body := emptyGGUF()
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Header.Get("Range") != "bytes=8-" {
			t.Fatalf("range=%q", r.Header.Get("Range"))
		}
		w.Header().Set("Content-Range", "bytes 8-23/24")
		w.WriteHeader(http.StatusPartialContent)
		_, _ = w.Write(body[8:])
	}))
	defer server.Close()
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	job, err := m.Create(Candidate{Source: Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: server.URL}, Artifact: Artifact{SizeBytes: 24}})
	if err != nil {
		t.Fatal(err)
	}
	if err = os.WriteFile(job.PartialPath, body[:8], 0600); err != nil {
		t.Fatal(err)
	}
	job.DownloadedBytes = 4
	job.Status = Paused
	if err = m.save(job); err != nil {
		t.Fatal(err)
	}
	completed, err := m.Resume(context.Background(), job.ID, nil)
	if err != nil || completed.Status != Completed {
		t.Fatalf("job=%+v err=%v", completed, err)
	}
}

func TestManagerPersistsContentDigestOnDownload(t *testing.T) {
	body := emptyGGUF()
	want := sha256.Sum256(body)
	s := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", "24")
			return
		}
		w.Write(body)
	}))
	defer s.Close()
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	c := Candidate{Source: Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: s.URL}, Artifact: Artifact{SizeBytes: 24}}
	j, e := m.Create(c)
	if e != nil {
		t.Fatal(e)
	}
	got, e := m.Resume(context.Background(), j.ID, nil)
	if e != nil {
		t.Fatal(e)
	}
	if got.Status != Completed {
		t.Fatalf("status=%s", got.Status)
	}
	target := filepath.Join(root, "models", "org", "repo", "m.gguf")
	rec, e := ReadRecord(target)
	if e != nil {
		t.Fatal(e)
	}
	if rec.Artifact.SHA256 != hex.EncodeToString(want[:]) {
		t.Fatalf("sha256=%q want=%q", rec.Artifact.SHA256, hex.EncodeToString(want[:]))
	}
}

func TestManagerResumedDownloadPersistsWholeFileDigest(t *testing.T) {
	body := emptyGGUF()
	want := sha256.Sum256(body)
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Header.Get("Range") != "bytes=8-" {
			t.Fatalf("range=%q", r.Header.Get("Range"))
		}
		w.Header().Set("Content-Range", "bytes 8-23/24")
		w.WriteHeader(http.StatusPartialContent)
		_, _ = w.Write(body[8:])
	}))
	defer server.Close()
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	job, err := m.Create(Candidate{Source: Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: server.URL}, Artifact: Artifact{SizeBytes: 24}})
	if err != nil {
		t.Fatal(err)
	}
	if err = os.WriteFile(job.PartialPath, body[:8], 0600); err != nil {
		t.Fatal(err)
	}
	job.DownloadedBytes = 8
	job.Status = Paused
	if err = m.save(job); err != nil {
		t.Fatal(err)
	}
	completed, err := m.Resume(context.Background(), job.ID, nil)
	if err != nil || completed.Status != Completed {
		t.Fatalf("job=%+v err=%v", completed, err)
	}
	target := filepath.Join(root, "models", "org", "repo", "m.gguf")
	rec, err := ReadRecord(target)
	if err != nil {
		t.Fatal(err)
	}
	if rec.Artifact.SHA256 != hex.EncodeToString(want[:]) {
		t.Fatalf("sha256=%q want=%q", rec.Artifact.SHA256, hex.EncodeToString(want[:]))
	}
}

func TestLibraryReportsSidecarLessModelAsUnreconciled(t *testing.T) {
	dir := t.TempDir()
	ggufPath := filepath.Join(dir, "publisher", "repo", "model.gguf")
	if err := os.MkdirAll(filepath.Dir(ggufPath), 0755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(ggufPath, []byte("bytes"), 0600); err != nil {
		t.Fatal(err)
	}
	m := NewManager(dir)
	entries, err := m.Library()
	if err != nil {
		t.Fatal(err)
	}
	if len(entries) != 1 {
		t.Fatalf("entries=%+v", entries)
	}
	e := entries[0]
	if e.State != "unreconciled" {
		t.Fatalf("state=%q", e.State)
	}
	if e.Source.Repository != "publisher/repo" {
		t.Fatalf("repository=%q", e.Source.Repository)
	}
	if e.Source.Filename != "model.gguf" {
		t.Fatalf("filename=%q", e.Source.Filename)
	}
	if e.Source.Provider != "" || e.Source.ResolvedRevision != "" || e.Source.URL != "" {
		t.Fatalf("expected zero-value provider/revision/url, got %+v", e.Source)
	}
	if e.TargetPath != ggufPath {
		t.Fatalf("targetPath=%q", e.TargetPath)
	}
}

func TestLibraryReportsPartialIdentityForOddlyPlacedGGUF(t *testing.T) {
	dir := t.TempDir()
	ggufPath := filepath.Join(dir, "model.gguf")
	if err := os.WriteFile(ggufPath, []byte("bytes"), 0600); err != nil {
		t.Fatal(err)
	}
	m := NewManager(dir)
	entries, err := m.Library()
	if err != nil {
		t.Fatal(err)
	}
	if len(entries) != 1 {
		t.Fatalf("entries=%+v", entries)
	}
	e := entries[0]
	if e.State != "unreconciled" {
		t.Fatalf("state=%q", e.State)
	}
	if e.Source.Filename != "model.gguf" {
		t.Fatalf("filename=%q", e.Source.Filename)
	}
	if e.Source.Repository != "" {
		t.Fatalf("expected empty repository for root-level file, got %q", e.Source.Repository)
	}
}

// func TestLibraryMixesUnreconciledAndInstalledEntriesWithoutFailing(t *testing.T) {
// 	dir := t.TempDir()
// 	installFixture(t, dir, "org/repo", "installed-Q4_K_M.gguf", "abc")
// 	oddPath := filepath.Join(dir, "stray.gguf")
// 	if err := os.WriteFile(oddPath, []byte("bytes"), 0600); err != nil {
// 		t.Fatal(err)
// 	}
// 	m := NewManager(dir)
// 	entries, err := m.Library()
// 	if err != nil {
// 		t.Fatal(err)
// 	}
// 	var installedCount, unreconciledCount int
// 	for _, e := range entries {
// 		switch e.State {
// 		case "installed":
// 			installedCount++
// 			if e.Source.Filename != "installed-Q4_K_M.gguf" {
// 				t.Fatalf("unexpected installed filename=%q", e.Source.Filename)
// 			}
// 		case "unreconciled":
// 			unreconciledCount++
// 		}
// 	}
// 	if installedCount != 1 || unreconciledCount != 1 {
// 		t.Fatalf("installed=%d unreconciled=%d entries=%+v", installedCount, unreconciledCount, entries)
// 	}
// }
