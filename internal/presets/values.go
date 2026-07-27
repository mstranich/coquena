package presets

import (
	"fmt"
	"strings"

	"github.com/mstranich/coquena/internal/fields"
)

// ValidatedValue is the outcome of validating one authored value against the
// field catalog: the canonical field name to store it under, plus any
// advisory diagnostic (never an error) about an unsatisfied parent.
type ValidatedValue struct {
	Field      string
	Diagnostic string
}

// ValidateValue resolves nameOrAlias against the catalog, checks value
// against the field's kind, and reports an advisory diagnostic when the
// field's parent prerequisite is not satisfied in currentValues. It refuses
// an unknown field and a value the kind does not admit.
func ValidateValue(catalog fields.Catalog, nameOrAlias string, value any, currentValues map[string]any) (ValidatedValue, error) {
	field, ok := catalog.Resolve(nameOrAlias)
	if !ok {
		return ValidatedValue{}, fmt.Errorf("field %q is not defined in the catalog", nameOrAlias)
	}
	if err := checkKind(field, value); err != nil {
		return ValidatedValue{}, err
	}
	result := ValidatedValue{Field: field.Name}
	if field.Parent != "" {
		if got, set := currentValues[field.Parent]; !set || got != field.ParentValue {
			result.Diagnostic = fmt.Sprintf("field %q expects parent %q to be set to %v", field.Name, field.Parent, field.ParentValue)
		}
	}
	return result, nil
}

func checkKind(field fields.Field, value any) error {
	switch field.Kind {
	case fields.KindString:
		if _, ok := value.(string); !ok {
			return fmt.Errorf("field %q requires a string value", field.Name)
		}
	case fields.KindInteger:
		if !isInteger(value) {
			return fmt.Errorf("field %q requires an integer value", field.Name)
		}
	case fields.KindBoolean:
		if _, ok := value.(bool); !ok {
			return fmt.Errorf("field %q requires a boolean value", field.Name)
		}
	case fields.KindVoid:
		if b, ok := value.(bool); !ok || !b {
			return fmt.Errorf("field %q is void and admits only the flag value true", field.Name)
		}
	case fields.KindEnum:
		s, ok := value.(string)
		if !ok {
			return fmt.Errorf("field %q requires one of its enum options", field.Name)
		}
		for _, opt := range field.EnumOptions {
			if opt == s {
				return nil
			}
		}
		return fmt.Errorf("field %q does not admit %q; options are %s", field.Name, s, strings.Join(field.EnumOptions, ", "))
	}
	return nil
}

func isInteger(value any) bool {
	switch v := value.(type) {
	case int, int32, int64:
		return true
	case float64:
		return v == float64(int64(v))
	default:
		return false
	}
}
