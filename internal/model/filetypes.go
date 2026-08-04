// Package model provides the internal model library management for Coquena.
package model

// LlamaFileType is a model-level quantization recipe from llama_ftype. It is
// deliberately distinct from GGMLType: both domains reuse small integers, but
// the same value has different meaning (15 is Q4_K_M here and Q8_K for a
// tensor).
type LlamaFileType int64

// GGMLType is the physical encoding of one tensor descriptor.
type GGMLType uint32

const LlamaFileTypeGuessed LlamaFileType = 1 << 10

var llamaFileTypeNames = map[LlamaFileType]string{
	0: "F32", 1: "F16", 2: "Q4_0", 3: "Q4_1", 5: "Q4_2", 6: "Q4_3", 7: "Q8_0", 8: "Q5_0", 9: "Q5_1",
	10: "Q2_K", 11: "Q3_K_S", 12: "Q3_K_M", 13: "Q3_K_L", 14: "Q4_K_S", 15: "Q4_K_M",
	16: "Q5_K_S", 17: "Q5_K_M", 18: "Q6_K", 19: "IQ2_XXS", 20: "IQ2_XS", 21: "Q2_K_S",
	22: "IQ3_XS", 23: "IQ3_XXS", 24: "IQ1_S", 25: "IQ4_NL", 26: "IQ3_S", 27: "IQ3_M",
	28: "IQ2_S", 29: "IQ2_M", 30: "IQ4_XS", 31: "IQ1_M", 32: "BF16", 33: "Q4_0_4_4",
	34: "Q4_0_4_8", 35: "Q4_0_8_8", 36: "TQ1_0", 37: "TQ2_0", 38: "MXFP4_MOE",
	39: "NVFP4", 40: "Q1_0", 41: "Q2_0",
}

var ggmlTypeNames = map[GGMLType]string{
	0: "F32", 1: "F16", 2: "Q4_0", 3: "Q4_1", 4: "Q4_2", 5: "Q4_3", 6: "Q5_0", 7: "Q5_1",
	8: "Q8_0", 9: "Q8_1", 10: "Q2_K", 11: "Q3_K", 12: "Q4_K", 13: "Q5_K", 14: "Q6_K", 15: "Q8_K",
	16: "IQ2_XXS", 17: "IQ2_XS", 18: "IQ3_XXS", 19: "IQ1_S", 20: "IQ4_NL", 21: "IQ3_S",
	22: "IQ2_S", 23: "IQ4_XS", 24: "I8", 25: "I16", 26: "I32", 27: "I64", 28: "F64",
	29: "IQ1_M", 30: "BF16", 34: "TQ1_0", 35: "TQ2_0", 39: "MXFP4", 40: "NVFP4",
	41: "Q1_0", 42: "Q2_0",
}

// LlamaFileTypeName resolves only a declared model recipe. Guessed values are
// intentionally not unmasked: retaining an upstream guessed suffix would turn
// presentation into a false declaration.
func LlamaFileTypeName(ft LlamaFileType) (string, bool) {
	if ft&LlamaFileTypeGuessed != 0 {
		return "", false
	}
	n, ok := llamaFileTypeNames[ft]
	return n, ok
}

func GGMLTypeName(t GGMLType) (string, bool) {
	n, ok := ggmlTypeNames[t]
	return n, ok
}

func LlamaFileTypeDisplayName(ft LlamaFileType) string {
	if n, ok := LlamaFileTypeName(ft); ok {
		return n
	}
	return "unknown"
}

func GGMLTypeDisplayName(t GGMLType) string {
	if n, ok := GGMLTypeName(t); ok {
		return n
	}
	return "unknown"
}
