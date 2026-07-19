package main

import (
	"context"
	"testing"

	"github.com/mstranich/coquena/internal/runtime/llamacpp"
	hostsystem "github.com/mstranich/coquena/internal/system"
)

type fakeReleaseSource struct{ release llamacpp.Release }

func (f fakeReleaseSource) Latest(context.Context) (llamacpp.Release, error) { return f.release, nil }
func (f fakeReleaseSource) ByTag(_ context.Context, _ string) (llamacpp.Release, error) {
	return f.release, nil
}
func (f fakeReleaseSource) History(_ context.Context, _ int) ([]llamacpp.Release, error) {
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
		asset("llama-b10068-bin-win-vulkan-x64.zip"),
		asset("llama-b10068-bin-ubuntu-vulkan-x64.tar.gz"),
	}}
}

func TestRuntimeManagerRemoteFiltersToCompatibleNativeHost(t *testing.T) {
	m := &runtimeManager{releases: fakeReleaseSource{serviceRelease()}, inspect: compatibleWindowsReport}
	records, err := m.Remote(runtimeQuery{Release: "latest"})
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

func TestRuntimeManagerShorthandAmbiguityAndExactResolution(t *testing.T) {
	m := &runtimeManager{releases: fakeReleaseSource{serviceRelease()}, inspect: compatibleWindowsReport}
	if _, err := m.resolveDownload(context.Background(), runtimeDownloadRequest{Release: "latest", Selector: "cuda"}); err == nil {
		t.Fatal("expected ambiguity")
	}
	id := "llama-b10068-bin-win-cuda-13.3-x64.zip"
	d, err := m.resolveDownload(context.Background(), runtimeDownloadRequest{AssetID: id})
	if err != nil {
		t.Fatal(err)
	}
	if d.Primary.Name != id || len(d.Dependencies) != 1 {
		t.Fatalf("distribution=%+v", d)
	}
}
