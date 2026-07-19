package store

import (
	"archive/tar"
	"archive/zip"
	"compress/gzip"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"
)

func safeArchivePath(root, name string) (string, error) {
	if name == "" || strings.ContainsRune(name, 0) || strings.Contains(name, "\\") || filepath.IsAbs(name) {
		return "", fmt.Errorf("unsafe archive path %q", name)
	}
	clean := filepath.Clean(filepath.FromSlash(name))
	if clean == "." || clean == ".." || strings.HasPrefix(clean, ".."+string(filepath.Separator)) || filepath.VolumeName(clean) != "" {
		return "", fmt.Errorf("unsafe archive path %q", name)
	}
	p := filepath.Join(root, clean)
	rel, e := filepath.Rel(root, p)
	if e != nil || rel == ".." || strings.HasPrefix(rel, ".."+string(filepath.Separator)) {
		return "", fmt.Errorf("archive path escapes destination %q", name)
	}
	return p, nil
}

type extractState struct {
	limits   Limits
	entries  int
	expanded int64
	seen     map[string]bool
}

func (s *extractState) add(name string, size int64) (string, error) {
	s.entries++
	s.expanded += size
	if s.entries > s.limits.MaxEntries {
		return "", errors.New("archive entry limit exceeded")
	}
	if size < 0 || s.expanded > s.limits.MaxExpandedBytes {
		return "", errors.New("archive expanded-size limit exceeded")
	}
	key := strings.ToLower(filepath.Clean(filepath.FromSlash(name)))
	if s.seen[key] {
		return "", fmt.Errorf("duplicate archive path %q", name)
	}
	s.seen[key] = true
	return key, nil
}

func ExtractZIP(archive, dest string, limits Limits) error {
	limits = limits.normalized()
	r, e := zip.OpenReader(archive)
	if e != nil {
		return e
	}
	defer r.Close()
	s := extractState{limits: limits, seen: map[string]bool{}}
	for _, f := range r.File {
		if _, e = s.add(f.Name, int64(f.UncompressedSize64)); e != nil {
			return e
		}
		p, e := safeArchivePath(dest, f.Name)
		if e != nil {
			return e
		}
		mode := f.Mode()
		if mode&os.ModeSymlink != 0 || mode&(os.ModeDevice|os.ModeNamedPipe|os.ModeSocket) != 0 {
			return fmt.Errorf("unsupported archive entry %q", f.Name)
		}
		if f.FileInfo().IsDir() {
			if e = os.MkdirAll(p, 0755); e != nil {
				return e
			}
			continue
		}
		if !mode.IsRegular() {
			return fmt.Errorf("unsupported archive entry %q", f.Name)
		}
		if e = os.MkdirAll(filepath.Dir(p), 0755); e != nil {
			return e
		}
		in, e := f.Open()
		if e != nil {
			return e
		}
		e = writeBoundedFile(p, in, int64(f.UncompressedSize64), mode)
		in.Close()
		if e != nil {
			return e
		}
	}
	return nil
}

func ExtractTarGZ(archive, dest string, limits Limits) error {
	limits = limits.normalized()
	f, e := os.Open(archive)
	if e != nil {
		return e
	}
	defer f.Close()
	gz, e := gzip.NewReader(f)
	if e != nil {
		return e
	}
	defer gz.Close()
	tr := tar.NewReader(gz)
	s := extractState{limits: limits, seen: map[string]bool{}}
	for {
		h, e := tr.Next()
		if errors.Is(e, io.EOF) {
			break
		}
		if e != nil {
			return e
		}
		if _, e = s.add(h.Name, h.Size); e != nil {
			return e
		}
		p, e := safeArchivePath(dest, h.Name)
		if e != nil {
			return e
		}
		switch h.Typeflag {
		case tar.TypeDir:
			if e = os.MkdirAll(p, 0755); e != nil {
				return e
			}
		case tar.TypeReg, tar.TypeRegA:
			if e = os.MkdirAll(filepath.Dir(p), 0755); e != nil {
				return e
			}
			if e = writeBoundedFile(p, tr, h.Size, os.FileMode(h.Mode)); e != nil {
				return e
			}
		default:
			return fmt.Errorf("unsupported archive entry %q", h.Name)
		}
	}
	return nil
}
func writeBoundedFile(path string, r io.Reader, size int64, mode os.FileMode) error {
	if size < 0 {
		return errors.New("negative archive entry size")
	}
	perm := mode.Perm() & 0755
	if perm == 0 {
		perm = 0644
	}
	f, e := os.OpenFile(path, os.O_CREATE|os.O_EXCL|os.O_WRONLY, perm)
	if e != nil {
		return e
	}
	ok := false
	defer func() {
		f.Close()
		if !ok {
			os.Remove(path)
		}
	}()
	n, e := io.Copy(f, io.LimitReader(r, size+1))
	if e != nil {
		return e
	}
	if n != size {
		return fmt.Errorf("archive entry size mismatch")
	}
	if e = f.Close(); e != nil {
		return e
	}
	ok = true
	return nil
}
