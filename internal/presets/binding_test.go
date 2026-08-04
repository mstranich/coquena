package presets

import (
	"testing"

	"github.com/mstranich/coquena/internal/model"
)

type fakeResolver struct {
	entry model.LibraryEntry
	err   error
}

type routingResolver map[string]struct {
	entry model.LibraryEntry
	err   error
}

func (r routingResolver) ResolveInstalled(reference string) (model.LibraryEntry, error) {
	result := r[reference]
	return result.entry, result.err
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
	if binding.Resolved.CanonicalReference != "qwen/Qwen3-4B-GGUF@abc:Qwen3-4B-Q4_K_M.gguf" {
		t.Fatalf("canonical=%q", binding.Resolved.CanonicalReference)
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

func TestEvaluateBindingKeepsCanonicalArtifactWhenSelectorBecomesAmbiguous(t *testing.T) {
	binding := ModelBinding{
		Requested: ModelReference{Repository: "qwen/repo", Raw: "qwen/repo:Q4_K_M"},
		Resolved:  &ResolvedModel{CanonicalReference: "qwen/repo@abc:model-Q4_K_M.gguf", Filename: "model-Q4_K_M.gguf", ResolvedRevision: "abc"},
	}
	resolver := routingResolver{
		"qwen/repo@abc:model-Q4_K_M.gguf": {entry: model.LibraryEntry{Source: model.Source{Repository: "qwen/repo", ResolvedRevision: "abc", Filename: "model-Q4_K_M.gguf"}}},
		"qwen/repo:Q4_K_M":                {err: &model.ErrAmbiguousInstalledMatch{Reference: "qwen/repo:Q4_K_M", Matches: []string{"qwen/repo@abc:model-Q4_K_M.gguf", "qwen/repo@def:model-Q4_K_M-imatrix.gguf"}}},
	}
	if got := EvaluateBinding(resolver, binding); got != BindingSame {
		t.Fatalf("state=%q", got)
	}
	if StaleModel(resolver, binding) {
		t.Fatal("new sibling marked canonical binding stale")
	}
}

func TestEvaluateBindingDistinguishesOutcomesWithoutMutation(t *testing.T) {
	binding := ModelBinding{
		Requested: ModelReference{Repository: "qwen/repo", Raw: "qwen/repo:Q4_K_M"},
		Resolved:  &ResolvedModel{CanonicalReference: "qwen/repo@abc:old.gguf", Filename: "old.gguf", ResolvedRevision: "abc"},
	}
	canonicalMissing := &model.ErrNoInstalledMatch{Reference: binding.Resolved.CanonicalReference}
	tests := []struct {
		name string
		next struct {
			entry model.LibraryEntry
			err   error
		}
		want BindingState
	}{
		{"different", struct {
			entry model.LibraryEntry
			err   error
		}{entry: model.LibraryEntry{Source: model.Source{Filename: "new.gguf", ResolvedRevision: "def"}}}, BindingDifferent},
		{"missing", struct {
			entry model.LibraryEntry
			err   error
		}{err: &model.ErrNoInstalledMatch{Reference: binding.Requested.Raw}}, BindingMissing},
		{"ambiguous", struct {
			entry model.LibraryEntry
			err   error
		}{err: &model.ErrAmbiguousInstalledMatch{Reference: binding.Requested.Raw, Matches: []string{"a", "b"}}}, BindingAmbiguous},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			resolver := routingResolver{
				binding.Resolved.CanonicalReference: {err: canonicalMissing},
				binding.Requested.Raw:               tt.next,
			}
			before := binding
			if got := EvaluateBinding(resolver, binding); got != tt.want {
				t.Fatalf("state=%q want=%q", got, tt.want)
			}
			if binding != before {
				t.Fatal("evaluation mutated binding")
			}
		})
	}
}
