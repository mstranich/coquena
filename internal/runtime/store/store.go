// Package store owns verified, immutable llama.cpp runtime installations.
package store

import (
	"bytes"
	"context"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"time"
)

const MetadataSchemaVersion = 1

var ErrNotInitialized = errors.New("portable installation is not initialized")

type Layout struct{ Root, Downloads, Temporary, Runtimes string }

// Open validates the configured runtime directory and creates only its owned
// working subdirectories.
func Open(root string) (Layout, error) {
	root, err := filepath.Abs(root)
	if err != nil {
		return Layout{}, err
	}
	l := Layout{root, root, filepath.Join(root, "tmp"), filepath.Join(root, "llama.cpp")}
	if s, e := os.Stat(root); e != nil || !s.IsDir() {
		return Layout{}, fmt.Errorf("runtime directory unavailable: %s", root)
	}
	if err := os.MkdirAll(l.Temporary, 0755); err != nil {
		return Layout{}, err
	}
	if err := os.MkdirAll(l.Runtimes, 0755); err != nil {
		return Layout{}, err
	}
	return l, nil
}

type Asset struct {
	Name   string `json:"name"`
	URL    string `json:"url"`
	Digest string `json:"digest"`
	Size   int64  `json:"size"`
}

type DownloadState struct {
	ID              string `json:"id"`
	Name            string `json:"name"`
	PartialPath     string `json:"partialPath"`
	Status          string `json:"status"`
	DownloadedBytes int64  `json:"downloadedBytes"`
	ExpectedBytes   int64  `json:"expectedBytes"`
	Digest          string `json:"digest"`
}

// DownloadStatePath returns the durable job sidecar adjacent to a runtime
// archive partial. The archive itself remains `<asset>.partial`; the state is
// deliberately named `<asset>.download.json`, matching model download jobs.
func DownloadStatePath(partialPath string) string {
	return strings.TrimSuffix(partialPath, ".partial") + ".download.json"
}

type Identity struct {
	Release        string `json:"release"`
	OS             string `json:"os"`
	Architecture   string `json:"architecture"`
	Target         string `json:"target"`
	Backend        string `json:"backend"`
	BackendVersion string `json:"backendVersion,omitempty"`
}
type Metadata struct {
	SchemaVersion int       `json:"schemaVersion"`
	ID            string    `json:"id"`
	Identity      Identity  `json:"identity"`
	Assets        []Asset   `json:"assets"`
	Files         []string  `json:"files"`
	InstalledAt   time.Time `json:"installedAt"`
}

func ValidateID(id string) error {
	if id == "" || id == "." || id == ".." || filepath.Base(id) != id || strings.ContainsAny(id, "/\\\x00") {
		return fmt.Errorf("unsafe runtime ID %q", id)
	}
	return nil
}
func dirName(id string) string { h := sha256.Sum256([]byte(id)); return hex.EncodeToString(h[:]) }

type Limits struct {
	MaxDownloadBytes, MaxExpandedBytes int64
	MaxEntries                         int
}

func (l Limits) normalized() Limits {
	if l.MaxDownloadBytes <= 0 {
		l.MaxDownloadBytes = 2 << 30
	}
	if l.MaxExpandedBytes <= 0 {
		l.MaxExpandedBytes = 4 << 30
	}
	if l.MaxEntries <= 0 {
		l.MaxEntries = 20000
	}
	return l
}

// Download streams an asset into the owned downloads directory and verifies its official digest.
func Download(ctx context.Context, client *http.Client, layout Layout, a Asset, limits Limits) (string, error) {
	return DownloadWithProgress(ctx, client, layout, a, limits, nil)
}

// DownloadWithProgress preserves Download's validation while reporting the
// number of verified bytes written so interface adapters can render progress.
func DownloadWithProgress(ctx context.Context, client *http.Client, layout Layout, a Asset, limits Limits, progress func(int64)) (string, error) {
	limits = limits.normalized()
	if a.Size < 0 || a.Size > limits.MaxDownloadBytes {
		return "", errors.New("asset exceeds download limit")
	}
	if !strings.HasPrefix(a.Digest, "sha256:") {
		return "", errors.New("missing supported sha256 digest")
	}
	req, e := http.NewRequestWithContext(ctx, http.MethodGet, a.URL, nil)
	if e != nil {
		return "", e
	}
	partial := filepath.Join(layout.Downloads, a.Name+".partial")
	if err := ValidateID(filepath.Base(a.Name)); err != nil {
		return "", err
	}
	var offset int64
	if info, statErr := os.Stat(partial); statErr == nil {
		offset = info.Size()
		if offset > 0 {
			req.Header.Set("Range", fmt.Sprintf("bytes=%d-", offset))
		}
	}
	resp, e := client.Do(req)
	if e != nil {
		return "", e
	}
	defer resp.Body.Close()
	if resp.StatusCode/100 != 2 {
		return "", fmt.Errorf("download %s: HTTP %s", a.Name, resp.Status)
	}
	if offset > 0 && resp.StatusCode != http.StatusPartialContent {
		offset = 0
	}
	flags := os.O_CREATE | os.O_WRONLY
	if offset == 0 {
		flags |= os.O_TRUNC
	} else {
		flags |= os.O_APPEND
	}
	f, e := os.OpenFile(partial, flags, 0o644)
	if e != nil {
		return "", e
	}
	name := partial
	defer func() {
		f.Close()
	}()
	metaPath := DownloadStatePath(name)
	writeState := func(status string, downloaded int64) {
		b, _ := json.Marshal(DownloadState{ID: a.Name, Name: a.Name, PartialPath: name, Status: status, DownloadedBytes: downloaded, ExpectedBytes: a.Size, Digest: a.Digest})
		_ = os.WriteFile(metaPath, append(b, '\n'), 0o644)
	}
	writeState("downloading", offset)
	h := sha256.New()
	if offset > 0 {
		existing, readErr := os.Open(partial)
		if readErr == nil {
			_, _ = io.CopyN(h, existing, offset)
			_ = existing.Close()
		}
	}
	reader := &progressReader{reader: io.LimitReader(resp.Body, limits.MaxDownloadBytes+1), report: progress}
	reader.report = func(v int64) {
		writeState("downloading", offset+v)
		if progress != nil {
			progress(offset + v)
		}
	}
	n, e := io.Copy(io.MultiWriter(f, h), reader)
	n += offset
	if e != nil {
		if errors.Is(ctx.Err(), context.Canceled) {
			writeState("paused", n)
		} else {
			writeState("failed", n)
		}
		return "", e
	}
	if n > limits.MaxDownloadBytes {
		return "", errors.New("download exceeded byte limit")
	}
	if a.Size > 0 && n != a.Size {
		return "", fmt.Errorf("download size %d, expected %d", n, a.Size)
	}
	if !strings.EqualFold(hex.EncodeToString(h.Sum(nil)), strings.TrimPrefix(a.Digest, "sha256:")) {
		return "", errors.New("sha256 digest mismatch")
	}
	if e = f.Sync(); e != nil {
		return "", e
	}
	if e = f.Close(); e != nil {
		return "", e
	}
	writeState("verified", n)
	return name, nil
}

type progressReader struct {
	reader io.Reader
	read   int64
	report func(int64)
}

func (r *progressReader) Read(p []byte) (int, error) {
	n, err := r.reader.Read(p)
	if n > 0 {
		r.read += int64(n)
		if r.report != nil {
			r.report(r.read)
		}
	}
	return n, err
}

func NewStage(layout Layout) (string, error) { return os.MkdirTemp(layout.Temporary, "runtime-stage-") }

// FindAndProbeServer finds exactly one llama-server and runs a bounded,
// retryable non-server --version probe. A freshly extracted Windows binary can
// be transiently unavailable while security software inspects its DLL set.
func FindAndProbeServer(ctx context.Context, root string, timeout time.Duration) (string, error) {
	var found []string
	err := filepath.WalkDir(root, func(p string, d os.DirEntry, e error) error {
		if e != nil {
			return e
		}
		n := strings.ToLower(d.Name())
		if !d.IsDir() && (n == "llama-server" || n == "llama-server.exe") {
			found = append(found, p)
		}
		return nil
	})
	if err != nil {
		return "", err
	}
	if len(found) != 1 {
		return "", fmt.Errorf("expected exactly one llama-server, found %d", len(found))
	}
	if timeout <= 0 {
		timeout = 10 * time.Second
	}
	cctx, cancel := context.WithTimeout(ctx, timeout)
	defer cancel()
	if err := probeServer(cctx, found[0], 3, runVersionProbe); err != nil {
		return "", err
	}
	return found[0], nil
}

type versionProbe func(context.Context, string) (string, error)

func runVersionProbe(ctx context.Context, executable string) (string, error) {
	cmd := exec.CommandContext(ctx, executable, "--version")
	var out bytes.Buffer
	cmd.Stdout = &out
	cmd.Stderr = &out
	err := cmd.Run()
	return strings.TrimSpace(out.String()), err
}

func probeServer(ctx context.Context, executable string, attempts int, probe versionProbe) error {
	if attempts < 1 {
		attempts = 1
	}
	var last error
	for attempt := 1; attempt <= attempts; attempt++ {
		output, err := probe(ctx, executable)
		if err == nil {
			return nil
		}
		if len(output) > 4096 {
			output = output[:4096] + "..."
		}
		last = fmt.Errorf("attempt %d/%d: %w", attempt, attempts, err)
		if output != "" {
			last = fmt.Errorf("%w; output: %s", last, output)
		}
		if attempt == attempts {
			break
		}
		timer := time.NewTimer(500 * time.Millisecond)
		select {
		case <-ctx.Done():
			timer.Stop()
			return fmt.Errorf("probe llama-server: %w", ctx.Err())
		case <-timer.C:
		}
	}
	return fmt.Errorf("probe llama-server failed: %w", last)
}

func writeMetadata(root string, m Metadata) error {
	m.SchemaVersion = 1
	if m.InstalledAt.IsZero() {
		m.InstalledAt = time.Now().UTC()
	}
	b, e := json.MarshalIndent(m, "", "  ")
	if e != nil {
		return e
	}
	b = append(b, '\n')
	return os.WriteFile(filepath.Join(root, "runtime.json"), b, 0644)
}

// Publish atomically installs a completed stage. Existing valid IDs are idempotent; conflicts are preserved.
func Publish(layout Layout, stage string, m Metadata) (already bool, err error) {
	if err = ValidateID(m.ID); err != nil {
		return
	}
	dest := filepath.Join(layout.Runtimes, dirName(m.ID))
	if _, e := os.Stat(dest); e == nil {
		old, e := ReadMetadata(dest)
		if e == nil && old.ID == m.ID && validateFiles(dest, old.Files) == nil {
			return true, nil
		}
		return false, fmt.Errorf("runtime destination conflicts: %s", dest)
	} else if !errors.Is(e, os.ErrNotExist) {
		return false, e
	}
	files, e := listFiles(stage)
	if e != nil {
		return false, e
	}
	m.Files = files
	if e = writeMetadata(stage, m); e != nil {
		return false, e
	}
	if e = os.Rename(stage, dest); e != nil {
		return false, fmt.Errorf("publish runtime atomically: %w", e)
	}
	return false, nil
}
func listFiles(root string) ([]string, error) {
	var v []string
	e := filepath.WalkDir(root, func(p string, d os.DirEntry, e error) error {
		if e != nil {
			return e
		}
		if p != root && !d.IsDir() {
			r, _ := filepath.Rel(root, p)
			v = append(v, filepath.ToSlash(r))
		}
		return nil
	})
	return v, e
}
func ReadMetadata(dir string) (Metadata, error) {
	var m Metadata
	b, e := os.ReadFile(filepath.Join(dir, "runtime.json"))
	if e != nil {
		return m, e
	}
	dec := json.NewDecoder(bytes.NewReader(b))
	dec.DisallowUnknownFields()
	if e = dec.Decode(&m); e != nil {
		return m, e
	}
	var extra any
	if e = dec.Decode(&extra); !errors.Is(e, io.EOF) {
		return m, errors.New("invalid runtime metadata: multiple JSON values")
	}
	if m.SchemaVersion != 1 || ValidateID(m.ID) != nil {
		return m, errors.New("invalid runtime metadata")
	}
	if e = validateFiles(dir, m.Files); e != nil {
		return m, e
	}
	return m, nil
}

func validateFiles(root string, files []string) error {
	if len(files) == 0 {
		return errors.New("runtime metadata has no managed files")
	}
	seen := make(map[string]bool, len(files))
	for _, name := range files {
		p, err := safeArchivePath(root, name)
		if err != nil || seen[strings.ToLower(filepath.Clean(name))] {
			return fmt.Errorf("invalid managed file %q", name)
		}
		seen[strings.ToLower(filepath.Clean(name))] = true
		info, err := os.Lstat(p)
		if err != nil || !info.Mode().IsRegular() {
			return fmt.Errorf("managed file unavailable: %s", p)
		}
	}
	return nil
}

type Entry struct {
	Path     string
	Metadata Metadata
}
type Diagnostic struct {
	Path  string
	Error string
}

func Scan(layout Layout) ([]Entry, []Diagnostic, error) {
	ds, e := os.ReadDir(layout.Runtimes)
	if e != nil {
		return nil, nil, e
	}
	var entries []Entry
	var diags []Diagnostic
	for _, d := range ds {
		if !d.IsDir() || strings.HasPrefix(d.Name(), ".") {
			continue
		}
		p := filepath.Join(layout.Runtimes, d.Name())
		m, e := ReadMetadata(p)
		if e != nil {
			diags = append(diags, Diagnostic{p, e.Error()})
			continue
		}
		if d.Name() != dirName(m.ID) {
			diags = append(diags, Diagnostic{p, "runtime directory does not match public ID"})
			continue
		}
		entries = append(entries, Entry{p, m})
	}
	return entries, diags, nil
}

type Lock struct{ path string }

var ErrBusy = errors.New("runtime mutation is busy")

func Acquire(layout Layout) (*Lock, error) {
	p := filepath.Join(layout.Runtimes, ".mutation.lock")
	f, e := os.OpenFile(p, os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0600)
	if errors.Is(e, os.ErrExist) {
		return nil, ErrBusy
	}
	if e != nil {
		return nil, e
	}
	fmt.Fprintf(f, "%d\n", os.Getpid())
	if e = f.Close(); e != nil {
		os.Remove(p)
		return nil, e
	}
	return &Lock{p}, nil
}
func (l *Lock) Release() error {
	if l == nil {
		return nil
	}
	e := os.Remove(l.path)
	if errors.Is(e, os.ErrNotExist) {
		return nil
	}
	return e
}

// Remove isolates exactly one ID by rename before deleting its tombstone.
func Remove(layout Layout, id string) error {
	if e := ValidateID(id); e != nil {
		return e
	}
	entries, _, e := Scan(layout)
	if e != nil {
		return e
	}
	var src string
	for _, x := range entries {
		if x.Metadata.ID == id {
			src = x.Path
			break
		}
	}
	if src == "" {
		return os.ErrNotExist
	}
	tomb := filepath.Join(layout.Runtimes, ".remove-"+dirName(id)+"-"+fmt.Sprint(time.Now().UnixNano()))
	if e = os.Rename(src, tomb); e != nil {
		return fmt.Errorf("isolate runtime %s: %w", src, e)
	}
	if e = os.RemoveAll(tomb); e != nil {
		return fmt.Errorf("delete isolated runtime %s: %w", tomb, e)
	}
	return nil
}
