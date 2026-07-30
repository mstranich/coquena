package model

import "testing"

func TestFileTypeNames(t *testing.T) {
	tests := []struct {
		fileType int64
		expected string
	}{
		{0, "F32"},
		{1, "F16"},
		{2, "Q4_0"},
		{3, "Q4_1"},
		{4, "Q4_2"},
		{5, "Q4_3"},
		{6, "Q5_0"},
		{7, "Q5_1"},
		{8, "Q8_0"},
		{9, "Q8_1"},
		{10, "Q2_K"},
		{11, "Q3_K"},
		{12, "Q4_K"},
		{13, "Q5_K"},
		{14, "Q6_K"},
		{15, "Q8_K"},
		{16, "IQ2_XXS"},
		{17, "IQ2_XS"},
		{18, "IQ3_XXS"},
		{19, "IQ1_S"},
		{20, "IQ4_NL"},
		{21, "IQ3_S"},
		{22, "IQ2_S"},
		{23, "IQ4_XS"},
		{24, "I8"},
		{25, "I16"},
		{26, "I32"},
		{27, "I64"},
		{28, "F64"},
		{29, "IQ1_M"},
		{30, "BF16"},
		{31, "Q4_0_4_4"},
		{32, "Q4_0_4_8"},
		{33, "Q4_0_8_8"},
		{34, "TQ1_0"},
		{35, "TQ2_0"},
		{36, "IQ4_NL_4_4"},
		{37, "IQ4_NL_4_8"},
		{38, "IQ4_NL_8_8"},
		{39, "MXFP4"},
		{40, "NVFP4"},
		{41, "Q1_0"},
		{42, "Q2_0"},
	}

	for _, tt := range tests {
		got, ok := FileTypeNames[tt.fileType]
		if !ok {
			t.Errorf("fileType %d: not found in map", tt.fileType)
			continue
		}
		if got != tt.expected {
			t.Errorf("fileType %d: got %q, want %q", tt.fileType, got, tt.expected)
		}
	}
}

func TestFileTypeNamesMissing(t *testing.T) {
	// Test that missing fileTypes return empty string
	got, ok := FileTypeNames[100]
	if ok {
		t.Errorf("fileType 100: should not be in map, got %q", got)
	}
	if got != "" {
		t.Errorf("fileType 100: expected empty string, got %q", got)
	}
}
