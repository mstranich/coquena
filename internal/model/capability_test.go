package model

import "testing"

func fileType(v int64) *LlamaFileType { ft := LlamaFileType(v); return &ft }

func TestDeriveRuntimeCapabilityTernaryFileType(t *testing.T) {
	got := DeriveRuntimeCapability(Classification{Type: TypeModel, Architecture: "qwen35", FileType: fileType(40)})
	if got.Empty() {
		t.Fatalf("expected a contribution for file type 40")
	}
	if !hasKind(got, CapabilityQuantization) {
		t.Fatalf("got=%+v", got.Requirements)
	}
	if !hasKind(got, CapabilityArchitecture) {
		t.Fatalf("architecture missing: %+v", got.Requirements)
	}
}

func TestDeriveRuntimeCapabilityOneBitFileType(t *testing.T) {
	got := DeriveRuntimeCapability(Classification{Type: TypeModel, Architecture: "qwen35", FileType: fileType(41)})
	if !hasKind(got, CapabilityQuantization) {
		t.Fatalf("got=%+v", got.Requirements)
	}
}

func TestDeriveRuntimeCapabilitySSM(t *testing.T) {
	got := DeriveRuntimeCapability(Classification{Type: TypeModel, Architecture: "qwen35", FileType: fileType(2), SSM: true})
	if !hasKind(got, CapabilityHybridSSM) {
		t.Fatalf("got=%+v", got.Requirements)
	}
	if hasKind(got, CapabilityQuantization) {
		t.Fatalf("file type 2 must not contribute: %+v", got.Requirements)
	}
}

func TestDeriveRuntimeCapabilityOrdinaryModel(t *testing.T) {
	// The real gemma-4 pair: file type 2 for the principal, 32 for its
	// projector, neither carrying SSM keys.
	for _, ft := range []int64{2, 32} {
		got := DeriveRuntimeCapability(Classification{Type: TypeModel, Architecture: "gemma4", FileType: fileType(ft)})
		if !got.Empty() {
			t.Fatalf("file type %d contributed %+v", ft, got.Requirements)
		}
	}
}

func TestDeriveRuntimeCapabilityArchitectureAloneContributesNothing(t *testing.T) {
	if got := DeriveRuntimeCapability(Classification{Type: TypeModel, Architecture: "qwen35"}); !got.Empty() {
		t.Fatalf("got=%+v", got.Requirements)
	}
}

func hasKind(c RuntimeCapability, kind CapabilityKind) bool {
	for _, r := range c.Requirements {
		if r.Kind == kind {
			return true
		}
	}
	return false
}
