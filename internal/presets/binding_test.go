package presets

import (
	"testing"

	"github.com/mstranich/coquena/internal/model"
)

type fakeResolver struct {
	entry model.LibraryEntry
	err   error
}

func (f fakeResolver) ResolveInstalled(reference string) (model.LibraryEntry, error) {
	return f.entry, f.err
}

func TestBindModelInstalled(t *testing.T) {
	resolver := fakeResolver{entry: model.LibraryEntry{Source: model.Source{Repository: "qwen/Qwen3-4B-GGUF", Filename: "Qwen3-4B-Q4_K_M.gguf", ResolvedRevision: "abc"}, State: "installed"}}
	binding, err := BindModel(resolver, "qwen/Qwen3-4B-GGUF:Q4_K_M", false)
	if err != nil {
		t.Fatal(err)
	}
	if binding.Requested.Raw != "qwen/Qwen3-4B-GGUF:Q4_K_M" || binding.Requested.Repository != "qwen/Qwen3-4B-GGUF" || binding.Requested.Quant != "Q4_K_M" {
		t.Fatalf("requested=%+v", binding.Requested)
	}
	if binding.Resolved == nil || binding.Resolved.Filename != "Qwen3-4B-Q4_K_M.gguf" || binding.Resolved.ResolvedRevision != "abc" {
		t.Fatalf("resolved=%+v", binding.Resolved)
	}
}

func TestBindModelRefusesUnresolvedWithoutAllowMissing(t *testing.T) {
	resolver := fakeResolver{err: &model.ErrNoInstalledMatch{Reference: "qwen/Qwen3-4B-GGUF:Q4_K_M"}}
	if _, err := BindModel(resolver, "qwen/Qwen3-4B-GGUF:Q4_K_M", false); err == nil {
		t.Fatal("expected error")
	}
}

func TestBindModelAllowsMissing(t *testing.T) {
	resolver := fakeResolver{err: &model.ErrNoInstalledMatch{Reference: "qwen/Qwen3-4B-GGUF:Q4_K_M"}}
	binding, err := BindModel(resolver, "qwen/Qwen3-4B-GGUF:Q4_K_M", true)
	if err != nil {
		t.Fatal(err)
	}
	if binding.Resolved != nil {
		t.Fatalf("resolved=%+v", binding.Resolved)
	}
	if binding.Requested.Raw != "qwen/Qwen3-4B-GGUF:Q4_K_M" {
		t.Fatalf("requested=%+v", binding.Requested)
	}
}

func TestBindModelRefusesAmbiguousEvenWithAllowMissing(t *testing.T) {
	resolver := fakeResolver{err: &model.ErrAmbiguousInstalledMatch{Reference: "qwen/Qwen3-4B-GGUF:Q4_K_M", Matches: []string{"a-Q4_K_M.gguf", "b-Q4_K_M.gguf"}}}
	if _, err := BindModel(resolver, "qwen/Qwen3-4B-GGUF:Q4_K_M", true); err == nil {
		t.Fatal("expected error")
	}
}

func TestStaleModelDetectsMissingArtifact(t *testing.T) {
	binding := ModelBinding{Requested: ModelReference{Raw: "qwen/Qwen3-4B-GGUF:Q4_K_M"}, Resolved: &ResolvedModel{Filename: "old.gguf"}}
	resolver := fakeResolver{err: &model.ErrNoInstalledMatch{Reference: "qwen/Qwen3-4B-GGUF:Q4_K_M"}}
	if !StaleModel(resolver, binding) {
		t.Fatal("expected stale")
	}
}

func TestStaleModelFalseWhenMatching(t *testing.T) {
	binding := ModelBinding{Requested: ModelReference{Raw: "qwen/Qwen3-4B-GGUF:Q4_K_M"}, Resolved: &ResolvedModel{Filename: "Qwen3-4B-Q4_K_M.gguf"}}
	resolver := fakeResolver{entry: model.LibraryEntry{Source: model.Source{Filename: "Qwen3-4B-Q4_K_M.gguf"}, State: "installed"}}
	if StaleModel(resolver, binding) {
		t.Fatal("expected not stale")
	}
}

func TestStaleModelFalseWhenNotResolved(t *testing.T) {
	binding := ModelBinding{Requested: ModelReference{Raw: "qwen/Qwen3-4B-GGUF:Q4_K_M"}}
	if StaleModel(fakeResolver{}, binding) {
		t.Fatal("expected not stale for an unresolved binding")
	}
}
