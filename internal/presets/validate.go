package presets

import (
	"fmt"
	"regexp"
)

// nameRE admits lowercase alphanumeric segments separated by '-' or '.'.
// The dot is deliberate: upstream's own preset.md examples name a model
// "Qwen3.5-4B", and a preset name that cannot represent a dotted version
// number would force operators to mangle it.
var nameRE = regexp.MustCompile(`^[a-z0-9]+([-.][a-z0-9]+)*$`)

func validName(name string) bool { return nameRE.MatchString(name) }

func isReserved(name string) bool {
	for _, r := range ReservedNames {
		if name == r {
			return true
		}
	}
	return false
}

// ValidateDocument checks the whole document: name form, uniqueness, the
// reserved names, and pin/capability contradiction on every preset.
func ValidateDocument(d Document) error {
	if !d.Global.IsGlobal() {
		return fmt.Errorf("global preset must be named %q", GlobalName)
	}
	if d.Global.Model != nil {
		return fmt.Errorf("global preset cannot bind a model")
	}
	if d.Global.Runtime != nil {
		return fmt.Errorf("global preset cannot bind a runtime")
	}
	seen := map[string]bool{}
	for _, p := range d.Presets {
		if !validName(p.Name) {
			return fmt.Errorf("preset name %q must be lowercase kebab-case", p.Name)
		}
		if isReserved(p.Name) {
			return fmt.Errorf("preset name %q is reserved", p.Name)
		}
		if seen[p.Name] {
			return fmt.Errorf("duplicate preset name %q", p.Name)
		}
		seen[p.Name] = true
		if err := validateRuntimeConstraint(p.Runtime); err != nil {
			return fmt.Errorf("preset %q: %w", p.Name, err)
		}
	}
	return nil
}

func validateRuntimeConstraint(c *RuntimeConstraint) error {
	if c == nil {
		return nil
	}
	if c.IsPin() && c.hasCapabilityFields() {
		return fmt.Errorf("runtime constraint cannot combine an explicit runtime id with a capability constraint")
	}
	return nil
}
