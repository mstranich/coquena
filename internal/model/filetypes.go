// Package model provides the internal model library management for Coquena.
package model

// FileTypeNames maps fileType integers to human-readable quantization names for
// types that are actively supported by llama.cpp. The mapping is derived from
// enum ggml_type in ggml/include/ggml.h. Types whose support has been removed
// from active development live in DeprecatedFileTypeNames.
var FileTypeNames = map[int64]string{
	0:  "F32",
	1:  "F16",
	2:  "Q4_0",
	3:  "Q4_1",
	6:  "Q5_0",
	7:  "Q5_1",
	8:  "Q8_0",
	9:  "Q8_1",
	10: "Q2_K",
	11: "Q3_K",
	12: "Q4_K",
	13: "Q5_K",
	14: "Q6_K",
	15: "Q8_K",
	16: "IQ2_XXS",
	17: "IQ2_XS",
	18: "IQ3_XXS",
	19: "IQ1_S",
	20: "IQ4_NL",
	21: "IQ3_S",
	22: "IQ2_S",
	23: "IQ4_XS",
	24: "I8",
	25: "I16",
	26: "I32",
	27: "I64",
	28: "F64",
	29: "IQ1_M",
	30: "BF16",
	34: "TQ1_0",
	35: "TQ2_0",
	39: "MXFP4",
	40: "NVFP4",
	41: "Q1_0",
	42: "Q2_0",
}

// DeprecatedFileTypeNames maps fileType integers to human-readable
// quantization names for types whose support has been removed from active
// development in llama.cpp. These types still function correctly (especially
// for companion artifacts like projectors) but are no longer actively
// maintained. Names are clean — no "(removed)" suffix — since the display
// layer has no need to signal deprecation status.
var DeprecatedFileTypeNames = map[int64]string{
	4:  "Q4_2",
	5:  "Q4_3",
	31: "Q4_0_4_4",
	32: "Q4_0_4_8",
	33: "Q4_0_8_8",
	36: "IQ4_NL_4_4",
	37: "IQ4_NL_4_8",
	38: "IQ4_NL_8_8",
}

// FileTypeDisplayName resolves a fileType integer to its human-readable
// quantization name. It checks FileTypeNames first (actively supported types),
// then DeprecatedFileTypeNames (types whose active development stopped but
// which still function correctly). Returns "unknown" when neither map has an
// entry, which is the same fallback behavior as a missing map key — callers
// that need to distinguish "not mapped" from "deprecated" should consult the
// maps directly.
func FileTypeDisplayName(ft int64) string {
	if n, ok := FileTypeNames[ft]; ok {
		return n
	}
	if n, ok := DeprecatedFileTypeNames[ft]; ok {
		return n
	}
	return "unknown"
}
