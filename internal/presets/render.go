package presets

import (
	"fmt"
	"sort"
	"strings"

	"github.com/mstranich/coquena/internal/fields"
	"github.com/mstranich/coquena/internal/model"
)

// Section is one rendered INI section: `[*]` for global, or a preset's name.
type Section struct {
	Name    string
	Entries []Entry
}

// Entry is one rendered `key = value` line, keyed by canonical field name.
type Entry struct {
	Key   string
	Value any
}

// RenderResult is a deterministic rendering of the persisted presets for one
// runtime, plus advisory diagnostics for anything omitted or notable.
type RenderResult struct {
	Sections    []Section
	Diagnostics []string
}

// Render is a pure function of doc, catalog, and the named runtime's
// provider and release: it issues no network request and performs no file
// I/O. modelsDir is used only to compute the on-disk path of a resolved
// model binding.
func Render(doc Document, catalog fields.Catalog, provider fields.Provider, release, modelsDir string) RenderResult {
	result := RenderResult{}
	result.Sections = append(result.Sections, Section{Name: "*", Entries: renderValues(doc.Global.Values)})

	names := make([]string, len(doc.Presets))
	for i, p := range doc.Presets {
		names[i] = p.Name
	}
	sort.Strings(names)
	byName := map[string]Preset{}
	for _, p := range doc.Presets {
		byName[p.Name] = p
	}

	for _, name := range names {
		p := byName[name]
		effective := Effective(doc.Global, p)
		computed := ComputeConstraint(catalog, provider, effective)
		status := EvaluateRuntime(provider, release, p.Runtime, computed)
		if status == fields.NotAdmitted {
			result.Diagnostics = append(result.Diagnostics, fmt.Sprintf("preset %q omitted: not admitted by runtime %s at %s", p.Name, provider, release))
			continue
		}
		entries := renderValues(p.Values)
		if p.Model != nil {
			if p.Model.Resolved != nil {
				target, err := model.PathFor(modelsDir, p.Model.Requested.Repository, p.Model.Resolved.Filename)
				if err == nil {
					entries = append([]Entry{{Key: "model", Value: target}}, entries...)
				} else {
					result.Diagnostics = append(result.Diagnostics, fmt.Sprintf("preset %q model path could not be computed: %s", p.Name, err))
				}
			} else {
				entries = append([]Entry{{Key: "hf", Value: p.Model.Requested.Raw}}, entries...)
			}
		}
		result.Sections = append(result.Sections, Section{Name: p.Name, Entries: entries})
	}
	return result
}

func renderValues(values map[string]any) []Entry {
	names := make([]string, 0, len(values))
	for name := range values {
		names = append(names, name)
	}
	sort.Strings(names)
	entries := make([]Entry, 0, len(names))
	for _, name := range names {
		entries = append(entries, Entry{Key: name, Value: values[name]})
	}
	return entries
}

// Format renders result as INI text, in `key = value` form, with each
// section preceded by a blank line except the first.
func Format(result RenderResult) string {
	var b strings.Builder
	for i, section := range result.Sections {
		if i > 0 {
			b.WriteString("\n")
		}
		fmt.Fprintf(&b, "[%s]\n", section.Name)
		for _, entry := range section.Entries {
			fmt.Fprintf(&b, "%s = %s\n", entry.Key, formatValue(entry.Value))
		}
	}
	return b.String()
}

func formatValue(value any) string {
	switch v := value.(type) {
	case string:
		return v
	case bool:
		if v {
			return "true"
		}
		return "false"
	case float64:
		if v == float64(int64(v)) {
			return fmt.Sprintf("%d", int64(v))
		}
		return fmt.Sprintf("%g", v)
	default:
		return fmt.Sprintf("%v", v)
	}
}
