// Package system inspects host capabilities without changing host state.
package system

import (
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"runtime"
	"slices"
	"strings"
)

const SchemaVersion = 1

type State string

const (
	Available   State = "available"
	Unavailable State = "unavailable"
	Unknown     State = "unknown"
	Unsupported State = "unsupported"
)

type OperatingSystem struct {
	Family             string `json:"family"`
	Name               string `json:"name,omitempty"`
	Version            string `json:"version,omitempty"`
	NativeArchitecture string `json:"nativeArchitecture,omitempty"`
}

type CPU struct {
	Vendor            string   `json:"vendor,omitempty"`
	Model             string   `json:"model,omitempty"`
	LogicalProcessors int      `json:"logicalProcessors"`
	Features          []string `json:"features,omitempty"`
}

type Memory struct {
	TotalBytes *uint64 `json:"totalBytes,omitempty"`
}

type GraphicsDevice struct {
	ID          string  `json:"id"`
	Name        string  `json:"name"`
	Vendor      string  `json:"vendor,omitempty"`
	VendorID    *uint32 `json:"vendorId,omitempty"`
	DeviceID    *uint32 `json:"deviceId,omitempty"`
	MemoryBytes *uint64 `json:"memoryBytes,omitempty"`
	Type        string  `json:"type,omitempty"`
	Source      string  `json:"source"`
}

type Evidence struct {
	Code     string `json:"code"`
	Value    string `json:"value,omitempty"`
	DeviceID string `json:"deviceId,omitempty"`
}

type Diagnostic struct {
	Code     string `json:"code"`
	Message  string `json:"message"`
	Backend  string `json:"backend,omitempty"`
	Severity string `json:"severity,omitempty"`
}

type BackendCapability struct {
	Type        string       `json:"type"`
	State       State        `json:"state"`
	DeviceID    string       `json:"deviceId,omitempty"`
	Version     string       `json:"version,omitempty"`
	Evidence    []Evidence   `json:"evidence,omitempty"`
	Diagnostics []Diagnostic `json:"diagnostics,omitempty"`
}

type Report struct {
	SchemaVersion          int                 `json:"schemaVersion"`
	OS                     OperatingSystem     `json:"os"`
	ExecutableArchitecture string              `json:"executableArchitecture"`
	CPU                    CPU                 `json:"cpu"`
	Memory                 Memory              `json:"memory"`
	Graphics               []GraphicsDevice    `json:"graphics"`
	Backends               []BackendCapability `json:"backends"`
	Diagnostics            []Diagnostic        `json:"diagnostics"`
}

type InventoryDetector interface {
	Detect(*Report) []Diagnostic
}

type BackendProbe interface {
	Probe(Report) BackendCapability
}

func validState(state State) bool {
	return state == Available || state == Unavailable || state == Unknown || state == Unsupported
}

func (r *Report) Normalize() error {
	if r.SchemaVersion == 0 {
		r.SchemaVersion = SchemaVersion
	}
	if r.SchemaVersion != SchemaVersion {
		return fmt.Errorf("unsupported report schemaVersion %d", r.SchemaVersion)
	}
	if r.OS.Family == "" || r.ExecutableArchitecture == "" || r.CPU.LogicalProcessors < 1 {
		return errors.New("report is missing required host facts")
	}
	for _, capability := range r.Backends {
		if capability.Type == "" || !validState(capability.State) {
			return fmt.Errorf("backend %q has invalid state %q", capability.Type, capability.State)
		}
	}
	slices.SortFunc(r.Graphics, func(a, b GraphicsDevice) int { return strings.Compare(a.ID, b.ID) })
	slices.SortFunc(r.Backends, func(a, b BackendCapability) int {
		if c := strings.Compare(a.Type, b.Type); c != 0 {
			return c
		}
		return strings.Compare(a.DeviceID, b.DeviceID)
	})
	slices.SortFunc(r.Diagnostics, func(a, b Diagnostic) int {
		if c := strings.Compare(a.Code, b.Code); c != 0 {
			return c
		}
		return strings.Compare(a.Message, b.Message)
	})
	return nil
}

func inspectWith(inventory InventoryDetector, probes ...BackendProbe) (Report, error) {
	report := Report{
		SchemaVersion:          SchemaVersion,
		OS:                     OperatingSystem{Family: runtime.GOOS},
		ExecutableArchitecture: runtime.GOARCH,
		CPU:                    CPU{LogicalProcessors: runtime.NumCPU()},
		Graphics:               []GraphicsDevice{}, Backends: []BackendCapability{}, Diagnostics: []Diagnostic{},
	}
	if inventory != nil {
		report.Diagnostics = append(report.Diagnostics, inventory.Detect(&report)...)
	}
	report.Backends = append(report.Backends, BackendCapability{Type: "cpu", State: Available, Evidence: []Evidence{{Code: "logical-processors", Value: fmt.Sprint(report.CPU.LogicalProcessors)}}})
	for _, probe := range probes {
		report.Backends = append(report.Backends, probe.Probe(report))
	}
	if err := report.Normalize(); err != nil {
		return Report{}, err
	}
	return report, nil
}

func Inspect() (Report, error) { return inspectPlatform() }

func WriteJSON(w io.Writer, report Report) error {
	if err := report.Normalize(); err != nil {
		return err
	}
	encoder := json.NewEncoder(w)
	encoder.SetEscapeHTML(false)
	return encoder.Encode(report)
}

func WriteText(w io.Writer, report Report) error {
	if err := report.Normalize(); err != nil {
		return err
	}
	name := report.OS.Name
	if name == "" {
		name = report.OS.Family
	}
	if report.OS.Version != "" {
		name += " " + report.OS.Version
	}
	if _, err := fmt.Fprintf(w, "System\n  %s (%s; executable %s)\n\nCPU\n  %s\n  %d logical processors\n", name, valueOrUnknown(report.OS.NativeArchitecture), report.ExecutableArchitecture, valueOrUnknown(report.CPU.Model), report.CPU.LogicalProcessors); err != nil {
		return err
	}
	if report.Memory.TotalBytes != nil {
		if _, err := fmt.Fprintf(w, "\nMemory\n  %.1f GiB\n", float64(*report.Memory.TotalBytes)/(1<<30)); err != nil {
			return err
		}
	}
	if _, err := fmt.Fprintln(w, "\nGraphics"); err != nil {
		return err
	}
	if len(report.Graphics) == 0 {
		if _, err := fmt.Fprintln(w, "  None detected"); err != nil {
			return err
		}
	}
	for _, device := range report.Graphics {
		if _, err := fmt.Fprintf(w, "  %s", device.Name); err != nil {
			return err
		}
		if device.MemoryBytes != nil {
			if _, err := fmt.Fprintf(w, " (%.1f GiB)", float64(*device.MemoryBytes)/(1<<30)); err != nil {
				return err
			}
		}
		if _, err := fmt.Fprintln(w); err != nil {
			return err
		}
	}
	if _, err := fmt.Fprintln(w, "\nBackends"); err != nil {
		return err
	}
	for _, capability := range report.Backends {
		label := capability.Type
		if capability.Version != "" {
			label += " " + capability.Version
		}
		if _, err := fmt.Fprintf(w, "  %-14s %s\n", label, capability.State); err != nil {
			return err
		}
		for _, diagnostic := range capability.Diagnostics {
			if _, err := fmt.Fprintf(w, "    %s\n", diagnostic.Message); err != nil {
				return err
			}
		}
	}
	return nil
}

func valueOrUnknown(value string) string {
	if value == "" {
		return "unknown"
	}
	return value
}
