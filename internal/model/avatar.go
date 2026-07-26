package model

import (
	"bytes"
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"strings"
)

const maxPublisherAvatarBytes = 2 << 20

func Publisher(repository string) (string, error) {
	parts := strings.Split(repository, "/")
	if len(parts) != 2 || parts[0] == "" || parts[0] == "." || parts[0] == ".." || strings.ContainsAny(parts[0], `\:`) {
		return "", fmt.Errorf("invalid publisher namespace")
	}
	return parts[0], nil
}

func (m *Manager) EnsurePublisherAvatar(repository string) (string, error) {
	publisher, err := Publisher(repository)
	if err != nil {
		return "", err
	}
	dir := filepath.Join(m.ModelsDir, publisher)
	if existing, _ := filepath.Glob(filepath.Join(dir, "publisher-avatar.*")); len(existing) > 0 {
		return existing[0], nil
	}
	base := strings.TrimRight(m.Resolver.BaseURL, "/")
	if base == "" {
		base = "https://huggingface.co"
	}
	req, err := http.NewRequest(http.MethodGet, base+"/api/avatars/"+publisher, nil)
	if err != nil {
		return "", err
	}
	resp, err := m.client().Do(req)
	if err != nil {
		return "", err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return "", fmt.Errorf("publisher avatar: %s", resp.Status)
	}
	data, err := io.ReadAll(io.LimitReader(resp.Body, maxPublisherAvatarBytes+1))
	if err != nil {
		return "", err
	}
	if len(data) > maxPublisherAvatarBytes {
		return "", fmt.Errorf("publisher avatar exceeds size limit")
	}
	ext, err := avatarExtension(data)
	if err != nil {
		return "", err
	}
	if err = os.MkdirAll(dir, 0755); err != nil {
		return "", err
	}
	path := filepath.Join(dir, "publisher-avatar"+ext)
	if err = writeAtomic(path, data); err != nil {
		if _, statErr := os.Stat(path); statErr == nil {
			return path, nil
		}
		return "", err
	}
	return path, nil
}

func (m *Manager) PublisherAvatar(publisher string) (string, error) {
	if _, err := Publisher(publisher + "/repository"); err != nil {
		return "", err
	}
	paths, _ := filepath.Glob(filepath.Join(m.ModelsDir, publisher, "publisher-avatar.*"))
	if len(paths) == 0 {
		return "", os.ErrNotExist
	}
	return paths[0], nil
}

func avatarExtension(data []byte) (string, error) {
	switch {
	case bytes.HasPrefix(data, []byte("\x89PNG\r\n\x1a\n")):
		return ".png", nil
	case len(data) >= 12 && bytes.Equal(data[:4], []byte("RIFF")) && bytes.Equal(data[8:12], []byte("WEBP")):
		return ".webp", nil
	case bytes.HasPrefix(data, []byte{0xff, 0xd8, 0xff}):
		return ".jpg", nil
	default:
		return "", fmt.Errorf("publisher avatar is not PNG, JPEG, or WebP")
	}
}
