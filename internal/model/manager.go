package model

import (
	"context"
	"crypto/rand"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"sync"
	"time"
)

type Manager struct {
	ModelsDir string
	Resolver  Resolver
	Client    *http.Client
	LogWriter io.Writer
	mu        sync.Mutex
	cancels   map[string]context.CancelFunc
	cancelled map[string]bool
	dones     map[string]chan struct{}
	terminals map[string]Job
}

const (
	maxDownloadAttempts = 3
	terminalSnapshotTTL = time.Minute
)

func NewManager(models string) *Manager {
	m := &Manager{ModelsDir: models, Resolver: Resolver{}, cancels: map[string]context.CancelFunc{}, cancelled: map[string]bool{}, dones: map[string]chan struct{}{}, terminals: map[string]Job{}}
	m.recoverInterrupted()
	return m
}
func (m *Manager) Resolve(input, selector string) ([]Candidate, error) {
	m.logf("model resolve reference=%q selector=%q", input, selector)
	candidates, err := m.Resolver.Resolve(input, selector)
	if err != nil {
		m.logf("model resolve failed: %v", err)
		return nil, err
	}
	m.logf("model resolve completed candidates=%d", len(candidates))
	return candidates, nil
}
func (m *Manager) ResolveRevision(repository, requestedRevision, selector string) ([]Candidate, error) {
	return m.Resolver.ResolveRevision(repository, requestedRevision, selector)
}
func (m *Manager) InspectRemote(c Candidate) (Inspection, error) {
	m.logf("model inspect started repository=%s filename=%s revision=%s", c.Source.Repository, c.Source.Filename, c.Source.ResolvedRevision)
	r, e := OpenHTTP(m.client(), c.Source.URL)
	if e != nil {
		return Inspection{}, e
	}
	meta, e := ReadMetadata(r)
	if e != nil {
		m.logf("model inspect failed filename=%s: %v", c.Source.Filename, e)
		return Inspection{}, e
	}
	c.Artifact.SizeBytes = r.Size()
	c.ETag = r.ETag()
	c.Artifact.LastModified = r.LastModified()
	m.logf("model inspect completed filename=%s metadataKeys=%d sizeBytes=%d", c.Source.Filename, len(meta), c.Artifact.SizeBytes)
	return NewInspection(c.Source, c.Artifact, meta), nil
}

// InspectLocal reads the installed artifact's own header rather than trusting
// the sidecar, which persists the classification but no metadata at all. The
// sidecar still supplies the recorded source and artifact identity, which the
// file itself cannot know.
func (m *Manager) InspectLocal(target string) (Inspection, error) {
	r, e := OpenLocal(target)
	if e != nil {
		return Inspection{}, e
	}
	defer r.Close()
	rec, e := ReadRecord(target)
	if e != nil {
		return Inspection{}, e
	}
	meta, e := ReadMetadata(r)
	if e != nil {
		return Inspection{}, e
	}
	return NewInspection(rec.Source, rec.Artifact, meta), nil
}
func (m *Manager) Create(c Candidate) (Job, error) {
	m.mu.Lock()
	defer m.mu.Unlock()
	if err := c.Source.Validate(); err != nil {
		return Job{}, err
	}
	id := newID()
	target, e := TargetPath(m.ModelsDir, c.Source)
	if e != nil {
		return Job{}, e
	}
	if err := os.MkdirAll(filepath.Dir(target), 0755); err != nil {
		return Job{}, err
	}
	j := Job{SchemaVersion: 1, ID: id, Source: c.Source, PartialPath: target + ".partial", TargetPath: target, ExpectedSizeBytes: c.Artifact.SizeBytes, ETag: c.ETag, LastModified: c.Artifact.LastModified, Status: Pending}
	if c.ContentSHA256 != "" {
		published := PublishedDigest(AlgorithmSHA256, DomainRawFile, c.ContentSHA256)
		j.PublishedDigest = &published
	}
	if _, err := os.Stat(j.PartialPath); err == nil {
		return Job{}, fmt.Errorf("model download already exists: %s", j.PartialPath)
	} else if !errors.Is(err, os.ErrNotExist) {
		return Job{}, err
	}
	if e = m.save(j); e != nil {
		return Job{}, e
	}
	if strings.Contains(c.Source.URL, "huggingface.co/") || m.Resolver.BaseURL != "" {
		if _, avatarErr := m.EnsurePublisherAvatar(c.Source.Repository); avatarErr != nil {
			m.logf("publisher avatar unavailable repository=%s: %v", c.Source.Repository, avatarErr)
		}
	}
	m.logf("model download created id=%s filename=%s expectedBytes=%d", j.ID, j.Source.Filename, j.ExpectedSizeBytes)
	return j, nil
}

func (m *Manager) List() ([]Job, error) {
	out := []Job{}
	e := filepath.WalkDir(m.ModelsDir, func(path string, entry os.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if entry.IsDir() || !strings.HasSuffix(strings.ToLower(entry.Name()), ".gguf.download.json") {
			return nil
		}
		j, loadErr := loadJobPath(path)
		if loadErr != nil {
			return loadErr
		}
		out = append(out, j)
		return nil
	})
	if e != nil && !errors.Is(e, os.ErrNotExist) {
		return nil, e
	}
	return out, nil
}
func (m *Manager) Status(id string) (Job, error) {
	j, err := m.load(id)
	if err == nil || !errors.Is(err, os.ErrNotExist) {
		return j, err
	}
	m.mu.Lock()
	defer m.mu.Unlock()
	if terminal, ok := m.terminals[id]; ok {
		return terminal, nil
	}
	return Job{}, err
}

func (m *Manager) ForgetTerminal(id string) {
	m.mu.Lock()
	defer m.mu.Unlock()
	delete(m.terminals, id)
}

// modifiedAfter reports whether the file was touched more recently than the
// verification that examined it.
//
// It only ever weakens a claim. A modification time is trivially forged and
// unreliable across filesystems, so it is fit to say "this result may be old"
// and unfit to establish anything: a stale record still reports the outcome it
// recorded, and is never upgraded to matched or downgraded to altered on this
// evidence.
//
// The comparison is made at the recorded resolution. CheckedAt is RFC3339 and
// therefore second-granular, so a file written in the same second as the check
// must not read as stale on sub-second precision alone. An unparseable
// timestamp reports not stale, since a bad clock should not manufacture doubt
// either.
func modifiedAfter(modified time.Time, checkedAt string) bool {
	checked, err := time.Parse(time.RFC3339, checkedAt)
	if err != nil {
		return false
	}
	return modified.Truncate(time.Second).After(checked)
}

func (m *Manager) Library() ([]LibraryEntry, error) {
	entries := []LibraryEntry{}
	jobs, err := m.List()
	if err != nil {
		return nil, err
	}
	partialTargets := map[string]bool{}
	for i := range jobs {
		job := jobs[i]
		entries = append(entries, LibraryEntry{Source: job.Source, TargetPath: job.TargetPath, JobStatus: job.Status, Job: &job})
		partialTargets[filepath.Clean(job.TargetPath)] = true
	}
	err = filepath.WalkDir(m.ModelsDir, func(path string, entry os.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if entry.IsDir() || !isGGUF(entry.Name()) || partialTargets[filepath.Clean(path)] {
			return nil
		}
		record, readErr := ReadRecord(path)
		if readErr != nil {
			// No sidecar, so the cascade stops at its first gate. The
			// classification read here is an observation for the operator's
			// benefit and is recorded nowhere: an artifact Coquena could not
			// classify is refused a sidecar, never given one saying unknown.
			classification := classifyUnreconciled(path)
			entries = append(entries, LibraryEntry{Source: untrackedSource(m.ModelsDir, path), TargetPath: path, State: stateOf(nil), Classification: &classification, Standing: StandingUnattested})
			return nil
		}
		classification := record.Classification
		libraryEntry := LibraryEntry{Source: record.Source, TargetPath: path, State: stateOf(&record), Classification: &classification, DerivationStale: record.DerivationStale(), PhysicalQuantization: record.Derivation.PhysicalQuantization, Standing: record.Standing()}
		if attestation, applies := record.AppliedAttestation(); applies {
			libraryEntry.AttestationMethod = attestation.Method
		}
		if verification, applies := record.AppliedVerification(); applies {
			libraryEntry.Verification = &verification
			// The walk already carries this artifact's directory information,
			// so comparing modification time against the verification costs
			// nothing beyond what enumeration read anyway.
			if info, infoErr := entry.Info(); infoErr == nil {
				libraryEntry.VerificationStale = modifiedAfter(info.ModTime(), verification.CheckedAt)
			}
		}
		entries = append(entries, libraryEntry)
		return nil
	})
	if errors.Is(err, os.ErrNotExist) {
		err = nil
	}
	// Usability is derived here, once the whole library is known, because a
	// projector's usability depends on the principals enumerated beside it. It
	// is computed on every read and written nowhere.
	AssessUsability(entries)
	return entries, err
}

// classifyUnreconciled reports what an artifact with no valid sidecar declares
// itself to be, read from the artifact's own header. It exists because a
// library of foreign files otherwise lists every row with an empty type column
// and no way to tell a projector from a model.
//
// Reading a header is an observation and grants no standing: nothing is
// written, and the caller leaves the entry unreconciled and so excluded from
// ResolveInstalled. The read is local, so enumeration issues no network
// request, and it skips array payloads, since a principal's vocabulary is the
// entire cost of a full read and classification needs only its length.
//
// An unreadable or malformed header degrades to an explicit unknown
// classification rather than failing the walk, exactly as an unreadable
// sidecar already does: one foreign file must not hide the rest of the
// library.
func classifyUnreconciled(path string) Classification {
	reader, err := OpenLocal(path)
	if err != nil {
		return Classification{Type: TypeUnknown}
	}
	defer reader.Close()
	meta, err := ReadMetadataSkippingArrays(reader)
	if err != nil {
		return Classification{Type: TypeUnknown}
	}
	return Classify(meta)
}

// untrackedSource derives a best-effort Source for a .gguf file that has
// no valid Coquena sidecar, using only its path relative to modelsDir. It
// mirrors the <publisher>/<repo>/<file>.gguf shape both Coquena and LM Studio
// use: the two path segments immediately above the file become Repository,
// and the base name becomes Filename. When the path does not have at least
// two parent segments beneath modelsDir, whatever partial information is
// available is still returned rather than failing.
func untrackedSource(modelsDir, path string) Source {
	filename := filepath.Base(path)
	rel, err := filepath.Rel(modelsDir, path)
	if err != nil {
		return Source{Filename: filename}
	}
	rel = filepath.ToSlash(rel)
	slashIdx := strings.LastIndex(rel, "/")
	if slashIdx < 0 {
		return Source{Filename: filename}
	}
	dir := rel[:slashIdx]
	parts := strings.Split(dir, "/")
	var repository string
	if len(parts) >= 2 {
		repository = strings.Join(parts[len(parts)-2:], "/")
	} else {
		repository = parts[len(parts)-1]
	}
	return Source{Repository: repository, Filename: filename}
}

func (m *Manager) InstalledMetadata(repository, filename string) (Inspection, error) {
	entries, err := m.Library()
	if err != nil {
		return Inspection{}, err
	}
	for _, entry := range entries {
		if entry.State != StateUntracked && entry.Source.Repository == repository && entry.Source.Filename == filename {
			// Read through InspectLocal rather than ReadRecord alone: the
			// sidecar no longer caches the raw GGUF map, so the metadata comes
			// from the artifact itself on demand.
			return m.InspectLocal(entry.TargetPath)
		}
	}
	return Inspection{}, os.ErrNotExist
}
func (m *Manager) Pause(id string) (Job, error) {
	m.logf("model download pause requested id=%s", id)
	m.mu.Lock()
	if c := m.cancels[id]; c != nil {
		c()
	}
	m.mu.Unlock()
	j, e := m.load(id)
	if e != nil {
		return Job{}, e
	}
	if j.Status == Downloading || j.Status == Pending {
		j.Status = Paused
		e = m.save(j)
	}
	return j, e
}
func (m *Manager) Cancel(id string) error {
	m.logf("model download cancel requested id=%s", id)
	j, err := m.load(id)
	if err != nil {
		return err
	}
	m.mu.Lock()
	m.cancelled[id] = true
	done := m.dones[id]
	if c := m.cancels[id]; c != nil {
		c()
		delete(m.cancels, id)
	}
	m.mu.Unlock()
	if done != nil {
		<-done
	}
	if err = os.Remove(j.PartialPath); err != nil && !errors.Is(err, os.ErrNotExist) {
		return err
	}
	if err = os.Remove(m.jobPath(j)); err != nil && !errors.Is(err, os.ErrNotExist) {
		return err
	}
	return nil
}
func (m *Manager) Resume(parent context.Context, id string, progress func(Job)) (Job, error) {
	m.logf("model download resume requested id=%s", id)
	m.mu.Lock()
	if _, ok := m.cancels[id]; ok {
		m.mu.Unlock()
		return Job{}, fmt.Errorf("download already active")
	}
	ctx, cancel := context.WithCancel(parent)
	m.cancels[id] = cancel
	done := make(chan struct{})
	m.dones[id] = done
	m.mu.Unlock()
	defer func() {
		m.mu.Lock()
		delete(m.cancels, id)
		delete(m.cancelled, id)
		delete(m.dones, id)
		close(done)
		m.mu.Unlock()
	}()
	j, e := m.load(id)
	if e != nil {
		return Job{}, e
	}
	if j.Status != Pending && j.Status != Paused && j.Status != Failed {
		return j, fmt.Errorf("job %s cannot resume from %s", id, j.Status)
	}
	j.Status = Downloading
	j.Error = ""
	if e = m.save(j); e != nil {
		return j, e
	}
	f, e := os.OpenFile(j.PartialPath, os.O_CREATE|os.O_WRONLY, 0600)
	if e != nil {
		return j, e
	}
	defer f.Close()
	info, _ := f.Stat()
	if info.Size() > j.DownloadedBytes {
		m.logf("model download reconciled partial id=%s persistedBytes=%d actualBytes=%d", j.ID, j.DownloadedBytes, info.Size())
		j.DownloadedBytes = info.Size()
		_ = m.save(j)
	} else if info.Size() < j.DownloadedBytes {
		j.Status = Failed
		j.Error = "partial file is smaller than its persisted checkpoint"
		_ = m.save(j)
		return j, errors.New(j.Error)
	}
	if _, e = f.Seek(j.DownloadedBytes, io.SeekStart); e != nil {
		return j, e
	}
	req, e := http.NewRequestWithContext(ctx, http.MethodGet, j.Source.URL, nil)
	if e != nil {
		return j, e
	}
	if j.DownloadedBytes > 0 {
		req.Header.Set("Range", fmt.Sprintf("bytes=%d-", j.DownloadedBytes))
		if j.ETag != "" {
			req.Header.Set("If-Range", j.ETag)
		} else if j.LastModified != "" {
			req.Header.Set("If-Range", j.LastModified)
		}
	}
	resp, e := m.doWithRetry(ctx, req)
	if e != nil {
		if errors.Is(e, context.Canceled) || errors.Is(ctx.Err(), context.Canceled) {
			if m.isCancelled(id) {
				j.Status = Cancelled
				return j, nil
			}
			j.Status = Paused
			j.Error = ""
			_ = m.save(j)
			return j, nil
		}
		return m.fail(j, e)
	}
	defer resp.Body.Close()
	if j.DownloadedBytes > 0 && j.ETag != "" && resp.Header.Get("ETag") != "" && resp.Header.Get("ETag") != j.ETag {
		return m.fail(j, errors.New("remote validator changed before resume"))
	}
	if j.DownloadedBytes > 0 && j.LastModified != "" && j.ETag == "" && resp.Header.Get("Last-Modified") != "" && resp.Header.Get("Last-Modified") != j.LastModified {
		return m.fail(j, errors.New("remote validator changed before resume"))
	}
	if v := resp.Header.Get("ETag"); v != "" {
		j.ETag = v
	}
	if v := resp.Header.Get("Last-Modified"); v != "" {
		j.LastModified = v
	}
	if j.DownloadedBytes > 0 && resp.StatusCode != http.StatusPartialContent {
		return m.fail(j, fmt.Errorf("server refused safe resume: %s", resp.Status))
	}
	if j.DownloadedBytes > 0 && !strings.HasPrefix(resp.Header.Get("Content-Range"), fmt.Sprintf("bytes %d-", j.DownloadedBytes)) {
		return m.fail(j, errors.New("server returned an invalid resume range"))
	}
	if j.DownloadedBytes == 0 && resp.StatusCode != http.StatusOK {
		return m.fail(j, fmt.Errorf("download: %s", resp.Status))
	}
	if j.ExpectedSizeBytes == 0 {
		if resp.StatusCode == http.StatusPartialContent {
			if _, total, ok := strings.Cut(resp.Header.Get("Content-Range"), "/"); ok {
				j.ExpectedSizeBytes, _ = strconv.ParseInt(total, 10, 64)
			}
		} else if resp.ContentLength > 0 {
			j.ExpectedSizeBytes = j.DownloadedBytes + resp.ContentLength
		}
		if j.ExpectedSizeBytes > 0 {
			_ = m.save(j)
			m.logf("model download size discovered id=%s expectedBytes=%d", j.ID, j.ExpectedSizeBytes)
		}
	}
	buf := make([]byte, 128<<10)
	last := time.Now()
	lastLog := last
	for {
		n, re := resp.Body.Read(buf)
		if n > 0 {
			if _, e = f.Write(buf[:n]); e != nil {
				return m.fail(j, e)
			}
			j.DownloadedBytes += int64(n)
			if time.Since(last) > 250*time.Millisecond {
				_ = m.save(j)
				if time.Since(lastLog) > 2*time.Second {
					m.logf("model download progress id=%s downloaded=%s total=%s progress=%.1f%%", j.ID, HumanBytes(j.DownloadedBytes), HumanBytes(j.ExpectedSizeBytes), DownloadPercent(j.DownloadedBytes, j.ExpectedSizeBytes))
					lastLog = time.Now()
				}
				if progress != nil {
					progress(j)
				}
				last = time.Now()
			}
		}
		if re == io.EOF {
			break
		}
		if re != nil {
			if errors.Is(re, context.Canceled) {
				if m.isCancelled(id) {
					j.Status = Cancelled
					return j, nil
				}
				j.Status = Paused
				j.Error = ""
				_ = m.save(j)
				return j, nil
			}
			return m.fail(j, re)
		}
	}
	if j.ExpectedSizeBytes > 0 && j.DownloadedBytes != j.ExpectedSizeBytes {
		return m.fail(j, fmt.Errorf("download size mismatch"))
	}
	if e := f.Close(); e != nil {
		return m.fail(j, e)
	}
	inspection, e := m.InspectPartial(j.PartialPath, j.Source, j.ExpectedSizeBytes)
	if e != nil {
		return m.fail(j, e)
	}
	digest, e := hashFile(j.PartialPath)
	if e != nil {
		return m.fail(j, e)
	}
	rec := inspection.Record()
	now := time.Now().UTC().Format(time.RFC3339)
	rec.SetIntegrity(digest, now)
	// A managed download is attested by construction. The bytes came from a
	// revision-pinned URL, so where they came from follows from the transfer
	// rather than from any comparison; only an artifact adopted from an
	// existing local cache needs a published hash to establish after the fact
	// that it belongs to a revision. Recording this as unattested would
	// understate what Coquena actually knows.
	rec.Attestation = &Attestation{
		Repository:     j.Source.Repository,
		ResolvedCommit: j.Source.ResolvedRevision,
		ArtifactPath:   j.Source.Filename,
		Digest:         LocalDigest(digest, now),
		Method:         MethodDownload,
		VerifiedAt:     now,
	}
	// The observation records what the repository said while resolving, on its
	// own axis. The published digest sits here and not in the attestation
	// because nothing compared it: comparing it is a separate improvement, and
	// asserting a comparison that did not happen is the defect this record
	// exists to prevent.
	rec.Observation = &Observation{
		Repository:        j.Source.Repository,
		RequestedRevision: j.Source.RequestedRevision,
		ResolvedCommit:    j.Source.ResolvedRevision,
		ArtifactPath:      j.Source.Filename,
		PublishedDigest:   j.PublishedDigest,
		ObservedAt:        now,
	}
	_, e = Publish(m.ModelsDir, j.PartialPath, rec)
	if e != nil {
		return m.fail(j, e)
	}
	j.Status = Completed
	_ = m.save(j)
	m.mu.Lock()
	m.terminals[j.ID] = j
	m.mu.Unlock()
	time.AfterFunc(terminalSnapshotTTL, func() { m.ForgetTerminal(j.ID) })
	m.logf("model download completed id=%s bytes=%d", j.ID, j.DownloadedBytes)
	if progress != nil {
		progress(j)
	}
	if err := os.Remove(m.jobPath(j)); err != nil && !errors.Is(err, os.ErrNotExist) {
		return j, err
	}
	return j, nil
}

func (m *Manager) doWithRetry(ctx context.Context, request *http.Request) (*http.Response, error) {
	var last error
	for attempt := 0; attempt < maxDownloadAttempts; attempt++ {
		response, err := m.client().Do(request.Clone(ctx))
		if err == nil && response.StatusCode < 500 {
			return response, nil
		}
		if response != nil {
			_ = response.Body.Close()
			last = fmt.Errorf("download: %s", response.Status)
		} else {
			last = err
		}
		if errors.Is(ctx.Err(), context.Canceled) {
			return nil, ctx.Err()
		}
		if attempt+1 < maxDownloadAttempts {
			select {
			case <-ctx.Done():
				return nil, ctx.Err()
			case <-time.After(time.Duration(attempt+1) * 25 * time.Millisecond):
			}
		}
	}
	return nil, last
}
func (m *Manager) InspectPartial(p string, s Source, size int64) (Inspection, error) {
	r, e := OpenLocal(p)
	if e != nil {
		return Inspection{}, e
	}
	defer r.Close()
	meta, e := ReadMetadata(r)
	if e != nil {
		return Inspection{}, e
	}
	return NewInspection(s, Artifact{SizeBytes: size}, meta), nil
}
func (m *Manager) fail(j Job, e error) (Job, error) {
	j.Status = Failed
	j.Error = e.Error()
	_ = m.save(j)
	m.logf("model download failed id=%s: %v", j.ID, e)
	return j, e
}
func (m *Manager) load(id string) (Job, error) {
	jobs, err := m.ListRaw()
	if err != nil {
		return Job{}, err
	}
	for _, j := range jobs {
		if j.ID == id {
			return j, nil
		}
	}
	return Job{}, os.ErrNotExist
}
func (m *Manager) save(j Job) error {
	if e := j.Validate(); e != nil {
		return e
	}
	b, e := json.MarshalIndent(j, "", "  ")
	if e != nil {
		return e
	}
	return writeAtomic(m.jobPath(j), append(b, '\n'))
}

func (m *Manager) jobPath(j Job) string {
	return j.TargetPath + ".download.json"
}

func (m *Manager) ListRaw() ([]Job, error) {
	var out []Job
	_ = filepath.WalkDir(m.ModelsDir, func(path string, entry os.DirEntry, err error) error {
		if err == nil && !entry.IsDir() && strings.HasSuffix(strings.ToLower(entry.Name()), ".gguf.download.json") {
			if j, loadErr := loadJobPath(path); loadErr == nil {
				out = append(out, j)
			}
		}
		return nil
	})
	return out, nil
}

func (m *Manager) recoverInterrupted() {
	jobs, _ := m.ListRaw()
	for _, job := range jobs {
		if job.Status == Downloading {
			job.Status = Paused
			job.Error = ""
			_ = m.save(job)
		}
	}
}

func loadJobPath(path string) (Job, error) {
	var j Job
	b, err := os.ReadFile(path)
	if err != nil {
		return j, err
	}
	if err = json.Unmarshal(b, &j); err != nil {
		return j, err
	}
	return j, j.Validate()
}
func (m *Manager) client() *http.Client {
	if m.Client != nil {
		return m.Client
	}
	return http.DefaultClient
}
func (m *Manager) logf(format string, args ...any) {
	if m.LogWriter != nil {
		_, _ = fmt.Fprintf(m.LogWriter, format+"\n", args...)
	}
}
func (m *Manager) isCancelled(id string) bool {
	m.mu.Lock()
	defer m.mu.Unlock()
	return m.cancelled[id]
}
func HumanBytes(bytes int64) string {
	const unit = int64(1024)
	if bytes < unit {
		return fmt.Sprintf("%d B", bytes)
	}
	value := float64(bytes)
	units := []string{"KiB", "MiB", "GiB", "TiB"}
	for _, label := range units {
		value /= 1024
		if value < 1024 || label == units[len(units)-1] {
			return fmt.Sprintf("%.1f %s", value, label)
		}
	}
	return fmt.Sprintf("%d B", bytes)
}
func DownloadPercent(downloaded, expected int64) float64 {
	if expected <= 0 {
		return 0
	}
	percent := float64(downloaded) * 100 / float64(expected)
	if percent > 100 {
		return 100
	}
	return percent
}
func newID() string { b := make([]byte, 16); _, _ = rand.Read(b); return hex.EncodeToString(b) }

// hashFile computes the SHA-256 digest of the complete file at path in a
// single sequential read pass. It is called exactly once per completed
// download, right after the write loop finishes and before Publish, so that
// a paused-then-resumed download still yields the digest of the whole file's
// final content rather than only the bytes written in the last session.
func hashFile(path string) (string, error) {
	f, e := os.Open(path)
	if e != nil {
		return "", e
	}
	defer f.Close()
	h := sha256.New()
	if _, e = io.Copy(h, f); e != nil {
		return "", e
	}
	return hex.EncodeToString(h.Sum(nil)), nil
}
