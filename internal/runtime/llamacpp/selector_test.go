package llamacpp

import (
	"errors"
	"reflect"
	"testing"
)

func TestAssetIDValidationAndEncoding(t *testing.T) {
	valid := []string{"llama-b10068-bin-win-cuda-13.3-x64.zip", "a b.zip"}
	seen := map[string]bool{}
	for _, id := range valid {
		e, err := EncodeAssetID(id)
		if err != nil {
			t.Fatal(err)
		}
		if seen[e] {
			t.Fatal("collision")
		}
		seen[e] = true
		got, err := DecodeAssetID(e)
		if err != nil || got != id {
			t.Fatalf("round trip %q %v", got, err)
		}
	}
	for _, id := range []string{"", ".", "..", "../x", "a/b", "a\\b", " C.zip ", "C:x.zip", string([]byte{'x', 0})} {
		if ValidateAssetID(id) == nil {
			t.Errorf("accepted %q", id)
		}
	}
}

func TestParseSelectors(t *testing.T) {
	minor := 3
	cases := map[string]BackendSelector{"cpu": {Name: "cpu"}, "vulkan": {Name: "vulkan"}, "cuda": {Name: "cuda"}, "cuda@13": {Name: "cuda", VersionMajor: 13}, "cuda@13.3": {Name: "cuda", VersionMajor: 13, VersionMinor: &minor}}
	for in, want := range cases {
		got, e := ParseBackendSelector(in)
		if e != nil || !reflect.DeepEqual(got, want) {
			t.Errorf("%s: %#v %v", in, got, e)
		}
	}
	for _, in := range []string{"CUDA@", "cuda@0", "metal", "cuda@13.x"} {
		if _, e := ParseBackendSelector(in); e == nil {
			t.Errorf("accepted %s", in)
		}
	}
	for _, in := range []string{"latest", "b1", "b10068"} {
		if _, e := ParseReleaseSelector(in); e != nil {
			t.Error(e)
		}
	}
	if _, e := ParseReleaseSelector("v1"); e == nil {
		t.Fatal("accepted v1")
	}
}

func TestResolveZeroOneManyAndOrdering(t *testing.T) {
	ds := ClassifyRelease(fixture(t)).Distributions
	b, _ := ParseBackendSelector("cuda")
	_, err := Resolve(ds, Selection{Release: ReleaseSelector{Tag: "b10068"}, Backend: &b})
	var re *ResolutionError
	if !errors.As(err, &re) || re.Kind != "ambiguous" || len(re.Candidates) != 2 {
		t.Fatalf("%v", err)
	}
	b, _ = ParseBackendSelector("cuda@13")
	d, err := Resolve(ds, Selection{Release: ReleaseSelector{Tag: "b10068"}, Backend: &b})
	if err != nil || d.Backend.Version != "13.3" {
		t.Fatalf("%#v %v", d, err)
	}
	b, _ = ParseBackendSelector("cuda@11")
	_, err = Resolve(ds, Selection{Release: ReleaseSelector{Tag: "b10068"}, Backend: &b})
	if !errors.As(err, &re) || re.Kind != "not-found" {
		t.Fatalf("%v", err)
	}
}

func TestResolveExactNeverMatchesDependencyOrPartial(t *testing.T) {
	ds := ClassifyRelease(fixture(t)).Distributions
	if _, e := ResolveExact(ds, "llama-b10068-bin-win-cuda-13.3-x64.zip"); e != nil {
		t.Fatal(e)
	}
	for _, id := range []string{"cudart-llama-bin-win-cuda-13.3-x64.zip", "cuda-13.3", "../x"} {
		if _, e := ResolveExact(ds, id); e == nil {
			t.Errorf("resolved %q", id)
		}
	}
}

func FuzzSelectors(f *testing.F) {
	f.Add("cuda@13.3")
	f.Add("../../x")
	f.Fuzz(func(t *testing.T, s string) {
		_, _ = ParseBackendSelector(s)
		_, _ = ParseReleaseSelector(s)
		_, _ = EncodeAssetID(s)
	})
}
