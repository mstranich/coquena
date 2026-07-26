package runtime

import (
	"context"
	"os"
	"path/filepath"
	"testing"

	"github.com/mstranich/coquena/internal/runtime/llamacpp"
	hostsystem "github.com/mstranich/coquena/internal/system"
)

type fakeReleaseSource struct{ release llamacpp.Release }

var _ Service = (*Manager)(nil)

func (f fakeReleaseSource) Latest(context.Context) (llamacpp.Release, error) { return f.release, nil }
func (f fakeReleaseSource) ByTag(context.Context, string) (llamacpp.Release, error) {
	return f.release, nil
}

func TestNewManagerDoesNotApplyMetadataTimeoutToArchiveTransfers(t *testing.T) {
	manager := NewManager()
	if manager.httpClient == nil {
		t.Fatal("download HTTP client is nil")
	}
	if manager.httpClient.Timeout != 0 {
		t.Fatalf("archive timeout = %v, want caller-controlled context", manager.httpClient.Timeout)
	}
}
func (f fakeReleaseSource) History(context.Context, int) ([]llamacpp.Release, error) {
	return []llamacpp.Release{f.release}, nil
}

func compatibleWindowsReport() (hostsystem.Report, error) {
	return hostsystem.Report{SchemaVersion: 1, OS: hostsystem.OperatingSystem{Family: "windows", NativeArchitecture: "amd64"}, ExecutableArchitecture: "amd64", CPU: hostsystem.CPU{LogicalProcessors: 8}, Graphics: []hostsystem.GraphicsDevice{}, Backends: []hostsystem.BackendCapability{{Type: "cpu", State: hostsystem.Available}, {Type: "cuda", State: hostsystem.Available, Version: "13.3"}, {Type: "vulkan", State: hostsystem.Available}}, Diagnostics: []hostsystem.Diagnostic{}}, nil
}

func serviceRelease() llamacpp.Release {
	asset := func(name string) llamacpp.APIAsset {
		return llamacpp.APIAsset{Name: name, BrowserDownloadURL: "https://example.invalid/" + name, Size: 1, Digest: "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}
	}
	return llamacpp.Release{Tag: "b10068", Assets: []llamacpp.APIAsset{
		asset("llama-b10068-bin-win-cpu-x64.zip"),
		asset("llama-b10068-bin-win-cuda-12.4-x64.zip"), asset("cudart-llama-bin-win-cuda-12.4-x64.zip"),
		asset("llama-b10068-bin-win-cuda-13.3-x64.zip"), asset("cudart-llama-bin-win-cuda-13.3-x64.zip"),
		asset("llama-b10068-bin-win-vulkan-x64.zip"), asset("llama-b10068-bin-ubuntu-vulkan-x64.tar.gz"),
	}}
}

func TestManagerRemoteFiltersToCompatibleNativeHost(t *testing.T) {
	manager := &Manager{releases: fakeReleaseSource{serviceRelease()}, inspect: compatibleWindowsReport}
	records, err := manager.Remote(context.Background(), Query{Release: "latest"})
	if err != nil {
		t.Fatal(err)
	}
	if len(records) != 4 {
		t.Fatalf("records=%+v", records)
	}
	for _, record := range records {
		if record.Compatibility != "compatible" || record.AssetID == "llama-b10068-bin-ubuntu-vulkan-x64.tar.gz" {
			t.Fatalf("record=%+v", record)
		}
	}
}

func TestManagerShorthandAmbiguityAndExactResolution(t *testing.T) {
	manager := &Manager{releases: fakeReleaseSource{serviceRelease()}, inspect: compatibleWindowsReport}
	if _, err := manager.resolveDownload(context.Background(), DownloadRequest{Release: "latest", Selector: "cuda"}); err == nil {
		t.Fatal("expected ambiguity")
	}
	id := "llama-b10068-bin-win-cuda-13.3-x64.zip"
	distribution, err := manager.resolveDownload(context.Background(), DownloadRequest{AssetID: id})
	if err != nil {
		t.Fatal(err)
	}
	if distribution.Primary.Name != id || len(distribution.Dependencies) != 1 {
		t.Fatalf("distribution=%+v", distribution)
	}
}

func TestValidateExactAssetID(t *testing.T) {
	for _, id := range []string{"", "cuda", "https://example.invalid/runtime.zip", "cudart-llama-bin-win-cuda-x64.zip", "llama-runtime.exe"} {
		if ValidateExactAssetID(id) == nil {
			t.Fatalf("expected %q to be rejected", id)
		}
	}
	for _, id := range []string{"llama-b1-bin-win-cpu-x64.zip", "llama-b1-bin-ubuntu-x64.tar.gz"} {
		if err := ValidateExactAssetID(id); err != nil {
			t.Fatalf("expected %q to be accepted: %v", id, err)
		}
	}
}

func TestRuntimeRootUsesApplicationHomeOverride(t *testing.T) {
	home := t.TempDir()
	t.Setenv("COQUENA_HOME", home)
	for _, dir := range []string{"config", "runtimes"} {
		if err := os.MkdirAll(filepath.Join(home, dir), 0755); err != nil {
			t.Fatal(err)
		}
	}
	if err := os.WriteFile(filepath.Join(home, "config", "coquena.json"), []byte(`{"schemaVersion":3}`), 0644); err != nil {
		t.Fatal(err)
	}

	got, err := runtimeRoot()
	if err != nil {
		t.Fatal(err)
	}
	if got != filepath.Join(filepath.Clean(home), "runtimes") {
		t.Fatalf("runtimeRoot() = %q, want %q", got, filepath.Join(filepath.Clean(home), "runtimes"))
	}
}
