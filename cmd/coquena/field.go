package main

import (
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"strconv"
	"strings"
	"text/tabwriter"

	"github.com/mstranich/coquena/internal/fields"
	"github.com/mstranich/coquena/internal/installation"
)

const fieldUsage = "usage: coquena field define <kind> <name> [--default <value>] [--options <value>[,<value>...]] [--parent <name>] [--parent-value <value>] [--short <alias>] [--env <alias>] [--provider <name>] [--introduced-in <release>] [--removed-in <release>] [--overwrite] [--json]\n" +
	"       coquena field remove <name> [--json]\n" +
	"       coquena field list [--json]\n" +
	"       coquena field show <name> [--json]"

// fieldStoreCLI is the persistence boundary the field commands depend on.
type fieldStoreCLI interface {
	Load() (fields.Catalog, error)
	Define(fields.Field) (fields.Catalog, error)
	Upsert(fields.Field) (fields.Catalog, error)
	Remove(string) (fields.Catalog, error)
}

var fieldStoreFactory = func() (fieldStoreCLI, error) {
	home, err := installation.Home()
	if err != nil {
		return nil, err
	}
	return fields.NewStore(home), nil
}

// fieldConfirmOverwrite asks whether to replace an existing field on an
// interactive terminal. It returns false without prompting when stdin is not
// a terminal, so a replayed script never blocks on a question nobody can answer.
var fieldConfirmOverwrite = func(name string) bool {
	info, err := os.Stdin.Stat()
	if err != nil || info.Mode()&os.ModeCharDevice == 0 {
		return false
	}
	fmt.Fprintf(os.Stderr, "field %q already exists; overwrite? [y/N] ", name)
	var response string
	_, _ = fmt.Fscanln(os.Stdin, &response)
	response = strings.ToLower(strings.TrimSpace(response))
	return response == "y" || response == "yes"
}

func runField(args []string, out io.Writer) error {
	jsonOutput, args, err := extractJSON(args)
	if err != nil || len(args) < 1 {
		return errors.New(fieldUsage)
	}
	store, storeErr := fieldStoreFactory()
	if storeErr != nil {
		return storeErr
	}
	switch args[0] {
	case "define":
		return runFieldDefine(args[1:], out, store, jsonOutput)
	case "remove":
		return runFieldRemove(args[1:], out, store, jsonOutput)
	case "list":
		if len(args) != 1 {
			return errors.New(fieldUsage)
		}
		catalog, e := store.Load()
		if e != nil {
			return e
		}
		return writeFieldCatalog(out, catalog, jsonOutput)
	case "show":
		if len(args) != 2 {
			return errors.New(fieldUsage)
		}
		catalog, e := store.Load()
		if e != nil {
			return e
		}
		field, ok := catalog.Resolve(args[1])
		if !ok {
			return fmt.Errorf("field %q not found", args[1])
		}
		return writeField(out, field, jsonOutput)
	default:
		return errors.New(fieldUsage)
	}
}

func runFieldDefine(args []string, out io.Writer, store fieldStoreCLI, jsonOutput bool) error {
	if len(args) < 2 {
		return errors.New(fieldUsage)
	}
	kind := fields.Kind(args[0])
	name := args[1]
	f := fields.Field{Name: name, Kind: kind}
	overwrite := false
	rest := args[2:]
	for i := 0; i < len(rest); i++ {
		switch rest[i] {
		case "--overwrite":
			overwrite = true
		case "--default":
			if i+1 >= len(rest) {
				return errors.New(fieldUsage)
			}
			i++
			value, parseErr := parseFieldValue(kind, rest[i])
			if parseErr != nil {
				return parseErr
			}
			f.Default = value
		case "--options":
			if i+1 >= len(rest) {
				return errors.New(fieldUsage)
			}
			i++
			for _, option := range strings.Split(rest[i], ",") {
				option = strings.TrimSpace(option)
				if option != "" {
					f.EnumOptions = append(f.EnumOptions, option)
				}
			}
		case "--parent":
			if i+1 >= len(rest) {
				return errors.New(fieldUsage)
			}
			i++
			f.Parent = rest[i]
		case "--parent-value":
			if i+1 >= len(rest) {
				return errors.New(fieldUsage)
			}
			i++
			f.ParentValue = rest[i]
		case "--short":
			if i+1 >= len(rest) {
				return errors.New(fieldUsage)
			}
			i++
			f.ShortArgument = rest[i]
		case "--env":
			if i+1 >= len(rest) {
				return errors.New(fieldUsage)
			}
			i++
			f.EnvVar = rest[i]
		case "--provider":
			if i+1 >= len(rest) {
				return errors.New(fieldUsage)
			}
			i++
			ensureAvailability(&f).Provider = fields.Provider(rest[i])
		case "--introduced-in":
			if i+1 >= len(rest) {
				return errors.New(fieldUsage)
			}
			i++
			ensureAvailability(&f).IntroducedIn = rest[i]
		case "--removed-in":
			if i+1 >= len(rest) {
				return errors.New(fieldUsage)
			}
			i++
			ensureAvailability(&f).RemovedIn = rest[i]
		default:
			return errors.New(fieldUsage)
		}
	}

	catalog, loadErr := store.Load()
	if loadErr != nil {
		return loadErr
	}
	_, exists := catalog.Find(name)
	if exists && !overwrite {
		if !fieldConfirmOverwrite(name) {
			return fmt.Errorf("field %q already exists; use --overwrite to replace it", name)
		}
	}

	var result fields.Catalog
	var defineErr error
	if exists {
		result, defineErr = store.Upsert(f)
	} else {
		result, defineErr = store.Define(f)
	}
	if defineErr != nil {
		return defineErr
	}
	defined, _ := result.Find(name)
	return writeField(out, defined, jsonOutput)
}

func ensureAvailability(f *fields.Field) *fields.Availability {
	if f.Availability == nil {
		f.Availability = &fields.Availability{}
	}
	return f.Availability
}

func parseFieldValue(kind fields.Kind, raw string) (any, error) {
	switch kind {
	case fields.KindInteger:
		v, err := strconv.Atoi(raw)
		if err != nil {
			return nil, fmt.Errorf("invalid integer default %q", raw)
		}
		return v, nil
	case fields.KindBoolean:
		v, err := strconv.ParseBool(raw)
		if err != nil {
			return nil, fmt.Errorf("invalid boolean default %q", raw)
		}
		return v, nil
	case fields.KindVoid:
		return nil, fmt.Errorf("void field does not accept a default value")
	default:
		return raw, nil
	}
}

func runFieldRemove(args []string, out io.Writer, store fieldStoreCLI, jsonOutput bool) error {
	if len(args) != 1 {
		return errors.New(fieldUsage)
	}
	catalog, err := store.Remove(args[0])
	if err != nil {
		return err
	}
	return writeFieldCatalog(out, catalog, jsonOutput)
}

func writeField(out io.Writer, f fields.Field, jsonOutput bool) error {
	if jsonOutput {
		return json.NewEncoder(out).Encode(struct {
			SchemaVersion int          `json:"schemaVersion"`
			Result        fields.Field `json:"result"`
		}{1, f})
	}
	_, err := fmt.Fprintf(out, "%s\t%s\t%s\t%s\t%s\t%s\n", f.Name, f.Kind, defaultLabel(f), parentLabel(f), optionsLabel(f), availabilityLabel(f))
	return err
}

func writeFieldCatalog(out io.Writer, catalog fields.Catalog, jsonOutput bool) error {
	if jsonOutput {
		return json.NewEncoder(out).Encode(struct {
			SchemaVersion int            `json:"schemaVersion"`
			Result        fields.Catalog `json:"result"`
		}{1, catalog})
	}
	tw := tabwriter.NewWriter(out, 0, 0, 2, ' ', 0)
	fmt.Fprintln(tw, "NAME\tKIND\tDEFAULT\tPARENT\tOPTIONS\tAVAILABILITY")
	for _, f := range catalog.Fields {
		fmt.Fprintf(tw, "%s\t%s\t%s\t%s\t%s\t%s\n", f.Name, f.Kind, defaultLabel(f), parentLabel(f), optionsLabel(f), availabilityLabel(f))
	}
	return tw.Flush()
}

func defaultLabel(f fields.Field) string {
	if f.Default == nil {
		return "-"
	}
	return fmt.Sprint(f.Default)
}

func parentLabel(f fields.Field) string {
	if f.Parent == "" {
		return "-"
	}
	if f.ParentValue != nil {
		return fmt.Sprintf("%s=%v", f.Parent, f.ParentValue)
	}
	return f.Parent
}

func optionsLabel(f fields.Field) string {
	if f.Kind != fields.KindEnum || len(f.EnumOptions) == 0 {
		return "-"
	}
	return strings.Join(f.EnumOptions, ",")
}

func availabilityLabel(f fields.Field) string {
	if f.Availability == nil {
		return "unknown"
	}
	label := string(f.Availability.Provider)
	if f.Availability.IntroducedIn != "" {
		label += " >=" + f.Availability.IntroducedIn
	}
	if f.Availability.RemovedIn != "" {
		label += " <" + f.Availability.RemovedIn
	}
	return label
}
