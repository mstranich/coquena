package presets

import "testing"

func findValue(values []EffectiveValue, field string) (EffectiveValue, bool) {
	for _, v := range values {
		if v.Field == field {
			return v, true
		}
	}
	return EffectiveValue{}, false
}

func TestEffectiveInheritsFromGlobal(t *testing.T) {
	global := Preset{Name: GlobalName, Values: map[string]any{"temp": 0.7}}
	p := Preset{Name: "qwen3-fast"}
	values := Effective(global, p)
	v, ok := findValue(values, "temp")
	if !ok || v.Source != SourceGlobal || v.Value != 0.7 {
		t.Fatalf("v=%+v ok=%v", v, ok)
	}
}

func TestEffectiveOverridesGlobal(t *testing.T) {
	global := Preset{Name: GlobalName, Values: map[string]any{"temp": 0.7}}
	p := Preset{Name: "qwen3-fast", Values: map[string]any{"temp": 0.9}}
	values := Effective(global, p)
	v, ok := findValue(values, "temp")
	if !ok || v.Source != SourceOwn || v.Value != 0.9 {
		t.Fatalf("v=%+v ok=%v", v, ok)
	}
}

func TestEffectiveReturnsToInheritanceAfterUnset(t *testing.T) {
	global := Preset{Name: GlobalName, Values: map[string]any{"temp": 0.7}}
	p := Preset{Name: "qwen3-fast", Values: map[string]any{}}
	values := Effective(global, p)
	v, ok := findValue(values, "temp")
	if !ok || v.Source != SourceGlobal {
		t.Fatalf("v=%+v ok=%v", v, ok)
	}
}

func TestEffectiveOmitsUnauthoredField(t *testing.T) {
	global := Preset{Name: GlobalName}
	p := Preset{Name: "qwen3-fast"}
	if values := Effective(global, p); len(values) != 0 {
		t.Fatalf("values=%+v", values)
	}
}

func TestEffectiveGlobalAloneIsAlwaysOwn(t *testing.T) {
	global := Preset{Name: GlobalName, Values: map[string]any{"temp": 0.7}}
	values := Effective(global, global)
	v, ok := findValue(values, "temp")
	if !ok || v.Source != SourceOwn {
		t.Fatalf("v=%+v ok=%v", v, ok)
	}
}
