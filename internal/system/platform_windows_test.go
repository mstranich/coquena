//go:build windows

package system

import (
	"errors"
	"runtime"
	"testing"

	"golang.org/x/sys/windows"
)

func TestParsePCI(t *testing.T) {
	vendor, device := parsePCI(`pci\ven_10de&dev_2208&subsys_0000`)
	if vendor == nil || *vendor != 0x10de || device == nil || *device != 0x2208 {
		t.Fatalf("parsePCI() = %v, %v", vendor, device)
	}
	missingVendor, missingDevice := parsePCI("not-pci")
	if missingVendor != nil || missingDevice != nil {
		t.Fatalf("parsePCI(non-PCI) = %v, %v", missingVendor, missingDevice)
	}
}

func TestArchitectureFromMachine(t *testing.T) {
	for machine, want := range map[uint16]string{0x8664: "amd64", 0xaa64: "arm64", 0x014c: "386", 0xffff: "machine-0xffff"} {
		if got := architectureFromMachine(machine); got != want {
			t.Fatalf("architectureFromMachine(%x) = %q, want %q", machine, got, want)
		}
	}
}

func TestReadGraphicsProducesStableHardwareRecords(t *testing.T) {
	for _, device := range readGraphics() {
		if device.ID == "" || device.Name == "" || device.Source != "windows-display-class" || device.Type != "hardware" {
			t.Fatalf("invalid graphics device: %+v", device)
		}
	}
}

func TestNormalizeGraphicsHandlesZeroMultipleSoftwareAndPartialRecords(t *testing.T) {
	if got := normalizeGraphics(nil); len(got) != 0 {
		t.Fatalf("normalizeGraphics(nil) = %+v", got)
	}
	got := normalizeGraphics([]GraphicsDevice{
		{ID: "gpu-2", Name: "Second", Type: "hardware", Source: "fixture"},
		{ID: "software", Name: "Software", Type: "software", Source: "fixture"},
		{ID: "", Name: "Unreadable", Type: "hardware", Source: "fixture"},
		{ID: "gpu-1", Name: "First", Type: "hardware", Source: "fixture"},
	})
	if len(got) != 2 || got[0].ID != "gpu-2" || got[1].ID != "gpu-1" {
		t.Fatalf("normalizeGraphics() = %+v", got)
	}
}

func TestWindowsInspectionProducesValidReportOnAnyHost(t *testing.T) {
	report, err := Inspect()
	if err != nil {
		t.Fatal(err)
	}
	if report.OS.Family != "windows" || report.OS.NativeArchitecture == "" || report.ExecutableArchitecture != runtime.GOARCH || report.CPU.LogicalProcessors < 1 {
		t.Fatalf("invalid Windows report: %+v", report)
	}
	states := map[string]State{}
	for _, backend := range report.Backends {
		states[backend.Type] = backend.State
	}
	if states["cpu"] != Available || !validState(states["cuda"]) || !validState(states["vulkan"]) || states["metal"] != Unsupported {
		t.Fatalf("unexpected backend states: %+v", states)
	}
}

func TestCapabilityConstructorsDescribeAbsenceAndUnknown(t *testing.T) {
	absent := unavailableCapability("cuda", "cuda-driver-not-found", "missing")
	unknown := unknownCapability("vulkan", "probe-failed", "failed")
	if absent.State != Unavailable || absent.Diagnostics[0].Code != "cuda-driver-not-found" {
		t.Fatalf("unexpected unavailable capability: %+v", absent)
	}
	if unknown.State != Unknown || unknown.Diagnostics[0].Code != "probe-failed" {
		t.Fatalf("unexpected unknown capability: %+v", unknown)
	}
}

func TestOptionalBackendProbesTreatMissingLibrariesAsNormalAbsence(t *testing.T) {
	original := loadOptionalSystemDLL
	loadOptionalSystemDLL = func(string) (windows.Handle, error) { return 0, errors.New("missing") }
	t.Cleanup(func() { loadOptionalSystemDLL = original })
	if got := (cudaProbe{}).Probe(Report{}); got.State != Unavailable || got.Diagnostics[0].Code != "cuda-driver-not-found" {
		t.Fatalf("CUDA missing-library result = %+v", got)
	}
	if got := (vulkanProbe{}).Probe(Report{}); got.State != Unavailable || got.Diagnostics[0].Code != "vulkan-loader-not-found" {
		t.Fatalf("Vulkan missing-library result = %+v", got)
	}
}
