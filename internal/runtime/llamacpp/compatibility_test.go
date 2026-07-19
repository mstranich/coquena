package llamacpp

import (
	"testing"

	hostsystem "github.com/mstranich/coquena/internal/system"
)

func report(arch string, backends ...hostsystem.BackendCapability) hostsystem.Report {
	return hostsystem.Report{SchemaVersion: 1, OS: hostsystem.OperatingSystem{Family: "windows", NativeArchitecture: arch}, ExecutableArchitecture: "amd64", CPU: hostsystem.CPU{LogicalProcessors: 8}, Backends: backends}
}
func distribution(backend, version, arch string) Distribution {
	return Distribution{Release: "b1", OS: "windows", Architecture: arch, Target: TargetGPU, Backend: Backend{Name: backend, Version: version}, Primary: ReleaseAsset{Name: "x.zip", Kind: AssetPrimary}}
}

func TestHostUsesNativeNotExecutableArchitecture(t *testing.T) {
	h, e := HostFromReport(report("arm64", hostsystem.BackendCapability{Type: "cpu", State: hostsystem.Available}))
	if e != nil {
		t.Fatal(e)
	}
	c := EvaluateCompatibility(distribution("cpu", "", "amd64"), h)
	if c.State != Incompatible || c.Reason != "native-architecture-mismatch" {
		t.Fatalf("%#v", c)
	}
}

func TestCompatibilityPolicies(t *testing.T) {
	cases := []struct {
		name    string
		backend hostsystem.BackendCapability
		runtime Backend
		want    CompatibilityState
		reason  string
	}{
		{"cpu available", hostsystem.BackendCapability{Type: "cpu", State: hostsystem.Available}, Backend{Name: "cpu"}, Compatible, ""},
		{"vulkan unavailable", hostsystem.BackendCapability{Type: "vulkan", State: hostsystem.Unavailable}, Backend{Name: "vulkan"}, Incompatible, "backend-unavailable"},
		{"vulkan unsupported", hostsystem.BackendCapability{Type: "vulkan", State: hostsystem.Unsupported}, Backend{Name: "vulkan"}, Incompatible, "backend-unsupported"},
		{"cuda unknown", hostsystem.BackendCapability{Type: "cuda", State: hostsystem.Unknown}, Backend{Name: "cuda", Version: "13.3"}, CompatibilityUnknown, "backend-unknown"},
		{"new driver old runtime", hostsystem.BackendCapability{Type: "cuda", State: hostsystem.Available, Version: "13.3"}, Backend{Name: "cuda", Version: "12.4"}, Compatible, ""},
		{"old driver new runtime", hostsystem.BackendCapability{Type: "cuda", State: hostsystem.Available, Version: "12.8"}, Backend{Name: "cuda", Version: "13.3"}, Incompatible, "cuda-driver-too-old:need-13.0"},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			h, e := HostFromReport(report("amd64", tc.backend))
			if e != nil {
				t.Fatal(e)
			}
			d := distribution(tc.runtime.Name, tc.runtime.Version, "amd64")
			c := EvaluateCompatibility(d, h)
			if c.State != tc.want || c.Reason != tc.reason {
				t.Fatalf("got %#v", c)
			}
		})
	}
}

func TestUnprobedWindowsBackendsAreUnknownAndNotDownloadable(t *testing.T) {
	h, _ := HostFromReport(report("amd64", hostsystem.BackendCapability{Type: "cpu", State: hostsystem.Available}))
	for _, backend := range []string{"hip", "sycl", "openvino", "opencl", "rocm"} {
		c := EvaluateCompatibility(distribution(backend, "", "amd64"), h)
		if c.State != CompatibilityUnknown {
			t.Errorf("%s: %#v", backend, c)
		}
	}
	if got := CompatibleOnly([]Distribution{distribution("hip", "", "amd64")}, h); len(got) != 0 {
		t.Fatal("unknown backend became downloadable")
	}
}

func TestCrossPlatformAndArchitectureNeverCompatible(t *testing.T) {
	h, _ := HostFromReport(report("amd64", hostsystem.BackendCapability{Type: "cpu", State: hostsystem.Available}))
	d := distribution("cpu", "", "amd64")
	d.OS = "linux"
	if EvaluateCompatibility(d, h).State != Incompatible {
		t.Fatal("cross platform accepted")
	}
	d.OS = "windows"
	d.Architecture = "arm64"
	if EvaluateCompatibility(d, h).State != Incompatible {
		t.Fatal("cross architecture accepted")
	}
}
