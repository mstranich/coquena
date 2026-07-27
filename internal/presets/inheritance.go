package presets

// Source identifies where an effective value came from.
type Source string

const (
	SourceOwn    Source = "own"
	SourceGlobal Source = "global"
)

// EffectiveValue is one field's value merged from global and a preset's own
// layer, with the layer it came from.
type EffectiveValue struct {
	Field  string `json:"field"`
	Value  any    `json:"value"`
	Source Source `json:"source"`
}

// Effective merges the global preset with p's own layer. Calling it on the
// global preset itself reports every value as own.
func Effective(global, p Preset) []EffectiveValue {
	if p.IsGlobal() {
		out := make([]EffectiveValue, 0, len(p.Values))
		for field, value := range p.Values {
			out = append(out, EffectiveValue{Field: field, Value: value, Source: SourceOwn})
		}
		return out
	}
	seen := map[string]bool{}
	out := make([]EffectiveValue, 0, len(global.Values)+len(p.Values))
	for field, value := range p.Values {
		out = append(out, EffectiveValue{Field: field, Value: value, Source: SourceOwn})
		seen[field] = true
	}
	for field, value := range global.Values {
		if seen[field] {
			continue
		}
		out = append(out, EffectiveValue{Field: field, Value: value, Source: SourceGlobal})
	}
	return out
}
