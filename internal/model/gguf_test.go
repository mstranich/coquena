package model

import (
	"bytes"
	"encoding/binary"
	"fmt"
	"os"
	"path/filepath"
	"testing"
)

func appendString(b []byte, s string) []byte {
	var n [8]byte
	binary.LittleEndian.PutUint64(n[:], uint64(len(s)))
	return append(append(b, n[:]...), []byte(s)...)
}
func appendType(b []byte, t uint32) []byte {
	var n [4]byte
	binary.LittleEndian.PutUint32(n[:], t)
	return append(b, n[:]...)
}
func TestReadMetadataPreservesScalarArrayAndUnknownKeys(t *testing.T) {
	b := make([]byte, 24)
	copy(b, "GGUF")
	binary.LittleEndian.PutUint32(b[4:], 3)
	binary.LittleEndian.PutUint64(b[16:], 3)
	b = appendString(b, "general.name")
	b = appendType(b, 8)
	b = appendString(b, "example")
	b = appendString(b, "unknown.vendor.flag")
	b = appendType(b, 7)
	b = append(b, 1)
	b = appendString(b, "tokenizer.list")
	b = appendType(b, 9)
	b = appendType(b, 4)
	var count [8]byte
	binary.LittleEndian.PutUint64(count[:], 2)
	b = append(b, count[:]...)
	var one, two [4]byte
	binary.LittleEndian.PutUint32(one[:], 1)
	binary.LittleEndian.PutUint32(two[:], 2)
	b = append(b, one[:]...)
	b = append(b, two[:]...)
	p := filepath.Join(t.TempDir(), "fixture.gguf")
	if err := os.WriteFile(p, b, 0600); err != nil {
		t.Fatal(err)
	}
	r, err := OpenLocal(p)
	if err != nil {
		t.Fatal(err)
	}
	defer r.Close()
	got, err := ReadMetadata(r)
	if err != nil {
		t.Fatal(err)
	}
	if got["general.name"] != "example" || got["unknown.vendor.flag"] != true {
		t.Fatalf("metadata=%#v", got)
	}
	values, ok := got["tokenizer.list"].([]any)
	if !ok || len(values) != 2 || values[0].(uint32) != 1 {
		t.Fatalf("array=%#v", got["tokenizer.list"])
	}
}
func appendUint64(b []byte, v uint64) []byte {
	var n [8]byte
	binary.LittleEndian.PutUint64(n[:], v)
	return append(b, n[:]...)
}

// realShapedHeader builds a header with the shape of a real principal: scalars
// of several widths, a large string array standing in for the token vocabulary,
// and a fixed-width array standing in for the token types.
func realShapedHeader(tokens int) []byte {
	b := make([]byte, 24)
	copy(b, "GGUF")
	binary.LittleEndian.PutUint32(b[4:], 3)
	binary.LittleEndian.PutUint64(b[16:], 5)
	b = appendString(b, "general.architecture")
	b = appendType(b, 8)
	b = appendString(b, "qwen35")
	b = appendString(b, "general.type")
	b = appendType(b, 8)
	b = appendString(b, "model")
	b = appendString(b, "qwen35.context_length")
	b = appendType(b, 4)
	b = appendType(b, 262144)
	b = appendString(b, "tokenizer.ggml.tokens")
	b = appendType(b, 9)
	b = appendType(b, 8)
	b = appendUint64(b, uint64(tokens))
	for i := 0; i < tokens; i++ {
		b = appendString(b, fmt.Sprintf("token%d", i))
	}
	b = appendString(b, "tokenizer.ggml.token_type")
	b = appendType(b, 9)
	b = appendType(b, 5)
	b = appendUint64(b, uint64(tokens))
	for i := 0; i < tokens; i++ {
		b = appendType(b, 1)
	}
	return b
}

func writeFixture(t *testing.T, b []byte) RandomAccessReader {
	t.Helper()
	p := filepath.Join(t.TempDir(), "fixture.gguf")
	if err := os.WriteFile(p, b, 0600); err != nil {
		t.Fatal(err)
	}
	r, err := OpenLocal(p)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = r.Close() })
	return r
}

func TestReadMetadataSkippingArraysMatchesScalarsOfFullRead(t *testing.T) {
	b := realShapedHeader(2048)
	full, err := ReadMetadata(writeFixture(t, b))
	if err != nil {
		t.Fatal(err)
	}
	skipped, err := ReadMetadataSkippingArrays(writeFixture(t, b))
	if err != nil {
		t.Fatal(err)
	}
	if len(full) != len(skipped) {
		t.Fatalf("keys full=%d skipped=%d", len(full), len(skipped))
	}
	for key, want := range full {
		got := skipped[key]
		if values, ok := want.([]any); ok {
			a, ok := got.(SkippedArray)
			if !ok || a.Len != int64(len(values)) {
				t.Fatalf("%s: skipped=%#v want length %d", key, got, len(values))
			}
			continue
		}
		if got != want {
			t.Fatalf("%s: skipped=%#v full=%#v", key, got, want)
		}
	}
	if a := skipped["tokenizer.ggml.tokens"].(SkippedArray); a.ElemType != 8 || a.Len != 2048 {
		t.Fatalf("tokens=%#v", a)
	}
	if a := skipped["tokenizer.ggml.token_type"].(SkippedArray); a.ElemType != 5 || a.Len != 2048 {
		t.Fatalf("token_type=%#v", a)
	}
}

func TestReadMetadataSkippingArraysRejectsMalformedArrays(t *testing.T) {
	header := func(t uint32, count uint64, tail []byte) []byte {
		b := make([]byte, 24)
		copy(b, "GGUF")
		binary.LittleEndian.PutUint32(b[4:], 3)
		binary.LittleEndian.PutUint64(b[16:], 1)
		b = appendString(b, "tokenizer.ggml.tokens")
		b = appendType(b, 9)
		b = appendType(b, t)
		b = appendUint64(b, count)
		return append(b, tail...)
	}
	cases := map[string][]byte{
		// A fixed-width count that would run far past the end of the file: the
		// span is arithmetic, so nothing fails unless it is checked.
		"fixed count past end": header(5, 100000, nil),
		// A string array whose declared elements are simply not there.
		"string payload truncated": header(8, 4, appendString(nil, "only-one")),
		// A string element whose length prefix reaches past the end.
		"string length past end": header(8, 1, appendUint64(nil, 1<<19)),
		// Nested arrays have no computable span and must be refused.
		"nested array": header(9, 2, nil),
		// An element type that is not a known width.
		"unknown element type": header(99, 2, nil),
	}
	for name, b := range cases {
		t.Run(name, func(t *testing.T) {
			if got, err := ReadMetadataSkippingArrays(writeFixture(t, b)); err == nil {
				t.Fatalf("malformed array accepted: %#v", got)
			}
		})
	}
}

func TestReadMetadataRejectsUnsupportedValue(t *testing.T) {
	b := emptyGGUF()
	binary.LittleEndian.PutUint64(b[16:], 1)
	b = appendString(b, "bad")
	b = appendType(b, 99)
	p := filepath.Join(t.TempDir(), "bad.gguf")
	_ = os.WriteFile(p, b, 0600)
	r, _ := OpenLocal(p)
	defer r.Close()
	if _, err := ReadMetadata(r); err == nil {
		t.Fatal("unsupported value accepted")
	}
}

// buildTensorTable builds a tensor table section for a GGUF binary.
// It writes the GGUF descriptor order: name, dimension count, uint64
// dimensions, physical type, data offset.
func buildTensorTable(tensors []tensorEntry) []byte {
	b := make([]byte, 0)

	for _, te := range tensors {
		// name
		b = appendString(b, te.name)
		var count [4]byte
		binary.LittleEndian.PutUint32(count[:], uint32(len(te.shape)))
		b = append(b, count[:]...)
		for _, dim := range te.shape {
			var v [8]byte
			binary.LittleEndian.PutUint64(v[:], dim)
			b = append(b, v[:]...)
		}
		var typ [4]byte
		binary.LittleEndian.PutUint32(typ[:], te.tensorType)
		b = append(b, typ[:]...)
		// offset
		var off [8]byte
		binary.LittleEndian.PutUint64(off[:], te.offset)
		b = append(b, off[:]...)
	}

	return b
}

type tensorEntry struct {
	name       string
	tensorType uint32
	shape      []uint64
	offset     uint64
}

// TestCountParameters verifies the parameter count for a known tensor table.
func TestCountParameters(t *testing.T) {
	// Build a minimal GGUF binary with proper header and tensor table
	b := make([]byte, 24)
	copy(b, "GGUF")
	binary.LittleEndian.PutUint32(b[4:], 3)
	binary.LittleEndian.PutUint64(b[8:], 3) // n_tensors = 3

	// Tensor table
	tensors := []tensorEntry{
		{
			name:       "blk.0.attn_q.weight",
			tensorType: 12, // Q4_K
			shape:      []uint64{512, 4096},
			offset:     0,
		},
		{
			name:       "blk.0.attn_k.weight",
			tensorType: 12, // Q4_K
			shape:      []uint64{512, 4096},
			offset:     100,
		},
		{
			name:       "output.weight",
			tensorType: 15, // Q8_K
			shape:      []uint64{4096},
			offset:     200,
		},
	}

	tensorTable := buildTensorTable(tensors)

	// Combine header and tensor table
	fullBinary := make([]byte, 0, len(b)+len(tensorTable))
	fullBinary = append(fullBinary, b...)
	fullBinary = append(fullBinary, tensorTable...)

	// Write to temp file
	p := filepath.Join(t.TempDir(), "test.gguf")
	if err := os.WriteFile(p, fullBinary, 0600); err != nil {
		t.Fatal(err)
	}

	r, err := OpenLocal(p)
	if err != nil {
		t.Fatal(err)
	}
	defer r.Close()

	got, err := CountParameters(r)
	if err != nil {
		t.Fatal(err)
	}

	// Count elements, not bytes:
	// blk.0.attn_q.weight: 512 * 4096 = 2097152 elements
	// blk.0.attn_k.weight: 512 * 4096 = 2097152 elements
	// output.weight: 4096 elements
	// Total: 2097152 + 2097152 + 4096 = 4198400
	want := int64(4198400)
	if got != want {
		t.Fatalf("CountParameters() = %d, want %d", got, want)
	}
}

func TestScanGGUFConsumesMetadataBeforeTensorDescriptors(t *testing.T) {
	b := emptyGGUF()
	binary.LittleEndian.PutUint64(b[8:], 1)
	binary.LittleEndian.PutUint64(b[16:], 1)
	b = appendString(b, "general.type")
	b = appendType(b, 8)
	b = appendString(b, "model")
	b = append(b, buildTensorTable([]tensorEntry{{name: "blk.0.weight", tensorType: 12, shape: []uint64{2, 3, 4}}})...)
	r := bytes.NewReader(b)
	scan, err := ScanGGUF(r, true)
	if err != nil {
		t.Fatal(err)
	}
	if scan.Metadata["general.type"] != "model" || len(scan.Tensors) != 1 {
		t.Fatalf("scan=%+v", scan)
	}
	if got, _ := tensorElements(scan.Tensors[0]); got != 24 {
		t.Fatalf("elements=%d", got)
	}
}

func TestDominantPhysicalTypeUsesStructuralParameterCount(t *testing.T) {
	scan := GGUFScan{Tensors: []TensorDescriptor{
		{Name: "token_embd.weight", Type: 14, Dimensions: []uint64{1_000_000}},
		{Name: "blk.0.base", Type: 2, Dimensions: []uint64{100}},
		{Name: "blk.0.exception", Type: 0, Dimensions: []uint64{20}},
		{Name: "blk.1.exception", Type: 0, Dimensions: []uint64{20}},
	}}
	typ, ok, err := DominantPhysicalType(scan)
	// Tensor count chooses F32 (two tensors) and encoded bytes choose F32
	// (160 vs roughly 50); scalar parameters correctly choose Q4_0 (100 vs 40).
	if err != nil || !ok || typ != 2 {
		t.Fatalf("type=%d ok=%v err=%v", typ, ok, err)
	}
}

func TestDominantPhysicalTypeUnavailableWithoutSupportedScope(t *testing.T) {
	_, ok, err := DominantPhysicalType(GGUFScan{Tensors: []TensorDescriptor{{Name: "token_embd.weight", Type: 12, Dimensions: []uint64{100}}}})
	if err != nil || ok {
		t.Fatalf("ok=%v err=%v", ok, err)
	}
}

// TestCountParametersEmpty verifies that an empty tensor table returns 0.
func TestCountParametersEmpty(t *testing.T) {
	b := make([]byte, 24)
	copy(b, "GGUF")
	binary.LittleEndian.PutUint32(b[4:], 3)
	binary.LittleEndian.PutUint64(b[8:], 0) // n_tensors = 0

	p := filepath.Join(t.TempDir(), "empty.gguf")
	if err := os.WriteFile(p, b, 0600); err != nil {
		t.Fatal(err)
	}

	r, err := OpenLocal(p)
	if err != nil {
		t.Fatal(err)
	}
	defer r.Close()

	got, err := CountParameters(r)
	if err != nil {
		t.Fatal(err)
	}

	if got != 0 {
		t.Fatalf("CountParameters() = %d, want 0", got)
	}
}
