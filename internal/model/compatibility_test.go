package model

import "testing"

func i64(v int64) *int64 { return &v }

// bonsaiProjectorClass reproduces the verified real header: the linking value is the
// projection dimension 5120, while the vision encoder's internal width is 1152.
func bonsaiProjectorClass() Classification {
	return Classification{
		Type:             TypeProjector,
		Architecture:     "clip",
		ProjectionDim:    5120,
		ProjectorType:    "qwen35vl_merger",
		HasVisionEncoder: true,
		// The vision encoder's own embedding length (1152) is deliberately
		// recorded nowhere as a linking value.
		EmbeddingLength: 1152,
	}
}

func bonsaiPrincipalClass() Classification {
	return Classification{Type: TypeModel, Architecture: "qwen35", EmbeddingLength: 5120}
}

func gemmaProjectorClass() Classification {
	return Classification{Type: TypeProjector, Architecture: "clip", ProjectionDim: 3840}
}

func gemmaPrincipalClass() Classification {
	return Classification{Type: TypeModel, Architecture: "gemma4", EmbeddingLength: 3840}
}

func TestProjectorAdmissibleOnProjectionDim(t *testing.T) {
	if got := ProjectorAdmissible(bonsaiProjectorClass(), bonsaiPrincipalClass()); got != Admissible {
		t.Fatalf("projection dim 5120 vs embedding length 5120: got %q, want %q", got, Admissible)
	}
	if got := ProjectorAdmissible(gemmaProjectorClass(), gemmaPrincipalClass()); got != Admissible {
		t.Fatalf("projection dim 3840 vs embedding length 3840: got %q, want %q", got, Admissible)
	}
}

// TestProjectorInternalWidthIsNeverTheLinkingValue guards the single most
// important detail of the criterion: a principal whose embedding length equals
// the projector's own vision width must not be admitted.
func TestProjectorInternalWidthIsNeverTheLinkingValue(t *testing.T) {
	principal := Classification{Type: TypeModel, Architecture: "siglip", EmbeddingLength: 1152}
	if got := ProjectorAdmissible(bonsaiProjectorClass(), principal); got != NotAdmissible {
		t.Fatalf("vision width 1152 used as linking value: got %q, want %q", got, NotAdmissible)
	}
}

func TestProjectorNotAdmissibleOnMismatch(t *testing.T) {
	if got := ProjectorAdmissible(gemmaProjectorClass(), bonsaiPrincipalClass()); got != NotAdmissible {
		t.Fatalf("3840 vs 5120: got %q, want %q", got, NotAdmissible)
	}
}

func TestProjectorUnknownWhenValueMissing(t *testing.T) {
	if got := ProjectorAdmissible(Classification{Type: TypeProjector}, bonsaiPrincipalClass()); got != CompatibilityUnknown {
		t.Fatalf("missing projection dim: got %q, want %q", got, CompatibilityUnknown)
	}
	if got := ProjectorAdmissible(bonsaiProjectorClass(), Classification{Type: TypeModel}); got != CompatibilityUnknown {
		t.Fatalf("missing embedding length: got %q, want %q", got, CompatibilityUnknown)
	}
}

func qwenTokenizer() Classification {
	return Classification{
		Type:         TypeModel,
		Architecture: "qwen35",
		Tokenizer: TokenizerDigest{
			Model:     "gpt2",
			Pre:       "qwen2",
			VocabSize: 151936,
			BOS:       i64(151643),
			EOS:       i64(151645),
			Pad:       i64(151643),
			Digest:    "sha256:aaaa",
		},
	}
}

func TestDraftAdmissibleForIdenticalTokenizers(t *testing.T) {
	draft, target := qwenTokenizer(), qwenTokenizer()
	if got := DraftAdmissible(draft, target); got != Admissible {
		t.Fatalf("identical tokenizers: got %q, want %q", got, Admissible)
	}
}

func TestDraftNotAdmissibleAcrossTokenizerFamilies(t *testing.T) {
	draft := qwenTokenizer()
	target := Classification{
		Type:         TypeModel,
		Architecture: "gemma4",
		Tokenizer: TokenizerDigest{
			Model:     "gemma4",
			VocabSize: 262144,
			BOS:       i64(2),
			EOS:       i64(1),
			Pad:       i64(0),
			Unk:       i64(3),
			Digest:    "sha256:bbbb",
		},
	}
	if got := DraftAdmissible(draft, target); got != NotAdmissible {
		t.Fatalf("differing tokenizer families: got %q, want %q", got, NotAdmissible)
	}
}

func TestDraftNotAdmissibleOnDigestAlone(t *testing.T) {
	draft, target := qwenTokenizer(), qwenTokenizer()
	target.Tokenizer.Digest = "sha256:cccc"
	if got := DraftAdmissible(draft, target); got != NotAdmissible {
		t.Fatalf("differing vocabulary digest: got %q, want %q", got, NotAdmissible)
	}
}

func TestDraftNotAdmissibleOnSpecialTokenPresence(t *testing.T) {
	draft, target := qwenTokenizer(), qwenTokenizer()
	target.Tokenizer.Unk = i64(0)
	if got := DraftAdmissible(draft, target); got != NotAdmissible {
		t.Fatalf("absent unk vs unk id 0: got %q, want %q", got, NotAdmissible)
	}
}

func TestDraftUnknownWithoutTokenizerEvidence(t *testing.T) {
	if got := DraftAdmissible(Classification{Type: TypeModel}, qwenTokenizer()); got != CompatibilityUnknown {
		t.Fatalf("missing tokenizer: got %q, want %q", got, CompatibilityUnknown)
	}
}

func TestEvaluateCompanionsReportsEveryAdmissibleOption(t *testing.T) {
	criterion := ProjectorCriterion(bonsaiPrincipalClass())
	candidates := []Companion{
		{Name: "bonsai-mmproj-BF16.gguf", Classification: bonsaiProjectorClass()},
		{Name: "gemma-4-mmproj.gguf", Classification: gemmaProjectorClass()},
		{Name: "bonsai-mmproj-F32.gguf", Classification: bonsaiProjectorClass()},
		{Name: "Ternary-Bonsai-27B-Q2_0.gguf", Classification: bonsaiPrincipalClass()},
	}
	got := EvaluateCompanions(criterion, candidates)
	if len(got) != 2 {
		t.Fatalf("admissible projectors: got %d (%v), want 2", len(got), got)
	}
	if got[0].Name != "bonsai-mmproj-BF16.gguf" || got[1].Name != "bonsai-mmproj-F32.gguf" {
		t.Fatalf("unexpected admissible set: %v", got)
	}
}

func TestEvaluateCompanionsForDraftRole(t *testing.T) {
	criterion := DraftCriterion(qwenTokenizer())
	candidates := []Companion{
		{Name: "Bonsai-1B-draft.gguf", Classification: qwenTokenizer()},
		{Name: "bonsai-mmproj.gguf", Classification: bonsaiProjectorClass()},
	}
	got := EvaluateCompanions(criterion, candidates)
	if len(got) != 1 || got[0].Name != "Bonsai-1B-draft.gguf" {
		t.Fatalf("admissible drafts: got %v, want the draft alone", got)
	}
}

func TestEvaluateCompanionsSelectsNoneWhenEmpty(t *testing.T) {
	if got := EvaluateCompanions(ProjectorCriterion(bonsaiPrincipalClass()), nil); len(got) != 0 {
		t.Fatalf("no candidates: got %v, want none", got)
	}
}
