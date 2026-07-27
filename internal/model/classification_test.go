package model

import "testing"

// gemmaPrincipal mirrors the verified header of gemma-4-12B-it-QAT-Q4_0.gguf.
func gemmaPrincipal() map[string]any {
	return map[string]any{
		"general.architecture":            "gemma4",
		"general.type":                    "model",
		"general.size_label":              "12B",
		"general.file_type":               uint32(2),
		"gemma4.context_length":           uint32(262144),
		"gemma4.embedding_length":         uint32(3840),
		"tokenizer.ggml.model":            "gemma4",
		"tokenizer.ggml.bos_token_id":     uint32(2),
		"tokenizer.ggml.eos_token_id":     uint32(1),
		"tokenizer.ggml.padding_token_id": uint32(0),
		"tokenizer.ggml.unknown_token_id": uint32(3),
		"general.sampling.temp":           float32(1),
		"general.sampling.top_k":          uint32(64),
		"general.sampling.top_p":          float32(0.95),
	}
}

func TestClassifyPrincipalReadsArchitectureNamespacedKeys(t *testing.T) {
	c := Classify(gemmaPrincipal())
	if c.Type != TypeModel || c.Architecture != "gemma4" || c.SizeLabel != "12B" {
		t.Fatalf("classification=%#v", c)
	}
	if c.FileType == nil || *c.FileType != 2 {
		t.Fatalf("fileType=%v", c.FileType)
	}
	if c.ContextLength != 262144 || c.EmbeddingLength != 3840 {
		t.Fatalf("context=%d embedding=%d", c.ContextLength, c.EmbeddingLength)
	}
	if c.SSM {
		t.Fatal("principal without ssm keys reported ssm")
	}
	if c.Tokenizer.Model != "gemma4" || c.Tokenizer.BOS == nil || *c.Tokenizer.BOS != 2 || c.Tokenizer.Pad == nil || *c.Tokenizer.Pad != 0 {
		t.Fatalf("tokenizer=%#v", c.Tokenizer)
	}
	if c.Sampling.TopK == nil || *c.Sampling.TopK != 64 || c.Sampling.TopP == nil || *c.Sampling.TopP < 0.94 || *c.Sampling.TopP > 0.96 {
		t.Fatalf("sampling=%#v", c.Sampling)
	}
}

func TestClassifyProjectorAcceptsBothKeySpellings(t *testing.T) {
	gemma := Classify(map[string]any{
		"general.architecture":         "clip",
		"general.type":                 "mmproj",
		"clip.has_vision_encoder":      true,
		"clip.has_audio_encoder":       true,
		"clip.vision.projector_type":   "gemma4uv",
		"clip.vision.projection_dim":   uint32(3840),
		"clip.vision.embedding_length": uint32(3840),
	})
	if gemma.Type != TypeProjector || gemma.ProjectorType != "gemma4uv" || gemma.ProjectionDim != 3840 {
		t.Fatalf("gemma projector=%#v", gemma)
	}
	if !gemma.HasVisionEncoder || !gemma.HasAudioEncoder {
		t.Fatalf("gemma encoders=%#v", gemma)
	}
	bonsai := Classify(map[string]any{
		"general.architecture":         "clip",
		"general.type":                 "mmproj",
		"clip.has_vision_encoder":      true,
		"clip.projector_type":          "qwen3vl_merger",
		"clip.vision.projection_dim":   uint32(5120),
		"clip.vision.embedding_length": uint32(1152),
	})
	if bonsai.ProjectorType != "qwen3vl_merger" || bonsai.ProjectionDim != 5120 {
		t.Fatalf("bonsai projector=%#v", bonsai)
	}
	if bonsai.HasAudioEncoder {
		t.Fatal("absent audio encoder key reported as present")
	}
	// The encoder's internal width must never stand in for the linking value.
	if bonsai.ProjectionDim == 1152 {
		t.Fatal("projector used clip.vision.embedding_length as its projection dimension")
	}
}

func TestClassifyUnrecognisedProjectorSpellingStaysUnknown(t *testing.T) {
	c := Classify(map[string]any{
		"general.architecture":     "clip",
		"general.type":             "mmproj",
		"clip.vision.proj_variant": "something_new",
	})
	if c.ProjectorType != "" || c.ProjectionDim != 0 {
		t.Fatalf("guessed projector facts: %#v", c)
	}
}

func TestClassifyDetectsSSMKeys(t *testing.T) {
	c := Classify(map[string]any{
		"general.architecture":    "qwen35",
		"general.type":            "model",
		"general.size_label":      "27B",
		"general.file_type":       uint32(41),
		"qwen35.embedding_length": uint32(5120),
		"qwen35.ssm.conv_kernel":  uint32(4),
		"qwen35.ssm.state_size":   uint32(128),
		"tokenizer.ggml.model":    "gpt2",
		"tokenizer.ggml.pre":      "qwen35",
	})
	if !c.SSM || c.EmbeddingLength != 5120 || c.Tokenizer.Pre != "qwen35" {
		t.Fatalf("classification=%#v", c)
	}
	// The prefix must follow the artifact's own architecture, not a fixed one.
	if Classify(map[string]any{"general.architecture": "gemma4", "gemma4.ssm.conv_kernel": uint32(4)}).SSM != true {
		t.Fatal("ssm keys under a different architecture were missed")
	}
}

func TestClassifyReportsUndeclaredTypeAsUnknown(t *testing.T) {
	c := Classify(map[string]any{"general.architecture": "gemma4", "general.size_label": "12B"})
	if c.Type != TypeUnknown {
		t.Fatalf("type=%q", c.Type)
	}
	if Classify(map[string]any{"general.type": "adapter"}).Type != TypeUnknown {
		t.Fatal("unrecognised declared type was not reported as unknown")
	}
}

func TestClassifyVocabularyDigestIsStableAndArraysAreNotRetained(t *testing.T) {
	vocab := func() map[string]any {
		return map[string]any{
			"general.type":              "model",
			"general.architecture":      "qwen35",
			"tokenizer.ggml.tokens":     []any{"a", "b", "c"},
			"tokenizer.ggml.merges":     []any{"a b"},
			"tokenizer.ggml.token_type": []any{int32(1), int32(1), int32(3)},
		}
	}
	first, second := Classify(vocab()), Classify(vocab())
	if first.Tokenizer.Digest == "" || first.Tokenizer.Digest != second.Tokenizer.Digest {
		t.Fatalf("digests %q and %q", first.Tokenizer.Digest, second.Tokenizer.Digest)
	}
	if first.Tokenizer.VocabSize != 3 {
		t.Fatalf("vocabSize=%d", first.Tokenizer.VocabSize)
	}
	changed := vocab()
	changed["tokenizer.ggml.tokens"] = []any{"a", "b", "d"}
	if Classify(changed).Tokenizer.Digest == first.Tokenizer.Digest {
		t.Fatal("different vocabularies produced the same digest")
	}
	if Classify(map[string]any{"general.type": "model"}).Tokenizer.Digest != "" {
		t.Fatal("digest reported for an artifact carrying no vocabulary")
	}
}
