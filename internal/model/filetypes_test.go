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
		{34, "TQ1_0"},
		{35, "TQ2_0"},
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

func TestDeprecatedFileTypeNames(t *testing.T) {
	tests := []struct {
		fileType int64
		expected string
	}{
		{4, "Q4_2"},
		{5, "Q4_3"},
		{31, "Q4_0_4_4"},
		{32, "Q4_0_4_8"},
		{33, "Q4_0_8_8"},
		{36, "IQ4_NL_4_4"},
		{37, "IQ4_NL_4_8"},
		{38, "IQ4_NL_8_8"},
	}

	for _, tt := range tests {
		got, ok := DeprecatedFileTypeNames[tt.fileType]
		if !ok {
			t.Errorf("fileType %d: not found in DeprecatedFileTypeNames", tt.fileType)
			continue
		}
		if got != tt.expected {
			t.Errorf("fileType %d: got %q, want %q", tt.fileType, got, tt.expected)
		}
	}
}

func TestFileTypeDisplayName(t *testing.T) {
	t.Run("active type returns clean name", func(t *testing.T) {
		if got := FileTypeDisplayName(15); got != "Q8_K" {
			t.Errorf("FileTypeDisplayName(15) = %q, want %q", got, "Q8_K")
		}
	})

	t.Run("deprecated type returns clean name without removed suffix", func(t *testing.T) {
		if got := FileTypeDisplayName(4); got != "Q4_2" {
			t.Errorf("FileTypeDisplayName(4) = %q, want %q", got, "Q4_2")
		}
	})

	t.Run("unknown type returns unknown", func(t *testing.T) {
		if got := FileTypeDisplayName(999); got != "unknown" {
			t.Errorf("FileTypeDisplayName(999) = %q, want %q", got, "unknown")
		}
	})

	t.Run("deprecated type not in FileTypeNames", func(t *testing.T) {
		if _, ok := FileTypeNames[4]; ok {
			t.Errorf("fileType 4 should not be in FileTypeNames")
		}
	})

	t.Run("active type not in DeprecatedFileTypeNames", func(t *testing.T) {
		if _, ok := DeprecatedFileTypeNames[15]; ok {
			t.Errorf("fileType 15 should not be in DeprecatedFileTypeNames")
		}
	})
}

func TestFileTypeNamesMissing(t *testing.T) {
	// Test that missing fileTypes return empty string from the map directly.
	got, ok := FileTypeNames[100]
	if ok {
		t.Errorf("fileType 100: should not be in map, got %q", got)
	}
	if got != "" {
		t.Errorf("fileType 100: expected empty string, got %q", got)
	}
}
