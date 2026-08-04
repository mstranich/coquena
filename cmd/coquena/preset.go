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
	modelapp "github.com/mstranich/coquena/internal/model"
	"github.com/mstranich/coquena/internal/presets"
)

const presetUsage = "usage: coquena preset define <name> --model <ref> [--allow-missing] [--title <text>] [--json]\n" +
	"       coquena preset rename <old-name> <new-name> [--json]\n" +
	"       coquena preset remove <name> [--json]\n" +
	"       coquena preset list [--json]\n" +
	"       coquena preset show <name> [--json]\n" +
	"       coquena preset set <name> <field> <value> [--json]\n" +
	"       coquena preset set <name> --title <text> [--json]\n" +
	"       coquena preset unset <name> <field> [--json]\n" +
	"       coquena preset bind model <name> <ref> [--allow-missing] [--json]\n" +
	"       coquena preset bind runtime <name> [--provider <p>] [--backend <b>] [--min-release <t>] [--max-release <t>] [--json]\n" +
	"       coquena preset bind runtime <name> --id <runtime-id> [--json]\n" +
	"       coquena preset unbind runtime <name> [--json]\n" +
	"       coquena preset render [--runtime <release>] [--write [<path>]] [--json]"

func newModelManagerForPresets() (*modelapp.Manager, error) {
	models, err := installation.ModelsDirectory()
	if err != nil {
		return nil, err
	}
	return modelapp.NewManager(models), nil
}

var presetModelManagerFactory = newModelManagerForPresets
var presetModelsDirFunc = installation.ModelsDirectory
var presetHomeFunc = installation.Home

func runPreset(args []string, out io.Writer) error {
	jsonOutput, args, err := extractJSON(args)
	if err != nil || len(args) < 1 {
		return errors.New(presetUsage)
	}
	store, err := presetStoreFactory()
	if err != nil {
		return err
	}
	catalogStore, err := fieldStoreFactory()
	if err != nil {
		return err
	}
	switch args[0] {
	case "define":
		return runPresetDefine(args[1:], out, store, jsonOutput)
	case "rename":
		if len(args) != 3 {
			return errors.New(presetUsage)
		}
		doc, err := store.Rename(args[1], args[2])
		if err != nil {
			return err
		}
		p, _ := doc.Find(args[2])
		catalog, _ := catalogStore.Load()
		return writePresetView(out, buildPresetView(catalog, doc.Global, p), jsonOutput)
	case "remove":
		if len(args) != 2 {
			return errors.New(presetUsage)
		}
		if _, err := store.Remove(args[1]); err != nil {
			return err
		}
		_, err := fmt.Fprintf(out, "removed %s\n", args[1])
		return err
	case "list":
		if len(args) != 1 {
			return errors.New(presetUsage)
		}
		return runPresetList(out, store, catalogStore, jsonOutput)
	case "show":
		if len(args) != 2 {
			return errors.New(presetUsage)
		}
		return runPresetShow(out, store, catalogStore, args[1], jsonOutput)
	case "set":
		return runPresetSet(args[1:], out, store, catalogStore, jsonOutput)
	case "unset":
		if len(args) != 3 {
			return errors.New(presetUsage)
		}
		catalog, err := catalogStore.Load()
		if err != nil {
			return err
		}
		if _, err := store.UnsetValue(catalog, args[1], args[2]); err != nil {
			return err
		}
		_, err = fmt.Fprintf(out, "unset %s on %s\n", args[2], args[1])
		return err
	case "bind":
		return runPresetBind(args[1:], out, store, jsonOutput)
	case "unbind":
		if len(args) != 3 || args[1] != "runtime" {
			return errors.New(presetUsage)
		}
		if _, err := store.UnsetRuntime(args[2]); err != nil {
			return err
		}
		_, err = fmt.Fprintf(out, "unbound runtime from %s\n", args[2])
		return err
	case "render":
		return runPresetRender(args[1:], out, store, catalogStore, jsonOutput)
	default:
		return errors.New(presetUsage)
	}
}

func runPresetDefine(args []string, out io.Writer, store *presets.Store, jsonOutput bool) error {
	if len(args) < 1 {
		return errors.New(presetUsage)
	}
	name := args[0]
	var reference, title string
	allowMissing := false
	rest := args[1:]
	for i := 0; i < len(rest); i++ {
		switch rest[i] {
		case "--model":
			if i+1 >= len(rest) {
				return errors.New(presetUsage)
			}
			i++
			reference = rest[i]
		case "--title":
			if i+1 >= len(rest) {
				return errors.New(presetUsage)
			}
			i++
			title = rest[i]
		case "--allow-missing":
			allowMissing = true
		default:
			return errors.New(presetUsage)
		}
	}
	if reference == "" {
		return errors.New(presetUsage)
	}
	manager, err := presetModelManagerFactory()
	if err != nil {
		return err
	}
	binding, err := presets.BindModel(manager, reference, allowMissing)
	if err != nil {
		return err
	}
	if _, err := store.Define(presets.Preset{Name: name, Title: title, Model: &binding}); err != nil {
		return err
	}
	_, err = fmt.Fprintf(out, "defined %s\n", name)
	return err
}

func runPresetSet(args []string, out io.Writer, store *presets.Store, catalogStore fieldStoreCLI, jsonOutput bool) error {
	if len(args) < 1 {
		return errors.New(presetUsage)
	}
	name := args[0]
	rest := args[1:]
	if len(rest) == 2 && rest[0] == "--title" {
		if _, err := store.SetTitle(name, rest[1]); err != nil {
			return err
		}
		_, err := fmt.Fprintf(out, "set title on %s\n", name)
		return err
	}
	if len(rest) != 2 {
		return errors.New(presetUsage)
	}
	field, value := rest[0], parsePresetValue(rest[1])
	catalog, err := catalogStore.Load()
	if err != nil {
		return err
	}
	_, validated, err := store.SetValue(catalog, name, field, value)
	if err != nil {
		return err
	}
	if validated.Diagnostic != "" {
		_, _ = fmt.Fprintf(out, "warning: %s\n", validated.Diagnostic)
	}
	_, err = fmt.Fprintf(out, "set %s on %s\n", validated.Field, name)
	return err
}

// parsePresetValue interprets a CLI value token as bool, integer, or string,
// so a preset field of the corresponding kind validates without extra flags.
func parsePresetValue(raw string) any {
	if raw == "true" {
		return true
	}
	if raw == "false" {
		return false
	}
	if i, err := strconv.ParseInt(raw, 10, 64); err == nil {
		return float64(i)
	}
	return raw
}

func runPresetBind(args []string, out io.Writer, store *presets.Store, jsonOutput bool) error {
	if len(args) < 2 {
		return errors.New(presetUsage)
	}
	switch args[0] {
	case "model":
		name, reference := args[1], args[2]
		allowMissing := false
		for _, a := range args[3:] {
			if a == "--allow-missing" {
				allowMissing = true
				continue
			}
			return errors.New(presetUsage)
		}
		manager, err := presetModelManagerFactory()
		if err != nil {
			return err
		}
		binding, err := presets.BindModel(manager, reference, allowMissing)
		if err != nil {
			return err
		}
		if _, err := store.SetModel(name, binding); err != nil {
			return err
		}
		_, err = fmt.Fprintf(out, "bound model on %s\n", name)
		return err
	case "runtime":
		name := args[1]
		var constraint presets.RuntimeConstraint
		rest := args[2:]
		for i := 0; i < len(rest); i++ {
			switch rest[i] {
			case "--provider":
				i++
				if i >= len(rest) {
					return errors.New(presetUsage)
				}
				constraint.Provider = rest[i]
			case "--backend":
				i++
				if i >= len(rest) {
					return errors.New(presetUsage)
				}
				constraint.Backend = rest[i]
			case "--min-release":
				i++
				if i >= len(rest) {
					return errors.New(presetUsage)
				}
				constraint.MinRelease = rest[i]
			case "--max-release":
				i++
				if i >= len(rest) {
					return errors.New(presetUsage)
				}
				constraint.MaxRelease = rest[i]
			case "--id":
				i++
				if i >= len(rest) {
					return errors.New(presetUsage)
				}
				constraint.RuntimeID = rest[i]
			default:
				return errors.New(presetUsage)
			}
		}
		if _, err := store.SetRuntime(name, constraint); err != nil {
			return err
		}
		_, err := fmt.Fprintf(out, "bound runtime on %s\n", name)
		return err
	default:
		return errors.New(presetUsage)
	}
}

type presetValueView struct {
	Field  string `json:"field"`
	Value  any    `json:"value"`
	Source string `json:"source"`
}

type presetJSONView struct {
	Name       string                 `json:"name"`
	Kind       string                 `json:"kind"`
	Title      string                 `json:"title,omitempty"`
	Model      *presets.ModelBinding  `json:"model,omitempty"`
	ModelState presets.BindingState   `json:"modelState,omitempty"`
	Runtime    *presetRuntimeJSONView `json:"runtime,omitempty"`
	Values     []presetValueView      `json:"values"`
}

type presetRuntimeJSONView struct {
	Declared *presets.RuntimeConstraint `json:"declared,omitempty"`
	Computed presets.ComputedConstraint `json:"computed"`
}

func buildPresetView(catalog fields.Catalog, global, p presets.Preset) presetJSONView {
	kind := "model"
	if p.IsGlobal() {
		kind = "global"
	}
	effective := presets.Effective(global, p)
	values := make([]presetValueView, 0, len(effective))
	for _, v := range effective {
		values = append(values, presetValueView{Field: v.Field, Value: v.Value, Source: string(v.Source)})
	}
	view := presetJSONView{Name: p.Name, Kind: kind, Title: p.Title, Model: p.Model, Values: values}
	if !p.IsGlobal() {
		provider := fields.ProviderOfficial
		if p.Runtime != nil && p.Runtime.Provider != "" {
			provider = fields.Provider(p.Runtime.Provider)
		}
		computed := presets.ComputeConstraint(catalog, provider, effective)
		view.Runtime = &presetRuntimeJSONView{Declared: p.Runtime, Computed: computed}
	}
	return view
}

func evaluatePresetView(view presetJSONView, resolver presets.ModelResolver) presetJSONView {
	if view.Model != nil {
		view.ModelState = presets.EvaluateBinding(resolver, *view.Model)
	}
	return view
}

func writePresetView(out io.Writer, view presetJSONView, jsonOutput bool) error {
	if jsonOutput {
		return json.NewEncoder(out).Encode(struct {
			SchemaVersion int            `json:"schemaVersion"`
			Result        presetJSONView `json:"result"`
		}{1, view})
	}
	if _, err := fmt.Fprintf(out, "PRESET  %s", view.Name); err != nil {
		return err
	}
	if view.Title != "" {
		if _, err := fmt.Fprintf(out, "  (%s)", view.Title); err != nil {
			return err
		}
	}
	if _, err := fmt.Fprintln(out); err != nil {
		return err
	}
	if view.Model != nil {
		state := "not installed"
		if view.Model.Resolved != nil {
			state = presets.ResolvedReference(*view.Model)
		}
		if view.ModelState != "" {
			state += " (" + string(view.ModelState) + ")"
		}
		if _, err := fmt.Fprintf(out, "MODEL   %s -> %s\n", view.Model.Requested.Raw, state); err != nil {
			return err
		}
	}
	if view.Runtime != nil {
		if _, err := fmt.Fprintf(out, "RUNTIME declared=%s computed=%s\n", formatRuntimeConstraint(view.Runtime.Declared), formatComputedConstraint(view.Runtime.Computed)); err != nil {
			return err
		}
	}
	writer := tabwriter.NewWriter(out, 0, 4, 2, ' ', 0)
	_, _ = fmt.Fprintln(writer, "FIELD\tVALUE\tSOURCE")
	for _, v := range view.Values {
		_, _ = fmt.Fprintf(writer, "%s\t%v\t%s\n", v.Field, v.Value, v.Source)
	}
	return writer.Flush()
}

func formatRuntimeConstraint(c *presets.RuntimeConstraint) string {
	if c == nil {
		return "none"
	}
	if c.RuntimeID != "" {
		return "id=" + c.RuntimeID
	}
	var parts []string
	if c.Provider != "" {
		parts = append(parts, "provider="+c.Provider)
	}
	if c.Backend != "" {
		parts = append(parts, "backend="+c.Backend)
	}
	if c.MinRelease != "" {
		parts = append(parts, "min="+c.MinRelease)
	}
	if c.MaxRelease != "" {
		parts = append(parts, "max="+c.MaxRelease)
	}
	if len(parts) == 0 {
		return "none"
	}
	return strings.Join(parts, " ")
}

func formatComputedConstraint(c presets.ComputedConstraint) string {
	var parts []string
	if c.MinRelease != nil {
		parts = append(parts, fmt.Sprintf("min=%s (from %s)", c.MinRelease.Release, c.MinRelease.Field))
	}
	if c.MaxRelease != nil {
		parts = append(parts, fmt.Sprintf("max=%s (from %s)", c.MaxRelease.Release, c.MaxRelease.Field))
	}
	if len(parts) == 0 {
		return "none"
	}
	return strings.Join(parts, " ")
}

func runPresetList(out io.Writer, store *presets.Store, catalogStore fieldStoreCLI, jsonOutput bool) error {
	doc, err := store.Load()
	if err != nil {
		return err
	}
	catalog, _ := catalogStore.Load()
	manager, managerErr := presetModelManagerFactory()
	if managerErr != nil {
		return managerErr
	}
	if jsonOutput {
		global := buildPresetView(catalog, doc.Global, doc.Global)
		items := make([]presetJSONView, 0, len(doc.Presets))
		for _, p := range doc.Presets {
			items = append(items, evaluatePresetView(buildPresetView(catalog, doc.Global, p), manager))
		}
		return json.NewEncoder(out).Encode(struct {
			SchemaVersion int              `json:"schemaVersion"`
			Global        presetJSONView   `json:"global"`
			Presets       []presetJSONView `json:"presets"`
		}{1, global, items})
	}
	writer := tabwriter.NewWriter(out, 0, 4, 2, ' ', 0)
	_, _ = fmt.Fprintln(writer, "NAME\tKIND\tTITLE")
	_, _ = fmt.Fprintf(writer, "%s\t%s\t%s\n", presets.GlobalName, "global", "")
	for _, p := range doc.Presets {
		_, _ = fmt.Fprintf(writer, "%s\t%s\t%s\n", p.Name, "model", p.Title)
	}
	return writer.Flush()
}

func runPresetShow(out io.Writer, store *presets.Store, catalogStore fieldStoreCLI, name string, jsonOutput bool) error {
	doc, err := store.Load()
	if err != nil {
		return err
	}
	p, ok := doc.Find(name)
	if !ok {
		return fmt.Errorf("preset %q not found", name)
	}
	catalog, _ := catalogStore.Load()
	manager, err := presetModelManagerFactory()
	if err != nil {
		return err
	}
	return writePresetView(out, evaluatePresetView(buildPresetView(catalog, doc.Global, p), manager), jsonOutput)
}

func runPresetRender(args []string, out io.Writer, store *presets.Store, catalogStore fieldStoreCLI, jsonOutput bool) error {
	release := ""
	write := false
	var writePath string
	for i := 0; i < len(args); i++ {
		switch args[i] {
		case "--runtime":
			i++
			if i >= len(args) {
				return errors.New(presetUsage)
			}
			release = args[i]
		case "--write":
			write = true
			if i+1 < len(args) && !strings.HasPrefix(args[i+1], "--") {
				i++
				writePath = args[i]
			}
		default:
			return errors.New(presetUsage)
		}
	}
	doc, err := store.Load()
	if err != nil {
		return err
	}
	catalog, err := catalogStore.Load()
	if err != nil {
		return err
	}
	models, err := presetModelsDirFunc()
	if err != nil {
		return err
	}
	result := presets.Render(doc, catalog, fields.ProviderOfficial, release, models)
	text := presets.Format(result)
	for _, diagnostic := range result.Diagnostics {
		fmt.Fprintf(os.Stderr, "warning: %s\n", diagnostic)
	}
	if !write {
		_, err := io.WriteString(out, text)
		return err
	}
	if writePath == "" {
		home, err := presetHomeFunc()
		if err != nil {
			return err
		}
		writePath = presets.DefaultRenderPath(home, release)
	}
	if err := presets.WriteRendering(writePath, text); err != nil {
		return err
	}
	_, err = fmt.Fprintf(out, "wrote %s\n", writePath)
	return err
}
