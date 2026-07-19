package llamacpp

import (
	"encoding/json"
	"os"
	"testing"
)

func fixture(t *testing.T) Release {
	t.Helper()
	b, e := os.ReadFile("testdata/release-b10068.json")
	if e != nil {
		t.Fatal(e)
	}
	var r Release
	if e = json.Unmarshal(b, &r); e != nil {
		t.Fatal(e)
	}
	return r
}

func TestClassifyRepresentativeRelease(t *testing.T) {
	c := ClassifyRelease(fixture(t))
	if len(c.Distributions) != 10 {
		t.Fatalf("got %d distributions: %#v", len(c.Distributions), c.Diagnostics)
	}
	by := map[string]Distribution{}
	for _, d := range c.Distributions {
		by[d.Primary.Name] = d
	}
	cuda := by["llama-b10068-bin-win-cuda-13.3-x64.zip"]
	if cuda.Backend.Version != "13.3" || len(cuda.Dependencies) != 1 || cuda.Dependencies[0].Name != "cudart-llama-bin-win-cuda-13.3-x64.zip" {
		t.Fatalf("bad CUDA: %#v", cuda)
	}
	if d := by["llama-b10068-bin-macos-arm64.zip"]; d.Backend.Name != "metal" || d.Backend.Flavor != "embedded" {
		t.Fatalf("bad implicit Metal: %#v", d)
	}
	if d := by["llama-b10068-bin-win-sycl-fp16-x64.zip"]; d.Backend.Precision != "fp16" {
		t.Fatalf("bad SYCL: %#v", d)
	}
	if d := by["llama-b10068-bin-win-opencl-adreno-arm64.zip"]; d.Backend.Flavor != "adreno" {
		t.Fatalf("bad OpenCL: %#v", d)
	}
	if d := by["llama-b10068-bin-ubuntu-rocm-7.2-x64.tar.gz"]; d.Backend.Version != "7.2" {
		t.Fatalf("bad ROCm: %#v", d)
	}
}

func TestCUDAIncompleteOrAmbiguousDependencyIsExcluded(t *testing.T) {
	r := fixture(t)
	r.Assets = r.Assets[:0]
	r.Assets = append(r.Assets, APIAsset{Name: "llama-b10068-bin-win-cuda-13.3-x64.zip"})
	c := ClassifyRelease(r)
	if len(c.Distributions) != 0 || len(c.Diagnostics) != 1 || c.Diagnostics[0].Code != "cuda-dependency-incomplete" {
		t.Fatalf("%#v", c)
	}
	r.Assets = append(r.Assets, APIAsset{Name: "cudart-llama-bin-win-cuda-13.3-x64.zip"}, APIAsset{Name: "cudart-llama-bin-win-cuda-13.3-x64.zip"})
	c = ClassifyRelease(r)
	if len(c.Distributions) != 0 || c.Diagnostics[0].Code != "cuda-dependency-incomplete" {
		t.Fatalf("%#v", c)
	}
}

func TestUnknownNamesAreNotGuessedBySubstring(t *testing.T) {
	c := ClassifyRelease(Release{Tag: "b1", Assets: []APIAsset{{Name: "totally-cuda-win-x64.zip"}}})
	if len(c.Distributions) != 0 || c.Diagnostics[0].Code != "asset-unknown" {
		t.Fatalf("%#v", c)
	}
}

func FuzzClassifyDoesNotPanicOrInventUnknown(f *testing.F) {
	f.Add("unknown-cuda-x64.zip")
	f.Add("../llama-b1-bin-win-cpu-x64.zip")
	f.Fuzz(func(t *testing.T, name string) {
		c := ClassifyRelease(Release{Tag: "b1", Assets: []APIAsset{{Name: name}}})
		for _, d := range c.Distributions {
			if d.Primary.Name != name {
				t.Fatal("raw name not preserved")
			}
			if err := d.Validate(); err != nil {
				t.Fatalf("invalid invented distribution: %v", err)
			}
		}
	})
}
