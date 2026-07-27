// Package presets persists model presets: the global default preset and any
// number of model presets, each binding to a model and, optionally, to the
// runtimes admitted to run it.
package presets

// SchemaVersion is the current on-disk preset document version.
const SchemaVersion = 1

// GlobalName is the reserved identifier for the preset that renders as the
// INI `[*]` section. It cannot be created, renamed, or removed.
const GlobalName = "global"

// ReservedNames lists identifiers a preset name may not use.
var ReservedNames = []string{GlobalName, "render"}

// ModelReference is what an operator requested when binding a preset to a
// model, using the same `user/repo:QUANT` grammar accepted for model
// downloads. It is durable and is never rewritten by resolution.
type ModelReference struct {
	Provider   string `json:"provider"`
	Repository string `json:"repository"`
	Quant      string `json:"quant,omitempty"`
	Raw        string `json:"raw"`
}

// ResolvedModel is the advisory attestation of what a ModelReference matched
// in the installed library at the time of binding.
type ResolvedModel struct {
	Filename         string `json:"filename"`
	ResolvedRevision string `json:"resolvedRevision,omitempty"`
	ResolvedAt       string `json:"resolvedAt,omitempty"`
}

// ModelBinding is a preset's reference to the model it configures.
type ModelBinding struct {
	Requested ModelReference `json:"requested"`
	Resolved  *ResolvedModel `json:"resolved,omitempty"`
}

// RuntimeConstraint declares which runtimes may run a preset. RuntimeID is a
// hard identity pin and, when set, excludes every capability field.
type RuntimeConstraint struct {
	RuntimeID  string `json:"runtimeId,omitempty"`
	Provider   string `json:"provider,omitempty"`
	Backend    string `json:"backend,omitempty"`
	MinRelease string `json:"minRelease,omitempty"`
	MaxRelease string `json:"maxRelease,omitempty"`
}

// IsPin reports whether the constraint is a hard runtime identity pin.
func (c RuntimeConstraint) IsPin() bool { return c.RuntimeID != "" }

// IsEmpty reports whether the constraint declares nothing, meaning the
// preset is portable.
func (c RuntimeConstraint) IsEmpty() bool { return c == RuntimeConstraint{} }

// hasCapabilityFields reports whether any capability-form field is set.
func (c RuntimeConstraint) hasCapabilityFields() bool {
	return c.Provider != "" || c.Backend != "" || c.MinRelease != "" || c.MaxRelease != ""
}

// Preset is one durable preset: the global preset or a model preset.
type Preset struct {
	Name    string             `json:"name"`
	Title   string             `json:"title,omitempty"`
	Model   *ModelBinding      `json:"model,omitempty"`
	Runtime *RuntimeConstraint `json:"runtime,omitempty"`
	// Companions are the preset's advisory companion role assignments,
	// alongside its principal binding. At most one assignment per role.
	Companions []CompanionAssignment `json:"companions,omitempty"`
	Values     map[string]any        `json:"values,omitempty"`
}

// IsGlobal reports whether this is the reserved global preset.
func (p Preset) IsGlobal() bool { return p.Name == GlobalName }

// Document is the persisted collection of presets.
type Document struct {
	SchemaVersion int      `json:"schemaVersion"`
	Global        Preset   `json:"global"`
	Presets       []Preset `json:"presets"`
}

// Find returns the named preset, checking global first.
func (d Document) Find(name string) (Preset, bool) {
	if name == GlobalName {
		return d.Global, true
	}
	for _, p := range d.Presets {
		if p.Name == name {
			return p, true
		}
	}
	return Preset{}, false
}
