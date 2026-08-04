package model

import "testing"

func TestQuantizationDomainsDoNotOverlapImplicitly(t *testing.T) {
	if got, ok := LlamaFileTypeName(15); !ok || got != "Q4_K_M" {
		t.Fatalf("LlamaFileTypeName(15) = %q, %v", got, ok)
	}
	if got, ok := GGMLTypeName(15); !ok || got != "Q8_K" {
		t.Fatalf("GGMLTypeName(15) = %q, %v", got, ok)
	}
}

func TestQuantizationDomainUnknowns(t *testing.T) {
	if got := LlamaFileTypeDisplayName(999); got != "unknown" {
		t.Fatalf("recipe=%q", got)
	}
	if got := GGMLTypeDisplayName(999); got != "unknown" {
		t.Fatalf("tensor=%q", got)
	}
}

func TestGuessedRecipeDoesNotDeclareSuffix(t *testing.T) {
	if got, ok := LlamaFileTypeName(LlamaFileTypeGuessed | 15); ok || got != "" {
		t.Fatalf("guessed recipe became declaration: %q", got)
	}
}

func TestDeprecatedRecipeNameStaysClean(t *testing.T) {
	if got := LlamaFileTypeDisplayName(33); got != "Q4_0_4_4" {
		t.Fatalf("deprecated recipe=%q", got)
	}
}
