package model

import "testing"

func TestInspectionOmitsVocabularyArrays(t *testing.T) {
	meta := map[string]any{
		"general.type":              "model",
		"general.architecture":      "qwen35",
		"qwen35.embedding_length":   uint32(3840),
		"tokenizer.ggml.model":      "gpt2",
		"tokenizer.ggml.tokens":     []any{"alpha", "beta", "gamma"},
		"tokenizer.ggml.merges":     []any{"a b"},
		"tokenizer.ggml.token_type": []any{int32(1), int32(1), int32(1)},
	}
	inspection := NewInspection(Source{Filename: "m.gguf"}, Artifact{SizeBytes: 7}, meta)
	for _, key := range []string{"tokenizer.ggml.tokens", "tokenizer.ggml.merges", "tokenizer.ggml.token_type"} {
		if _, ok := inspection.Metadata[key]; ok {
			t.Fatalf("array key %q survived the projection", key)
		}
	}
	if inspection.Metadata["general.architecture"] != "qwen35" {
		t.Fatalf("scalar keys missing: %+v", inspection.Metadata)
	}
	if inspection.Classification.Tokenizer.VocabSize != 3 || inspection.Classification.Tokenizer.Digest == "" {
		t.Fatalf("tokenizer facts lost: %+v", inspection.Classification.Tokenizer)
	}
	if inspection.Classification.EmbeddingLength != 3840 {
		t.Fatalf("classification=%+v", inspection.Classification)
	}
}

// The record handed to publication must still carry the identity and
// classification the sidecar persists, since Resume builds it from the
// projection rather than reading the header twice.
func TestInspectionRecordCarriesPersistedState(t *testing.T) {
	inspection := NewInspection(Source{Filename: "m.gguf", Repository: "org/repo"}, Artifact{SizeBytes: 7}, map[string]any{"general.type": "mmproj"})
	record := inspection.Record()
	if record.SchemaVersion != RecordSchemaVersion || record.Source.Repository != "org/repo" || record.Artifact.SizeBytes != 7 {
		t.Fatalf("record=%+v", record)
	}
	if record.Classification.Type != TypeProjector {
		t.Fatalf("classification=%+v", record.Classification)
	}
}
