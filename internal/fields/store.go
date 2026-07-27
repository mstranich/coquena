package fields

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
)

const catalogFilename = "llamacpp-fields.json"

// Store resolves and mutates the persisted field catalog beneath an
// application home directory.
type Store struct {
	home string
	ops  operations
}

type operations struct {
	readFile   func(string) ([]byte, error)
	createTemp func(string, string) (*os.File, error)
	rename     func(string, string) error
	remove     func(string) error
	stat       func(string) (os.FileInfo, error)
}

func defaultOperations() operations {
	return operations{
		readFile:   os.ReadFile,
		createTemp: os.CreateTemp,
		rename:     os.Rename,
		remove:     os.Remove,
		stat:       os.Stat,
	}
}

// NewStore returns a Store rooted at the given application home.
func NewStore(home string) *Store { return &Store{home: home, ops: defaultOperations()} }

func (s *Store) path() string { return filepath.Join(s.home, "config", catalogFilename) }

// Load reads the persisted catalog. An installation with no catalog document
// resolves an empty catalog rather than an error.
func (s *Store) Load() (Catalog, error) {
	data, err := s.ops.readFile(s.path())
	if errors.Is(err, os.ErrNotExist) {
		return Catalog{SchemaVersion: SchemaVersion}, nil
	}
	if err != nil {
		return Catalog{}, fmt.Errorf("read field catalog %q: %w", s.path(), err)
	}
	return parseCatalog(data, s.path())
}

func parseCatalog(data []byte, path string) (Catalog, error) {
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.DisallowUnknownFields()
	var c Catalog
	if err := decoder.Decode(&c); err != nil {
		return Catalog{}, fmt.Errorf("invalid field catalog %q: %w", path, err)
	}
	var extra any
	if err := decoder.Decode(&extra); !errors.Is(err, io.EOF) {
		return Catalog{}, fmt.Errorf("invalid field catalog %q: multiple JSON values", path)
	}
	if c.SchemaVersion != SchemaVersion {
		return Catalog{}, fmt.Errorf("field catalog %q has unsupported schemaVersion %d", path, c.SchemaVersion)
	}
	return c, nil
}

// Define adds a new field. It fails if a field with the same canonical name
// already exists; use Upsert to replace it.
func (s *Store) Define(f Field) (Catalog, error) {
	c, err := s.Load()
	if err != nil {
		return Catalog{}, err
	}
	if _, exists := c.Find(f.Name); exists {
		return Catalog{}, fmt.Errorf("field %q already exists; use overwrite to replace it", f.Name)
	}
	return s.publishWith(c, append(c.Fields, f))
}

// Upsert creates the field if absent and replaces it if present.
func (s *Store) Upsert(f Field) (Catalog, error) {
	c, err := s.Load()
	if err != nil {
		return Catalog{}, err
	}
	replaced := false
	updated := make([]Field, 0, len(c.Fields)+1)
	for _, existing := range c.Fields {
		if existing.Name == f.Name {
			updated = append(updated, f)
			replaced = true
			continue
		}
		updated = append(updated, existing)
	}
	if !replaced {
		updated = append(updated, f)
	}
	return s.publishWith(c, updated)
}

// Remove deletes the field identified by canonical name or alias, along with
// its descendant subtree.
func (s *Store) Remove(nameOrAlias string) (Catalog, error) {
	c, err := s.Load()
	if err != nil {
		return Catalog{}, err
	}
	resolved, exists := c.Resolve(nameOrAlias)
	if !exists {
		return Catalog{}, fmt.Errorf("field %q not found", nameOrAlias)
	}
	subtree := c.Subtree(resolved.Name)
	removed := map[string]bool{}
	for _, n := range subtree {
		removed[n] = true
	}
	remaining := make([]Field, 0, len(c.Fields))
	for _, f := range c.Fields {
		if !removed[f.Name] {
			remaining = append(remaining, f)
		}
	}
	return s.publishWith(c, remaining)
}

func (s *Store) publishWith(current Catalog, updatedFields []Field) (Catalog, error) {
	if err := ValidateCatalog(updatedFields); err != nil {
		return Catalog{}, err
	}
	updated := Catalog{SchemaVersion: SchemaVersion, Fields: updatedFields}
	data, err := json.MarshalIndent(updated, "", "  ")
	if err != nil {
		return Catalog{}, fmt.Errorf("encode field catalog: %w", err)
	}
	data = append(data, '\n')
	if err := s.publish(data); err != nil {
		return Catalog{}, err
	}
	return updated, nil
}

func (s *Store) publish(data []byte) (err error) {
	path := s.path()
	dir := filepath.Dir(path)
	temporary, err := s.ops.createTemp(dir, ".llamacpp-fields.json-*")
	if err != nil {
		return fmt.Errorf("create temporary field catalog beside %q: %w", path, err)
	}
	temporaryPath := temporary.Name()
	defer func() {
		_ = temporary.Close()
		_ = s.ops.remove(temporaryPath)
	}()

	if _, err = temporary.Write(data); err != nil {
		return fmt.Errorf("write temporary field catalog %q: %w", temporaryPath, err)
	}
	if err = temporary.Sync(); err != nil {
		return fmt.Errorf("sync temporary field catalog %q: %w", temporaryPath, err)
	}
	if err = temporary.Close(); err != nil {
		return fmt.Errorf("close temporary field catalog %q: %w", temporaryPath, err)
	}
	if _, err = s.ops.stat(path); err != nil && !errors.Is(err, os.ErrNotExist) {
		return fmt.Errorf("inspect field catalog destination %q: %w", path, err)
	}
	if err = s.ops.rename(temporaryPath, path); err != nil {
		return fmt.Errorf("publish field catalog %q: %w", path, err)
	}
	return nil
}
