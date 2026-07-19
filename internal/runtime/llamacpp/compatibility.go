package llamacpp

import (
	"fmt"
	"strconv"
	"strings"

	hostsystem "github.com/mstranich/coquena/internal/system"
)

type CompatibilityState string

const (
	Compatible           CompatibilityState = "compatible"
	Incompatible         CompatibilityState = "incompatible"
	CompatibilityUnknown CompatibilityState = "unknown"
)

type Compatibility struct {
	State    CompatibilityState `json:"state"`
	Evidence []string           `json:"evidence"`
	Reason   string             `json:"reason,omitempty"`
}

type Host struct {
	OS                 string
	NativeArchitecture string
	Backends           map[string]hostsystem.BackendCapability
}

func HostFromReport(r hostsystem.Report) (Host, error) {
	if err := r.Normalize(); err != nil {
		return Host{}, fmt.Errorf("normalize system report: %w", err)
	}
	if r.OS.NativeArchitecture == "" {
		return Host{}, fmt.Errorf("system report does not establish native architecture")
	}
	h := Host{OS: normalizeOS(r.OS.Family), NativeArchitecture: normalizeArch(r.OS.NativeArchitecture), Backends: map[string]hostsystem.BackendCapability{}}
	for _, b := range r.Backends {
		existing, ok := h.Backends[b.Type]
		if !ok || stateRank(b.State) > stateRank(existing.State) {
			h.Backends[b.Type] = b
		}
	}
	return h, nil
}
func stateRank(s hostsystem.State) int {
	switch s {
	case hostsystem.Available:
		return 4
	case hostsystem.Unknown:
		return 3
	case hostsystem.Unavailable:
		return 2
	case hostsystem.Unsupported:
		return 1
	}
	return 0
}
func normalizeOS(s string) string {
	switch strings.ToLower(s) {
	case "win", "windows":
		return "windows"
	case "macos", "darwin":
		return "darwin"
	case "ubuntu", "linux":
		return "linux"
	}
	return strings.ToLower(s)
}
func normalizeArch(s string) string {
	switch strings.ToLower(s) {
	case "x64", "x86_64", "amd64":
		return "amd64"
	case "aarch64", "arm64":
		return "arm64"
	}
	return strings.ToLower(s)
}

func EvaluateCompatibility(d Distribution, h Host) Compatibility {
	ev := []string{"host-os:" + h.OS, "host-native-architecture:" + h.NativeArchitecture, "runtime-os:" + d.OS, "runtime-architecture:" + d.Architecture}
	if normalizeOS(d.OS) != normalizeOS(h.OS) {
		return Compatibility{State: Incompatible, Evidence: ev, Reason: "operating-system-mismatch"}
	}
	if normalizeArch(d.Architecture) != normalizeArch(h.NativeArchitecture) {
		return Compatibility{State: Incompatible, Evidence: ev, Reason: "native-architecture-mismatch"}
	}
	b, ok := h.Backends[d.Backend.Name]
	if !ok {
		return Compatibility{State: CompatibilityUnknown, Evidence: append(ev, "backend-unprobed:"+d.Backend.Name), Reason: "backend-not-evaluated"}
	}
	ev = append(ev, "backend-state:"+string(b.State))
	if b.Version != "" {
		ev = append(ev, "backend-version:"+b.Version)
	}
	switch b.State {
	case hostsystem.Unavailable, hostsystem.Unsupported:
		return Compatibility{State: Incompatible, Evidence: ev, Reason: "backend-" + string(b.State)}
	case hostsystem.Unknown:
		return Compatibility{State: CompatibilityUnknown, Evidence: ev, Reason: "backend-unknown"}
	case hostsystem.Available:
	default:
		return Compatibility{State: CompatibilityUnknown, Evidence: ev, Reason: "invalid-backend-state"}
	}
	if d.Backend.Name == "cuda" {
		ok, reason := cudaCompatible(d.Backend.Version, b.Version)
		if !ok {
			return Compatibility{State: Incompatible, Evidence: ev, Reason: reason}
		}
	}
	return Compatibility{State: Compatible, Evidence: ev}
}

// Minimum CUDA driver API capability by official toolkit major. This is intentionally
// data-driven: a newer driver may run an older toolkit, while equality is not required.
var cudaMinimumDriver = map[int][2]int{12: {12, 0}, 13: {13, 0}}

func cudaCompatible(toolkit, driver string) (bool, string) {
	tMaj, _, ok := parseVersion(toolkit)
	if !ok {
		return false, "cuda-toolkit-version-unknown"
	}
	minimum, known := cudaMinimumDriver[tMaj]
	if !known {
		return false, "cuda-toolkit-family-unsupported"
	}
	dMaj, dMin, ok := parseVersion(driver)
	if !ok {
		return false, "cuda-driver-version-unknown"
	}
	if dMaj < minimum[0] || (dMaj == minimum[0] && dMin < minimum[1]) {
		return false, fmt.Sprintf("cuda-driver-too-old:need-%d.%d", minimum[0], minimum[1])
	}
	return true, ""
}
func parseVersion(s string) (int, int, bool) {
	p := strings.Split(s, ".")
	if len(p) < 1 || len(p) > 3 {
		return 0, 0, false
	}
	a, e := strconv.Atoi(p[0])
	if e != nil {
		return 0, 0, false
	}
	b := 0
	if len(p) > 1 {
		b, e = strconv.Atoi(p[1])
		if e != nil {
			return 0, 0, false
		}
	}
	return a, b, true
}

type CompatibleDistribution struct {
	Distribution  Distribution  `json:"distribution"`
	Compatibility Compatibility `json:"compatibility"`
}

func CompatibleOnly(ds []Distribution, h Host) []CompatibleDistribution {
	out := []CompatibleDistribution{}
	for _, d := range ds {
		c := EvaluateCompatibility(d, h)
		if c.State == Compatible {
			out = append(out, CompatibleDistribution{d, c})
		}
	}
	return out
}
