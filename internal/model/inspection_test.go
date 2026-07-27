package model

import "testing"

func TestInspectionResultSummarizesLargeArrays(t *testing.T) {
	record := Record{SchemaVersion: 1, Metadata: map[string]any{"small": []any{1, 2}, "tokens": make([]any, 300)}}
	summary := InspectionResult(record, false)
	if len(summary.OmittedMetadata) != 1 || summary.OmittedMetadata[0] != "tokens" {
		t.Fatalf("summary=%+v", summary)
	}
	if got := summary.GGUF["small"].([]any); len(got) != 2 {
		t.Fatalf("small=%v", got)
	}
	raw := InspectionResult(record, true)
	if len(raw.GGUF["tokens"].([]any)) != 300 {
		t.Fatal("raw metadata changed")
	}
}
