package presets

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"

	"github.com/mstranich/coquena/internal/fields"
)

const documentFilename = "model-presets.json"

// Store resolves and mutates the persisted preset document beneath an
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

func (s *Store) path() string { return filepath.Join(s.home, "config", documentFilename) }

// Load reads the persisted document. An installation with no document
// resolves an empty document with an empty global preset, rather than an
// error.
func (s *Store) Load() (Document, error) {
	data, err := s.ops.readFile(s.path())
	if errors.Is(err, os.ErrNotExist) {
		return Document{SchemaVersion: SchemaVersion, Global: Preset{Name: GlobalName}}, nil
	}
	if err != nil {
		return Document{}, fmt.Errorf("read preset document %q: %w", s.path(), err)
	}
	return parseDocument(data, s.path())
}

func parseDocument(data []byte, path string) (Document, error) {
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.DisallowUnknownFields()
	var d Document
	if err := decoder.Decode(&d); err != nil {
		return Document{}, fmt.Errorf("invalid preset document %q: %w", path, err)
	}
	var extra any
	if err := decoder.Decode(&extra); !errors.Is(err, io.EOF) {
		return Document{}, fmt.Errorf("invalid preset document %q: multiple JSON values", path)
	}
	if d.SchemaVersion != SchemaVersion {
		return Document{}, fmt.Errorf("preset document %q has unsupported schemaVersion %d", path, d.SchemaVersion)
	}
	return d, nil
}

// Define adds a new preset. It fails if a preset with the same name already
// exists.
func (s *Store) Define(p Preset) (Document, error) {
	d, err := s.Load()
	if err != nil {
		return Document{}, err
	}
	if _, exists := d.Find(p.Name); exists {
		return Document{}, fmt.Errorf("preset %q already exists", p.Name)
	}
	return s.publishWith(d, append(append([]Preset(nil), d.Presets...), p))
}

// Remove deletes the named preset. Removing the global preset is refused.
func (s *Store) Remove(name string) (Document, error) {
	d, err := s.Load()
	if err != nil {
		return Document{}, err
	}
	if name == GlobalName {
		return Document{}, fmt.Errorf("the global preset cannot be removed")
	}
	if _, exists := d.Find(name); !exists {
		return Document{}, fmt.Errorf("preset %q not found", name)
	}
	remaining := make([]Preset, 0, len(d.Presets))
	for _, p := range d.Presets {
		if p.Name != name {
			remaining = append(remaining, p)
		}
	}
	return s.publishWith(d, remaining)
}

// Rename moves a preset to a new name, preserving its title, bindings, and
// values.
func (s *Store) Rename(oldName, newName string) (Document, error) {
	d, err := s.Load()
	if err != nil {
		return Document{}, err
	}
	if oldName == GlobalName {
		return Document{}, fmt.Errorf("the global preset cannot be renamed")
	}
	if _, exists := d.Find(newName); exists {
		return Document{}, fmt.Errorf("preset %q already exists", newName)
	}
	updated := make([]Preset, 0, len(d.Presets))
	found := false
	for _, p := range d.Presets {
		if p.Name == oldName {
			p.Name = newName
			found = true
		}
		updated = append(updated, p)
	}
	if !found {
		return Document{}, fmt.Errorf("preset %q not found", oldName)
	}
	return s.publishWith(d, updated)
}

// mutate loads the document, applies fn to the addressed preset's own layer,
// validates, and publishes.
func (s *Store) mutate(name string, fn func(Preset) (Preset, error)) (Document, error) {
	d, err := s.Load()
	if err != nil {
		return Document{}, err
	}
	if name == GlobalName {
		updated, err := fn(d.Global)
		if err != nil {
			return Document{}, err
		}
		updated.Name = GlobalName
		d.Global = updated
		return s.publishWith(d, d.Presets)
	}
	updated := make([]Preset, 0, len(d.Presets))
	found := false
	for _, p := range d.Presets {
		if p.Name == name {
			mutated, err := fn(p)
			if err != nil {
				return Document{}, err
			}
			mutated.Name = name
			updated = append(updated, mutated)
			found = true
			continue
		}
		updated = append(updated, p)
	}
	if !found {
		return Document{}, fmt.Errorf("preset %q not found", name)
	}
	return s.publishWith(d, updated)
}

// SetTitle stores a preset's display title.
func (s *Store) SetTitle(name, title string) (Document, error) {
	return s.mutate(name, func(p Preset) (Preset, error) {
		p.Title = title
		return p, nil
	})
}

// SetModel stores a preset's model binding. It is refused on the global
// preset.
func (s *Store) SetModel(name string, binding ModelBinding) (Document, error) {
	if name == GlobalName {
		return Document{}, fmt.Errorf("the global preset cannot bind a model")
	}
	return s.mutate(name, func(p Preset) (Preset, error) {
		p.Model = &binding
		return p, nil
	})
}

// SetRuntime stores a preset's runtime constraint. It is refused on the
// global preset.
func (s *Store) SetRuntime(name string, constraint RuntimeConstraint) (Document, error) {
	if name == GlobalName {
		return Document{}, fmt.Errorf("the global preset cannot bind a runtime")
	}
	return s.mutate(name, func(p Preset) (Preset, error) {
		p.Runtime = &constraint
		return p, nil
	})
}

// UnsetRuntime removes a preset's runtime constraint, making it portable.
func (s *Store) UnsetRuntime(name string) (Document, error) {
	return s.mutate(name, func(p Preset) (Preset, error) {
		p.Runtime = nil
		return p, nil
	})
}

// SetValue validates value against catalog and stores it in the addressed
// preset's own layer, returning any advisory diagnostic.
func (s *Store) SetValue(catalog fields.Catalog, name, fieldName string, value any) (Document, ValidatedValue, error) {
	var result ValidatedValue
	d, err := s.mutate(name, func(p Preset) (Preset, error) {
		current := p.Values
		validated, err := ValidateValue(catalog, fieldName, value, current)
		if err != nil {
			return Preset{}, err
		}
		result = validated
		if p.Values == nil {
			p.Values = map[string]any{}
		} else {
			p.Values = cloneValues(p.Values)
		}
		p.Values[validated.Field] = value
		return p, nil
	})
	return d, result, err
}

// UnsetValue removes a value from the addressed preset's own layer, so that
// a model preset returns to inheriting from global.
func (s *Store) UnsetValue(catalog fields.Catalog, name, fieldName string) (Document, error) {
	field, ok := catalog.Resolve(fieldName)
	if !ok {
		return Document{}, fmt.Errorf("field %q is not defined in the catalog", fieldName)
	}
	return s.mutate(name, func(p Preset) (Preset, error) {
		if p.Values == nil {
			return p, nil
		}
		p.Values = cloneValues(p.Values)
		delete(p.Values, field.Name)
		return p, nil
	})
}

func cloneValues(values map[string]any) map[string]any {
	out := make(map[string]any, len(values))
	for k, v := range values {
		out[k] = v
	}
	return out
}

func (s *Store) publishWith(current Document, presets []Preset) (Document, error) {
	return s.PublishDocument(Document{SchemaVersion: SchemaVersion, Global: current.Global, Presets: presets})
}

// PublishDocument validates and atomically publishes d as-is, replacing the
// whole document. It exists for cross-document operations, such as a
// cascading field removal, that must change more than one preset's own
// layer in a single publication.
func (s *Store) PublishDocument(d Document) (Document, error) {
	updated := Document{SchemaVersion: SchemaVersion, Global: d.Global, Presets: d.Presets}
	if err := ValidateDocument(updated); err != nil {
		return Document{}, err
	}
	data, err := json.MarshalIndent(updated, "", "  ")
	if err != nil {
		return Document{}, fmt.Errorf("encode preset document: %w", err)
	}
	data = append(data, '\n')
	if err := s.publish(data); err != nil {
		return Document{}, err
	}
	return updated, nil
}

func (s *Store) publish(data []byte) (err error) {
	path := s.path()
	dir := filepath.Dir(path)
	temporary, err := s.ops.createTemp(dir, ".model-presets.json-*")
	if err != nil {
		return fmt.Errorf("create temporary preset document beside %q: %w", path, err)
	}
	temporaryPath := temporary.Name()
	defer func() {
		_ = temporary.Close()
		_ = s.ops.remove(temporaryPath)
	}()

	if _, err = temporary.Write(data); err != nil {
		return fmt.Errorf("write temporary preset document %q: %w", temporaryPath, err)
	}
	if err = temporary.Sync(); err != nil {
		return fmt.Errorf("sync temporary preset document %q: %w", temporaryPath, err)
	}
	if err = temporary.Close(); err != nil {
		return fmt.Errorf("close temporary preset document %q: %w", temporaryPath, err)
	}
	if _, err = s.ops.stat(path); err != nil && !errors.Is(err, os.ErrNotExist) {
		return fmt.Errorf("inspect preset document destination %q: %w", path, err)
	}
	if err = s.ops.rename(temporaryPath, path); err != nil {
		return fmt.Errorf("publish preset document %q: %w", path, err)
	}
	return nil
}
