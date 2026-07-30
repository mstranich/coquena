package model

import (
	"encoding/binary"
	"fmt"
	"io"
	"math"
)

const maxMetadataBytes = int64(64 << 20)

// ggufScalarSizes maps a GGUF value type to its fixed width in bytes. It is
// shared by the decoding and the skipping reads so both agree on the span an
// element occupies.
var ggufScalarSizes = map[uint32]int64{0: 1, 1: 1, 2: 2, 3: 2, 4: 4, 5: 4, 6: 4, 7: 1, 10: 8, 11: 8, 12: 8}

// ggufTypeSize maps GGUF tensor types to their element size in bytes. Used by
// CountParameters to compute the total parameter count from the tensor table.
var ggufTypeSize = map[uint32]float64{
	0: 4,    // F32
	1: 2,    // F16
	2: 0.5,  // Q4_0
	3: 0.625, // Q4_1
	4: 4,    // U32 (was missing - caused CountParameters to return 0)
	5: 0.5,  // Q4_3
	6: 1,    // Q5_0
	7: 1,    // Q5_1
	8: 1.125, // Q8_0
	9: 1,    // Q8_1
	10: 1.25, // Q2_K
	11: 1.5, // Q3_K
	12: 1.59375, // Q4_K
	13: 2,   // Q5_K
	14: 2.5, // Q6_K
	15: 2,   // Q8_K
	16: 0.5, // IQ2_XXS
	17: 0.625, // IQ2_XS
	18: 0.75, // IQ3_XXS
	19: 0.875, // IQ1_S
	20: 1,   // IQ4_NL
	21: 1,   // IQ3_S
	22: 1,   // IQ2_S
	23: 1,   // IQ4_XS
	24: 4,   // I8
	25: 2,   // I16
	26: 4,   // I32
	27: 8,   // I64
	28: 8,   // F64
	29: 1,   // IQ1_M
	30: 2,   // BF16
	34: 1,   // TQ1_0
	35: 1,   // TQ2_0
	39: 1,   // MXFP4
	40: 1,   // NVFP4
	41: 0.5, // Q1_0
	42: 1,   // Q2_0
}

// SkippedArray stands in the metadata map for an array whose elements were not
// decoded. It keeps what a caller can still learn cheaply — the element type
// and the element count — because the count is the only thing classification
// asks of the vocabulary arrays, while their contents are what make a full read
// expensive: a real principal declares a quarter of a million tokens and as
// many merges.
type SkippedArray struct {
	ElemType uint32
	Len      int64
}

// ReadMetadata decodes every value, arrays included. It stays the read for the
// paths that genuinely need array contents — the tokenizer vocabulary digest
// hashes the token, merge and token-type arrays at reconciliation time.
func ReadMetadata(r RandomAccessReader) (map[string]any, error) {
	return readMetadata(r, false)
}

// ReadMetadataSkippingArrays returns the same scalar entries as ReadMetadata
// but records each array as a SkippedArray instead of materializing it. It
// exists because classification runs on every library enumeration, where
// decoding half a million boxed values per artifact is the dominant cost and
// none of it is used.
func ReadMetadataSkippingArrays(r RandomAccessReader) (map[string]any, error) {
	return readMetadata(r, true)
}

func readMetadata(r RandomAccessReader, skip bool) (map[string]any, error) {
	if r.Size() < 24 {
		return nil, fmt.Errorf("GGUF is too small")
	}
	h := make([]byte, 24)
	if _, err := r.ReadAt(h, 0); err != nil {
		return nil, err
	}
	if string(h[:4]) != "GGUF" {
		return nil, fmt.Errorf("invalid GGUF magic")
	}
	v := binary.LittleEndian.Uint32(h[4:8])
	if v < 2 || v > 3 {
		return nil, fmt.Errorf("unsupported GGUF version %d", v)
	}
	n := binary.LittleEndian.Uint64(h[16:24])
	if n > 1<<20 {
		return nil, fmt.Errorf("too many GGUF metadata entries")
	}
	off := int64(24)
	values := map[string]any{}
	for i := uint64(0); i < n; i++ {
		key, next, err := readString(r, off)
		if err != nil {
			return nil, err
		}
		off = next
		t := make([]byte, 4)
		if _, err = r.ReadAt(t, off); err != nil {
			return nil, err
		}
		off += 4
		vt := binary.LittleEndian.Uint32(t)
		var value any
		if skip && vt == 9 {
			value, next, err = skipArray(r, off)
		} else {
			value, next, err = readValue(r, off, vt)
		}
		if err != nil {
			return nil, err
		}
		off = next
		values[key] = value
		if off > maxMetadataBytes {
			return nil, fmt.Errorf("GGUF metadata exceeds limit")
		}
	}
	return values, nil
}
func readString(r RandomAccessReader, off int64) (string, int64, error) {
	b := make([]byte, 8)
	if _, e := r.ReadAt(b, off); e != nil {
		return "", off, e
	}
	n := binary.LittleEndian.Uint64(b)
	if n > 1<<20 {
		return "", off, fmt.Errorf("GGUF string is too large")
	}
	b = make([]byte, n)
	if _, e := r.ReadAt(b, off+8); e != nil && e != io.EOF {
		return "", off, e
	}
	return string(b), off + 8 + int64(n), nil
}
func readValue(r RandomAccessReader, off int64, t uint32) (any, int64, error) {
	sizes := ggufScalarSizes
	if t == 8 {
		return readString(r, off)
	}
	if t == 9 {
		b := make([]byte, 12)
		if _, e := r.ReadAt(b, off); e != nil {
			return nil, off, e
		}
		et, n := binary.LittleEndian.Uint32(b), binary.LittleEndian.Uint64(b[4:])
		if n > 1<<20 {
			return nil, off, fmt.Errorf("GGUF array too large")
		}
		a := make([]any, 0, n)
		o := off + 12
		for i := uint64(0); i < n; i++ {
			v, no, e := readValue(r, o, et)
			if e != nil {
				return nil, off, e
			}
			a = append(a, v)
			o = no
		}
		return a, o, nil
	}
	z, ok := sizes[t]
	if !ok {
		return nil, off, fmt.Errorf("unsupported GGUF value type %d", t)
	}
	b := make([]byte, z)
	if _, e := r.ReadAt(b, off); e != nil {
		return nil, off, e
	}
	var v any
	switch t {
	case 0:
		v = b[0]
	case 1:
		v = int8(b[0])
	case 2:
		v = binary.LittleEndian.Uint16(b)
	case 3:
		v = int16(binary.LittleEndian.Uint16(b))
	case 4:
		v = binary.LittleEndian.Uint32(b)
	case 5:
		v = int32(binary.LittleEndian.Uint32(b))
	case 6:
		v = math32(b)
	case 7:
		v = b[0] != 0
	case 10:
		v = binary.LittleEndian.Uint64(b)
	case 11:
		v = int64(binary.LittleEndian.Uint64(b))
	case 12:
		v = math64(b)
	}
	return v, off + z, nil
}

// skipArray reads an array's header and advances past its payload without
// decoding any element. Because the offset is then reached by arithmetic rather
// than by reads that would fail on their own, every computed end is checked
// against the file size: a count large enough to run past the end of the file
// must be an error, never a silent seek beyond it.
func skipArray(r RandomAccessReader, off int64) (any, int64, error) {
	b := make([]byte, 12)
	if _, e := r.ReadAt(b, off); e != nil {
		return nil, off, e
	}
	et, n := binary.LittleEndian.Uint32(b), binary.LittleEndian.Uint64(b[4:])
	if n > 1<<20 {
		return nil, off, fmt.Errorf("GGUF array too large")
	}
	o := off + 12
	switch et {
	case 9:
		// A nested array's length is only discoverable by walking its own
		// header, so no single span covers it. Refuse explicitly rather than
		// mis-compute an offset and read the rest of the section as garbage.
		return nil, off, fmt.Errorf("nested GGUF array is not supported")
	case 8:
		// Each string carries its own 8-byte length prefix, so the end of the
		// payload is only known by walking. Only the prefixes are read: no Go
		// string and no boxed value is allocated per element.
		p := make([]byte, 8)
		for i := uint64(0); i < n; i++ {
			if _, e := r.ReadAt(p, o); e != nil {
				return nil, off, e
			}
			m := binary.LittleEndian.Uint64(p)
			if m > 1<<20 {
				return nil, off, fmt.Errorf("GGUF string is too large")
			}
			o += 8 + int64(m)
			if o > r.Size() {
				return nil, off, fmt.Errorf("GGUF array runs past end of file")
			}
		}
	default:
		z, ok := ggufScalarSizes[et]
		if !ok {
			return nil, off, fmt.Errorf("unsupported GGUF value type %d", et)
		}
		// Fixed-width elements are contiguous, so the whole span is one
		// multiplication. n is bounded above by 1<<20 and z by 8, so the
		// product cannot overflow.
		o += int64(n) * z
		if o > r.Size() {
			return nil, off, fmt.Errorf("GGUF array runs past end of file")
		}
	}
	return SkippedArray{ElemType: et, Len: int64(n)}, o, nil
}
func math32(b []byte) float32 { return math.Float32frombits(binary.LittleEndian.Uint32(b)) }
func math64(b []byte) float64 { return math.Float64frombits(binary.LittleEndian.Uint64(b)) }

// CountParameters reads the tensor table and computes the total parameter count.
// It sums the number of elements across all tensors, regardless of element width.
func CountParameters(r RandomAccessReader) (int64, error) {
	h := make([]byte, 24)
	if _, err := r.ReadAt(h, 0); err != nil {
		return 0, err
	}
	if string(h[:4]) != "GGUF" {
		return 0, fmt.Errorf("invalid GGUF magic")
	}
	nTensors := binary.LittleEndian.Uint64(h[8:16])
	totalParams := int64(0)
	off := int64(24)
	for i := uint64(0); i < nTensors; i++ {
		name, next, err := readString(r, off)
		if err != nil {
			return 0, err
		}
		off = next
		_ = name
		typeBytes := make([]byte, 4)
		if _, err := r.ReadAt(typeBytes, off); err != nil {
			return 0, err
		}
		off += 4
		shape, nextOff, err := readUint32Array(r, off)
		if err != nil {
			return 0, err
		}
		off = nextOff
		_ = shape
		elements := int64(1)
		for _, dim := range shape {
			elements *= int64(dim)
		}
		totalParams += elements
		// Skip the offset field (uint64)
		off += 8
	}
	return totalParams, nil
}

// MostCommonTensorType reads the tensor table and returns the most common tensor type.
// This is the deterministic way to determine quantization: read actual tensor types,
// not the legacy general.file_type metadata field which is unreliable.
func MostCommonTensorType(r RandomAccessReader) (uint32, error) {
	h := make([]byte, 24)
	if _, err := r.ReadAt(h, 0); err != nil {
		return 0, err
	}
	if string(h[:4]) != "GGUF" {
		return 0, fmt.Errorf("invalid GGUF magic")
	}
	nTensors := binary.LittleEndian.Uint64(h[8:16])
	typeCounts := map[uint32]int{}
	off := int64(24)
	for i := uint64(0); i < nTensors; i++ {
		_, next, err := readString(r, off)
		if err != nil {
			return 0, err
		}
		off = next
		typeBytes := make([]byte, 4)
		if _, err := r.ReadAt(typeBytes, off); err != nil {
			return 0, err
		}
		tensorType := binary.LittleEndian.Uint32(typeBytes)
		off += 4
		_, nextOff, err := readUint32Array(r, off)
		if err != nil {
			return 0, err
		}
		off = nextOff
		off += 8 // skip offset
		typeCounts[tensorType]++
	}
	var mostCommon uint32
	maxCount := 0
	for t, c := range typeCounts {
		if c > maxCount {
			mostCommon = t
			maxCount = c
		}
	}
	return mostCommon, nil
}

// readUint32Array reads an array of uint32 values from the GGUF reader.
// Returns the shape values and the offset after the array.
func readUint32Array(r RandomAccessReader, off int64) ([]uint32, int64, error) {
	// Read array header (12 bytes: 4 for element type, 8 for count)
	header := make([]byte, 12)
	if _, err := r.ReadAt(header, off); err != nil {
		return nil, off, err
	}
	et := binary.LittleEndian.Uint32(header[0:4])
	count := binary.LittleEndian.Uint64(header[4:12])
	if uint64(et) != 4 || uint64(count) > 1<<20 {
		return nil, off, fmt.Errorf("GGUF array too large or wrong element type")
	}
	off += 12

	shape := make([]uint32, count)
	for i := uint64(0); i < count; i++ {
		valBytes := make([]byte, 4)
		if _, err := r.ReadAt(valBytes, off); err != nil {
			return nil, off, err
		}
		shape[i] = binary.LittleEndian.Uint32(valBytes)
		off += 4
	}

	return shape, off, nil
}
