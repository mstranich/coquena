package model

import (
	"math"
	"testing"
)

func discoveredFile(name string, size int64, kind ArtifactType) DiscoveredCandidate {
	return DiscoveredCandidate{
		Source:         Source{Filename: name},
		Artifact:       Artifact{SizeBytes: size},
		Classification: Classification{Type: kind},
	}
}

func TestProjectDiscoveryGroupsQuantizationsAndCompanions(t *testing.T) {
	candidates := []DiscoveredCandidate{
		discoveredFile("single-Q8_0.gguf", 10, TypeModel),
		discoveredFile("Q4_K_M/model-00002-of-00002.gguf", 20, TypeModel),
		discoveredFile("Q4_K_M/model-00001-of-00002.gguf", 15, TypeModel),
		discoveredFile("flat-Q5_K_M-00001-of-00002.gguf", 30, TypeModel),
		discoveredFile("flat-Q5_K_M-00002-of-00002.gguf", 40, TypeModel),
		discoveredFile("mmproj.gguf", 5, TypeProjector),
	}
	candidates[1].Selected = true

	quantizations, companions := projectDiscovery(candidates)
	if len(quantizations) != 3 || len(companions) != 1 {
		t.Fatalf("quantizations=%+v companions=%+v", quantizations, companions)
	}
	if quantizations[0].Label != "single-Q8_0" || quantizations[0].TotalSizeBytes != 10 || !quantizations[0].TotalSizeKnown {
		t.Fatalf("single=%+v", quantizations[0])
	}
	if quantizations[1].Label != "Q4_K_M" || len(quantizations[1].Files) != 2 || quantizations[1].TotalSizeBytes != 35 || !quantizations[1].Selected {
		t.Fatalf("directory split=%+v", quantizations[1])
	}
	if quantizations[1].Files[0].Source.Filename != "Q4_K_M/model-00001-of-00002.gguf" {
		t.Fatalf("files not deterministic: %+v", quantizations[1].Files)
	}
	if quantizations[2].Label != "flat-Q5_K_M" || len(quantizations[2].Files) != 2 || quantizations[2].TotalSizeBytes != 70 {
		t.Fatalf("flat split=%+v", quantizations[2])
	}
	if companions[0].Source.Filename != "mmproj.gguf" {
		t.Fatalf("companions=%+v", companions)
	}
}

func TestProjectDiscoveryDoesNotClaimMalformedFlatSplit(t *testing.T) {
	quantizations, _ := projectDiscovery([]DiscoveredCandidate{
		discoveredFile("model-00001-of-00003.gguf", 10, TypeModel),
		discoveredFile("model-00003-of-00003.gguf", 10, TypeModel),
	})
	if len(quantizations) != 2 {
		t.Fatalf("incomplete series grouped: %+v", quantizations)
	}
}

func TestProjectDiscoveryRequiresEverySizeAndRejectsOverflow(t *testing.T) {
	for _, tc := range []struct {
		name  string
		files []DiscoveredCandidate
	}{
		{"missing", []DiscoveredCandidate{
			discoveredFile("Q4/model-00001-of-00002.gguf", 10, TypeModel),
			discoveredFile("Q4/model-00002-of-00002.gguf", 0, TypeModel),
		}},
		{"overflow", []DiscoveredCandidate{
			discoveredFile("Q4/model-00001-of-00002.gguf", math.MaxInt64, TypeModel),
			discoveredFile("Q4/model-00002-of-00002.gguf", 1, TypeModel),
		}},
	} {
		t.Run(tc.name, func(t *testing.T) {
			quantizations, _ := projectDiscovery(tc.files)
			if len(quantizations) != 1 || quantizations[0].TotalSizeKnown || quantizations[0].TotalSizeBytes != 0 {
				t.Fatalf("quantizations=%+v", quantizations)
			}
		})
	}
}
