package model

import (
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
)

type RandomAccessReader interface {
	ReadAt([]byte, int64) (int, error)
	Size() int64
}

type LocalReader struct {
	file *os.File
	size int64
}

func OpenLocal(path string) (*LocalReader, error) {
	f, err := os.Open(path)
	if err != nil {
		return nil, err
	}
	info, err := f.Stat()
	if err != nil {
		_ = f.Close()
		return nil, err
	}
	return &LocalReader{f, info.Size()}, nil
}
func (r *LocalReader) ReadAt(p []byte, off int64) (int, error) { return r.file.ReadAt(p, off) }
func (r *LocalReader) Size() int64                             { return r.size }
func (r *LocalReader) Close() error                            { return r.file.Close() }

type Source struct {
	Provider          string `json:"provider"`
	Repository        string `json:"repository"`
	RequestedRevision string `json:"requestedRevision"`
	ResolvedRevision  string `json:"resolvedRevision"`
	Filename          string `json:"filename"`
	URL               string `json:"url"`
}
type Artifact struct {
	SizeBytes    int64  `json:"sizeBytes"`
	ETag         string `json:"etag,omitempty"`
	LastModified string `json:"lastModified,omitempty"`
	SHA256       string `json:"sha256,omitempty"`
	DownloadedAt string `json:"downloadedAt,omitempty"`
}

const (
	RecordSchemaVersion = 3
	DerivationVersion   = 1
)

type PhysicalQuantization struct {
	Type            GGMLType `json:"type"`
	Name            string   `json:"name"`
	CountParameters uint64   `json:"countParameters"`
}

type Derivation struct {
	Version              int                   `json:"version"`
	ComputedAt           string                `json:"computedAt"`
	Classification       Classification        `json:"classification"`
	CountParameters      int64                 `json:"countParameters"`
	PhysicalQuantization *PhysicalQuantization `json:"physicalQuantization,omitempty"`
}

type Record struct {
	SchemaVersion int        `json:"schemaVersion"`
	Source        Source     `json:"source"`
	Artifact      Artifact   `json:"artifact"`
	Derivation    Derivation `json:"derivation"`
	// Classification is an in-memory compatibility projection. Schema 3
	// persists it only inside Derivation; schema 2 readers populate this field
	// and mark Derivation stale.
	Classification Classification             `json:"-"`
	Extra          map[string]json.RawMessage `json:"-"`
}
type Status string

const (
	Pending     Status = "pending"
	Downloading Status = "downloading"
	Paused      Status = "paused"
	Completed   Status = "completed"
	Failed      Status = "failed"
	Cancelled   Status = "cancelled"
)

type Job struct {
	SchemaVersion     int    `json:"schemaVersion"`
	ID                string `json:"id"`
	Source            Source `json:"source"`
	PartialPath       string `json:"partialPath"`
	TargetPath        string `json:"targetPath"`
	DownloadedBytes   int64  `json:"downloadedBytes"`
	ExpectedSizeBytes int64  `json:"expectedSizeBytes"`
	ETag              string `json:"etag,omitempty"`
	LastModified      string `json:"lastModified,omitempty"`
	Status            Status `json:"status"`
	Error             string `json:"error,omitempty"`
}

// LibraryEntry is one artifact the configured models directory holds.
//
// Classification is read from the sidecar when one records it, and from the
// artifact's own header when none does, so an artifact's declared type is
// reported before anything is written about it. Classifying an unreconciled
// artifact is an observation only: it stays unreconciled and unresolvable for
// binding. Classification stays a pointer so an incomplete entry — a download
// still in flight, which has no artifact to read — reports its absence rather
// than an all-zero digest.
//
// Usability is derived from the enumerated library as a whole and is never
// persisted, since a projector's usability changes when a principal beside it
// is installed or removed.
type LibraryEntry struct {
	Source               Source                `json:"source"`
	TargetPath           string                `json:"targetPath"`
	State                string                `json:"state"`
	Classification       *Classification       `json:"classification,omitempty"`
	DerivationStale      bool                  `json:"derivationStale,omitempty"`
	PhysicalQuantization *PhysicalQuantization `json:"physicalQuantization,omitempty"`
	Usability            Usability             `json:"usability"`
	Job                  *Job                  `json:"job,omitempty"`
}

func (j Job) Validate() error {
	if j.ID == "" || j.Source.ResolvedRevision == "" || j.Source.URL == "" {
		return errors.New("download job is missing immutable source identity")
	}
	if j.Status == "" {
		return errors.New("download job has no status")
	}
	if j.DownloadedBytes < 0 || j.ExpectedSizeBytes < 0 {
		return errors.New("download job has negative byte count")
	}
	return nil
}
func (s Source) Validate() error {
	switch s.Provider {
	case "huggingface":
		if s.Repository == "" || s.Filename == "" || s.ResolvedRevision == "" || s.URL == "" {
			return fmt.Errorf("invalid Hugging Face source")
		}
		return nil
	case "local":
		if s.Repository == "" || s.Filename == "" {
			return fmt.Errorf("invalid local source")
		}
		return nil
	default:
		return fmt.Errorf("invalid Hugging Face source")
	}
}

var ErrInconsistentRevision = errors.New("model revision sidecars are inconsistent")
var _ io.ReaderAt = (*os.File)(nil)
