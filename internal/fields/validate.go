package fields

import (
	"fmt"
	"strings"
)

// validName reports whether name is a bare INI key: no leading dashes, no
// whitespace, and no '='.
func validName(name string) bool {
	if name == "" || strings.HasPrefix(name, "-") {
		return false
	}
	return !strings.ContainsAny(name, " \t\r\n=")
}

// Validate checks one field in isolation: name shape, known kind, and enum
// option validity. It does not check catalog-wide uniqueness or parent
// resolution; use ValidateCatalog for that.
func (f Field) Validate() error {
	if !validName(f.Name) {
		return fmt.Errorf("field name %q must be a bare INI key without leading dashes, whitespace, or '='", f.Name)
	}
	if f.Kind == "" {
		return fmt.Errorf("field %q must declare a kind", f.Name)
	}
	if !f.Kind.valid() {
		return fmt.Errorf("field %q has unknown kind %q; supported kinds are string, integer, boolean, void, enum", f.Name, f.Kind)
	}
	if f.ShortArgument != "" && !validName(f.ShortArgument) {
		return fmt.Errorf("field %q has an invalid short-argument alias %q", f.Name, f.ShortArgument)
	}
	if f.Kind == KindEnum {
		if len(f.EnumOptions) == 0 {
			return fmt.Errorf("enum field %q must declare at least one option", f.Name)
		}
		seen := map[string]bool{}
		for _, o := range f.EnumOptions {
			if o == "" {
				return fmt.Errorf("enum field %q has an empty option", f.Name)
			}
			if seen[o] {
				return fmt.Errorf("enum field %q has duplicate option %q", f.Name, o)
			}
			seen[o] = true
		}
	}
	if f.Availability != nil {
		if f.Availability.Provider == "" {
			return fmt.Errorf("field %q availability must declare a provider", f.Name)
		}
		if f.Availability.Provider != ProviderOfficial {
			return fmt.Errorf("field %q declares unsupported provider %q", f.Name, f.Availability.Provider)
		}
		if f.Availability.IntroducedIn != "" && !validTag(f.Availability.IntroducedIn) {
			return fmt.Errorf("field %q has an invalid introducedIn release %q", f.Name, f.Availability.IntroducedIn)
		}
		if f.Availability.RemovedIn != "" && !validTag(f.Availability.RemovedIn) {
			return fmt.Errorf("field %q has an invalid removedIn release %q", f.Name, f.Availability.RemovedIn)
		}
	}
	return nil
}

// ValidateCatalog checks a whole catalog for name/alias uniqueness and
// resolvable parent references, in addition to validating every field.
func ValidateCatalog(catalogFields []Field) error {
	canonical := map[string]bool{}
	names := map[string]string{} // name-or-alias -> owning canonical name (first occurrence)
	for _, f := range catalogFields {
		if err := f.Validate(); err != nil {
			return err
		}
		if canonical[f.Name] {
			return fmt.Errorf("duplicate field name %q", f.Name)
		}
		canonical[f.Name] = true
		for _, key := range []string{f.Name, f.ShortArgument, f.EnvVar} {
			if key == "" {
				continue
			}
			if owner, exists := names[key]; exists && owner != f.Name {
				return fmt.Errorf("name or alias %q is already used by field %q", key, owner)
			}
			names[key] = f.Name
		}
	}
	for _, f := range catalogFields {
		if f.Parent == "" {
			continue
		}
		if f.Parent == f.Name {
			return fmt.Errorf("field %q cannot declare itself as its parent", f.Name)
		}
		if _, ok := findByName(catalogFields, f.Parent); !ok {
			return fmt.Errorf("field %q declares an unresolved parent %q", f.Name, f.Parent)
		}
	}
	return nil
}

func findByName(catalogFields []Field, name string) (Field, bool) {
	for _, f := range catalogFields {
		if f.Name == name {
			return f, true
		}
	}
	return Field{}, false
}
