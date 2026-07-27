package presets

import (
	"fmt"
	"strings"

	"github.com/mstranich/coquena/internal/fields"
)

// ReferencingPresets reports the names of every preset — including "global"
// — that holds a value for fieldName or any of its descendants in catalog.
// It is the check a field removal must pass before it may proceed unforced.
func ReferencingPresets(doc Document, catalog fields.Catalog, fieldName string) []string {
	subtree := map[string]bool{}
	for _, n := range catalog.Subtree(fieldName) {
		subtree[n] = true
	}
	var referencing []string
	if presetReferences(doc.Global, subtree) {
		referencing = append(referencing, GlobalName)
	}
	for _, p := range doc.Presets {
		if presetReferences(p, subtree) {
			referencing = append(referencing, p.Name)
		}
	}
	return referencing
}

func presetReferences(p Preset, subtree map[string]bool) bool {
	for field := range p.Values {
		if subtree[field] {
			return true
		}
	}
	return false
}

// CascadeRemoveFieldValues returns doc with every value for fieldNames
// removed from every preset, including global. It is pure: the caller is
// responsible for publishing the result.
func CascadeRemoveFieldValues(doc Document, fieldNames []string) Document {
	drop := map[string]bool{}
	for _, n := range fieldNames {
		drop[n] = true
	}
	doc.Global = dropValues(doc.Global, drop)
	presets := make([]Preset, len(doc.Presets))
	for i, p := range doc.Presets {
		presets[i] = dropValues(p, drop)
	}
	doc.Presets = presets
	return doc
}

// RemoveField removes a field (and its descendant subtree) from catalogStore,
// refusing unforced removal when any preset in presetStore — including
// global — holds a value for the field or a descendant. A forced removal
// first publishes the preset document with those values dropped, and only
// then removes the field from the catalog, so a failure at either step never
// leaves one document referencing content the other has dropped.
func RemoveField(catalogStore *fields.Store, presetStore *Store, nameOrAlias string, force bool) (fields.Catalog, Document, error) {
	catalog, err := catalogStore.Load()
	if err != nil {
		return fields.Catalog{}, Document{}, err
	}
	resolved, ok := catalog.Resolve(nameOrAlias)
	if !ok {
		return fields.Catalog{}, Document{}, fmt.Errorf("field %q not found", nameOrAlias)
	}
	doc, err := presetStore.Load()
	if err != nil {
		return fields.Catalog{}, Document{}, err
	}
	referencing := ReferencingPresets(doc, catalog, resolved.Name)
	if len(referencing) > 0 && !force {
		return fields.Catalog{}, Document{}, fmt.Errorf("field %q is referenced by preset(s) %s; pass force to remove it and their values", resolved.Name, strings.Join(referencing, ", "))
	}
	if len(referencing) > 0 {
		cascaded := CascadeRemoveFieldValues(doc, catalog.Subtree(resolved.Name))
		doc, err = presetStore.PublishDocument(cascaded)
		if err != nil {
			return fields.Catalog{}, Document{}, err
		}
	}
	catalog, err = catalogStore.Remove(resolved.Name)
	if err != nil {
		return fields.Catalog{}, Document{}, err
	}
	return catalog, doc, nil
}

func dropValues(p Preset, drop map[string]bool) Preset {
	if len(p.Values) == 0 {
		return p
	}
	values := make(map[string]any, len(p.Values))
	for field, value := range p.Values {
		if !drop[field] {
			values[field] = value
		}
	}
	p.Values = values
	return p
}
