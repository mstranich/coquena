package model

import (
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
type Record struct {
	SchemaVersion int            `json:"schemaVersion"`
	Source        Source         `json:"source"`
	Artifact      Artifact       `json:"artifact"`
	GGUF          map[string]any `json:"gguf"`
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

type LibraryEntry struct {
	Source     Source `json:"source"`
	TargetPath string `json:"targetPath"`
	State      string `json:"state"`
	Job        *Job   `json:"job,omitempty"`
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
	if s.Provider != "huggingface" || s.Repository == "" || s.Filename == "" || s.ResolvedRevision == "" || s.URL == "" {
		return fmt.Errorf("invalid Hugging Face source")
	}
	return nil
}

var ErrInconsistentRevision = errors.New("model revision sidecars are inconsistent")
var _ io.ReaderAt = (*os.File)(nil)
