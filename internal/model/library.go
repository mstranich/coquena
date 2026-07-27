package model

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"
	"time"
)

func TargetPath(modelsDir string, source Source) (string, error) {
	if err := source.Validate(); err != nil {
		return "", err
	}
	return PathFor(modelsDir, source.Repository, source.Filename)
}

// PathFor computes the on-disk path for a repository and filename beneath
// modelsDir, without requiring a full Source (revision, URL, provider). It
// applies the same path-safety checks as TargetPath.
func PathFor(modelsDir, repository, filename string) (string, error) {
	parts := strings.Split(repository, "/")
	for _, v := range append(parts, filename) {
		if v == "" || v == "." || v == ".." || strings.ContainsAny(v, `\\:`) {
			return "", fmt.Errorf("unsafe model path component %q", v)
		}
	}
	root := filepath.Clean(modelsDir)
	target := filepath.Join(append([]string{root}, append(parts, filename)...)...)
	rel, err := filepath.Rel(root, target)
	if err != nil || rel == ".." || strings.HasPrefix(rel, ".."+string(os.PathSeparator)) {
		return "", fmt.Errorf("model target escapes models directory")
	}
	return target, nil
}
func Publish(modelsDir, partial string, record Record) (string, error) {
	target, err := TargetPath(modelsDir, record.Source)
	if err != nil {
		return "", err
	}
	if err = os.MkdirAll(filepath.Dir(target), 0755); err != nil {
		return "", err
	}
	if _, err = os.Stat(target); err == nil {
		existing, readErr := ReadRecord(target)
		if readErr == nil && existing.Source == record.Source {
			return target, nil
		}
		return "", fmt.Errorf("model already exists with conflicting or incomplete sidecars: %s", target)
	} else if !os.IsNotExist(err) {
		return "", err
	}
	record.SchemaVersion = 1
	record.Artifact.DownloadedAt = time.Now().UTC().Format(time.RFC3339)
	sidecar := strings.TrimSuffix(target, filepath.Ext(target))
	jsonPath := sidecar + ".json"
	revisionPath := sidecar + ".revision"
	data, err := json.MarshalIndent(record, "", "  ")
	if err != nil {
		return "", err
	}
	data = append(data, '\n')
	modelTemp, err := os.CreateTemp(filepath.Dir(target), "."+filepath.Base(target)+"-*")
	if err != nil {
		return "", err
	}
	modelTempPath := modelTemp.Name()
	defer os.Remove(modelTempPath)
	stagedByRename := filepath.Clean(filepath.Dir(partial)) == filepath.Clean(filepath.Dir(target))
	if stagedByRename {
		if err = modelTemp.Close(); err != nil {
			return "", err
		}
		if err = os.Remove(modelTempPath); err != nil {
			return "", err
		}
		if err = os.Rename(partial, modelTempPath); err != nil {
			return "", err
		}
		defer func() {
			if _, statErr := os.Stat(partial); errors.Is(statErr, os.ErrNotExist) {
				_ = os.Rename(modelTempPath, partial)
			}
		}()
	} else {
		source, openErr := os.Open(partial)
		if openErr != nil {
			_ = modelTemp.Close()
			return "", openErr
		}
		_, copyErr := io.Copy(modelTemp, source)
		closeErr := source.Close()
		if copyErr != nil || closeErr != nil || modelTemp.Sync() != nil || modelTemp.Close() != nil {
			_ = modelTemp.Close()
			if copyErr != nil {
				return "", copyErr
			}
			if closeErr != nil {
				return "", closeErr
			}
			return "", errors.New("stage model publication")
		}
	}
	jsonTemp, err := writeTemp(filepath.Dir(jsonPath), filepath.Base(jsonPath), data)
	if err != nil {
		return "", err
	}
	defer os.Remove(jsonTemp)
	revisionTemp, err := writeTemp(filepath.Dir(revisionPath), filepath.Base(revisionPath), []byte(record.Source.ResolvedRevision+"\n"))
	if err != nil {
		return "", err
	}
	defer os.Remove(revisionTemp)
	if err = os.Rename(modelTempPath, target); err != nil {
		return "", err
	}
	published := []string{target}
	if err = os.Rename(jsonTemp, jsonPath); err == nil {
		published = append(published, jsonPath)
	}
	if err == nil {
		err = os.Rename(revisionTemp, revisionPath)
		if err == nil {
			published = append(published, revisionPath)
		}
	}
	if err != nil {
		for _, path := range published {
			if stagedByRename && path == target {
				_ = os.Rename(path, partial)
			} else {
				_ = os.Remove(path)
			}
		}
		return "", err
	}
	if !stagedByRename {
		if err = os.Remove(partial); err != nil {
			return "", err
		}
	}
	return target, nil
}

// PublishInPlace writes the .json/.revision sidecar pair for a .gguf that
// already exists at its final on-disk location (the shape reconcile always
// operates on, since Library derives Repository/Filename from a path already
// inside modelsDir). Unlike Publish, it never stages, copies, or renames the
// model file itself — only the two sidecar files are written, atomically.
func PublishInPlace(modelsDir string, record Record) (string, error) {
	target, err := TargetPath(modelsDir, record.Source)
	if err != nil {
		return "", err
	}
	if _, err = os.Stat(target); err != nil {
		return "", err
	}
	record.SchemaVersion = 1
	record.Artifact.DownloadedAt = time.Now().UTC().Format(time.RFC3339)
	sidecar := strings.TrimSuffix(target, filepath.Ext(target))
	jsonPath := sidecar + ".json"
	revisionPath := sidecar + ".revision"
	data, err := json.MarshalIndent(record, "", "  ")
	if err != nil {
		return "", err
	}
	data = append(data, '\n')
	jsonTemp, err := writeTemp(filepath.Dir(jsonPath), filepath.Base(jsonPath), data)
	if err != nil {
		return "", err
	}
	defer os.Remove(jsonTemp)
	revisionTemp, err := writeTemp(filepath.Dir(revisionPath), filepath.Base(revisionPath), []byte(record.Source.ResolvedRevision+"\n"))
	if err != nil {
		return "", err
	}
	defer os.Remove(revisionTemp)
	if err = os.Rename(jsonTemp, jsonPath); err != nil {
		return "", err
	}
	if err = os.Rename(revisionTemp, revisionPath); err != nil {
		_ = os.Remove(jsonPath)
		return "", err
	}
	return target, nil
}

func ReadRecord(target string) (Record, error) {
	base := strings.TrimSuffix(target, filepath.Ext(target))
	data, err := os.ReadFile(base + ".json")
	if err != nil {
		return Record{}, err
	}
	var r Record
	d := json.NewDecoder(bytes.NewReader(data))
	d.DisallowUnknownFields()
	if err = d.Decode(&r); err != nil {
		return Record{}, err
	}
	if r.SchemaVersion != 1 {
		return Record{}, fmt.Errorf("unsupported model sidecar schemaVersion %d", r.SchemaVersion)
	}
	if err = r.Source.Validate(); err != nil {
		return Record{}, fmt.Errorf("invalid model source sidecar: %w", err)
	}
	revision, err := os.ReadFile(base + ".revision")
	if err != nil {
		return Record{}, err
	}
	if string(revision) != r.Source.ResolvedRevision+"\n" {
		return Record{}, ErrInconsistentRevision
	}
	return r, nil
}
func writeAtomic(path string, data []byte) error {
	tmp, err := writeTemp(filepath.Dir(path), filepath.Base(path), data)
	if err != nil {
		return err
	}
	defer os.Remove(tmp)
	return os.Rename(tmp, path)
}
func writeTemp(dir, base string, data []byte) (string, error) {
	f, err := os.CreateTemp(dir, "."+base+"-*")
	if err != nil {
		return "", err
	}
	tmp := f.Name()
	if _, err = f.Write(data); err != nil {
		_ = f.Close()
		_ = os.Remove(tmp)
		return "", err
	}
	if err = f.Sync(); err != nil {
		_ = f.Close()
		_ = os.Remove(tmp)
		return "", err
	}
	if err = f.Close(); err != nil {
		_ = os.Remove(tmp)
		return "", err
	}
	return tmp, nil
}
