// Package fields persists the durable llama.cpp field catalog: the typed
// vocabulary of INI keys that presets will later reference and render.
package fields

// SchemaVersion is the current on-disk catalog document version.
const SchemaVersion = 1

// Kind is a field's immutable value type.
type Kind string

const (
	KindString  Kind = "string"
	KindInteger Kind = "integer"
	KindBoolean Kind = "boolean"
	KindVoid    Kind = "void"
	KindEnum    Kind = "enum"
)

func (k Kind) valid() bool {
	switch k {
	case KindString, KindInteger, KindBoolean, KindVoid, KindEnum:
		return true
	default:
		return false
	}
}

// Provider identifies which llama.cpp distribution family admits a field.
type Provider string

// ProviderOfficial is the only provider recognized by this capability.
const ProviderOfficial Provider = "official"

// Availability declares the build range that admits a field for one provider.
type Availability struct {
	Provider     Provider `json:"provider"`
	IntroducedIn string   `json:"introducedIn,omitempty"`
	RemovedIn    string   `json:"removedIn,omitempty"`
}

// Field is one durable llama.cpp INI key definition.
type Field struct {
	Name          string        `json:"name"`
	Kind          Kind          `json:"kind"`
	Default       any           `json:"default,omitempty"`
	EnumOptions   []string      `json:"enumOptions,omitempty"`
	FalseArgument string        `json:"falseArgument,omitempty"`
	ShortArgument string        `json:"shortArgument,omitempty"`
	EnvVar        string        `json:"envVar,omitempty"`
	Parent        string        `json:"parent,omitempty"`
	ParentValue   any           `json:"parentValue,omitempty"`
	Availability  *Availability `json:"availability,omitempty"`
}

// Catalog is the persisted document containing every defined field.
type Catalog struct {
	SchemaVersion int     `json:"schemaVersion"`
	Fields        []Field `json:"fields"`
}

// Find returns the field whose canonical name matches, ignoring aliases.
func (c Catalog) Find(name string) (Field, bool) {
	for _, f := range c.Fields {
		if f.Name == name {
			return f, true
		}
	}
	return Field{}, false
}

// Resolve returns the field identified by canonical name, short argument, or
// environment variable alias, reporting it under its canonical name.
func (c Catalog) Resolve(nameOrAlias string) (Field, bool) {
	if f, ok := c.Find(nameOrAlias); ok {
		return f, true
	}
	for _, f := range c.Fields {
		if f.ShortArgument == nameOrAlias || f.EnvVar == nameOrAlias {
			return f, true
		}
	}
	return Field{}, false
}

// Children returns the fields whose parent prerequisite is name.
func (c Catalog) Children(name string) []Field {
	var children []Field
	for _, f := range c.Fields {
		if f.Parent == name {
			children = append(children, f)
		}
	}
	return children
}
