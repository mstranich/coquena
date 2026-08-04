package model

import (
	"encoding/binary"
	"fmt"
	"io"
	"math"
	"strings"
)

const maxMetadataBytes = int64(64 << 20)

var ggufScalarSizes = map[uint32]int64{0: 1, 1: 1, 2: 2, 3: 2, 4: 4, 5: 4, 6: 4, 7: 1, 10: 8, 11: 8, 12: 8}

type SkippedArray struct {
	ElemType uint32
	Len      int64
}

type TensorDescriptor struct {
	Name       string
	Dimensions []uint64
	Type       GGMLType
	Offset     uint64
}

type GGUFScan struct {
	Metadata map[string]any
	Tensors  []TensorDescriptor
}

func ReadMetadata(r RandomAccessReader) (map[string]any, error) {
	meta, _, _, err := readHeaderAndMetadata(r, false)
	return meta, err
}

func ReadMetadataSkippingArrays(r RandomAccessReader) (map[string]any, error) {
	meta, _, _, err := readHeaderAndMetadata(r, true)
	return meta, err
}

// ScanGGUF is the single validated traversal used by every consumer that needs
// both metadata and tensor descriptors.
func ScanGGUF(r RandomAccessReader, skipArrays bool) (GGUFScan, error) {
	meta, tensorCount, off, err := readHeaderAndMetadata(r, skipArrays)
	if err != nil {
		return GGUFScan{}, err
	}
	if tensorCount > 1<<20 {
		return GGUFScan{}, fmt.Errorf("too many GGUF tensors")
	}
	tensors := make([]TensorDescriptor, 0, tensorCount)
	for i := uint64(0); i < tensorCount; i++ {
		name, next, err := readString(r, off)
		if err != nil {
			return GGUFScan{}, err
		}
		off = next
		b, err := readExact(r, off, 4)
		if err != nil {
			return GGUFScan{}, err
		}
		nDims := binary.LittleEndian.Uint32(b)
		off += 4
		if nDims > 16 {
			return GGUFScan{}, fmt.Errorf("GGUF tensor %q has too many dimensions", name)
		}
		dims := make([]uint64, nDims)
		for j := range dims {
			b, err = readExact(r, off, 8)
			if err != nil {
				return GGUFScan{}, err
			}
			dims[j] = binary.LittleEndian.Uint64(b)
			off += 8
		}
		b, err = readExact(r, off, 12)
		if err != nil {
			return GGUFScan{}, err
		}
		tensors = append(tensors, TensorDescriptor{
			Name: name, Dimensions: dims,
			Type:   GGMLType(binary.LittleEndian.Uint32(b[:4])),
			Offset: binary.LittleEndian.Uint64(b[4:12]),
		})
		off += 12
	}
	return GGUFScan{Metadata: meta, Tensors: tensors}, nil
}

func readHeaderAndMetadata(r RandomAccessReader, skipArrays bool) (map[string]any, uint64, int64, error) {
	if r.Size() < 24 {
		return nil, 0, 0, fmt.Errorf("GGUF is too small")
	}
	h, err := readExact(r, 0, 24)
	if err != nil {
		return nil, 0, 0, err
	}
	if string(h[:4]) != "GGUF" {
		return nil, 0, 0, fmt.Errorf("invalid GGUF magic")
	}
	version := binary.LittleEndian.Uint32(h[4:8])
	if version < 2 || version > 3 {
		return nil, 0, 0, fmt.Errorf("unsupported GGUF version %d", version)
	}
	tensorCount := binary.LittleEndian.Uint64(h[8:16])
	metadataCount := binary.LittleEndian.Uint64(h[16:24])
	if metadataCount > 1<<20 {
		return nil, 0, 0, fmt.Errorf("too many GGUF metadata entries")
	}
	off := int64(24)
	meta := make(map[string]any, metadataCount)
	for i := uint64(0); i < metadataCount; i++ {
		key, next, err := readString(r, off)
		if err != nil {
			return nil, 0, 0, err
		}
		off = next
		b, err := readExact(r, off, 4)
		if err != nil {
			return nil, 0, 0, err
		}
		off += 4
		valueType := binary.LittleEndian.Uint32(b)
		var value any
		if skipArrays && valueType == 9 {
			value, off, err = skipArray(r, off)
		} else {
			value, off, err = readValue(r, off, valueType)
		}
		if err != nil {
			return nil, 0, 0, err
		}
		meta[key] = value
		if off > maxMetadataBytes {
			return nil, 0, 0, fmt.Errorf("GGUF metadata exceeds limit")
		}
	}
	return meta, tensorCount, off, nil
}

func readExact(r RandomAccessReader, off int64, n int) ([]byte, error) {
	if off < 0 || int64(n) > r.Size()-off {
		return nil, io.ErrUnexpectedEOF
	}
	b := make([]byte, n)
	read, err := r.ReadAt(b, off)
	if err != nil && err != io.EOF {
		return nil, err
	}
	if read != len(b) {
		return nil, io.ErrUnexpectedEOF
	}
	return b, nil
}

func readString(r RandomAccessReader, off int64) (string, int64, error) {
	b, err := readExact(r, off, 8)
	if err != nil {
		return "", off, err
	}
	n := binary.LittleEndian.Uint64(b)
	if n > 1<<20 || n > uint64(math.MaxInt64) {
		return "", off, fmt.Errorf("GGUF string is too large")
	}
	b, err = readExact(r, off+8, int(n))
	if err != nil {
		return "", off, err
	}
	return string(b), off + 8 + int64(n), nil
}

func readValue(r RandomAccessReader, off int64, t uint32) (any, int64, error) {
	if t == 8 {
		return readString(r, off)
	}
	if t == 9 {
		b, err := readExact(r, off, 12)
		if err != nil {
			return nil, off, err
		}
		et, n := binary.LittleEndian.Uint32(b), binary.LittleEndian.Uint64(b[4:])
		if n > 1<<20 {
			return nil, off, fmt.Errorf("GGUF array too large")
		}
		values := make([]any, 0, n)
		next := off + 12
		for i := uint64(0); i < n; i++ {
			v, after, err := readValue(r, next, et)
			if err != nil {
				return nil, off, err
			}
			values, next = append(values, v), after
		}
		return values, next, nil
	}
	size, ok := ggufScalarSizes[t]
	if !ok {
		return nil, off, fmt.Errorf("unsupported GGUF value type %d", t)
	}
	b, err := readExact(r, off, int(size))
	if err != nil {
		return nil, off, err
	}
	var value any
	switch t {
	case 0:
		value = b[0]
	case 1:
		value = int8(b[0])
	case 2:
		value = binary.LittleEndian.Uint16(b)
	case 3:
		value = int16(binary.LittleEndian.Uint16(b))
	case 4:
		value = binary.LittleEndian.Uint32(b)
	case 5:
		value = int32(binary.LittleEndian.Uint32(b))
	case 6:
		value = math.Float32frombits(binary.LittleEndian.Uint32(b))
	case 7:
		value = b[0] != 0
	case 10:
		value = binary.LittleEndian.Uint64(b)
	case 11:
		value = int64(binary.LittleEndian.Uint64(b))
	case 12:
		value = math.Float64frombits(binary.LittleEndian.Uint64(b))
	}
	return value, off + size, nil
}

func skipArray(r RandomAccessReader, off int64) (any, int64, error) {
	b, err := readExact(r, off, 12)
	if err != nil {
		return nil, off, err
	}
	et, n := binary.LittleEndian.Uint32(b), binary.LittleEndian.Uint64(b[4:])
	if n > 1<<20 {
		return nil, off, fmt.Errorf("GGUF array too large")
	}
	next := off + 12
	if et == 9 {
		return nil, off, fmt.Errorf("nested GGUF array is not supported")
	}
	if et == 8 {
		for i := uint64(0); i < n; i++ {
			_, after, err := readString(r, next)
			if err != nil {
				return nil, off, err
			}
			next = after
		}
	} else {
		size, ok := ggufScalarSizes[et]
		if !ok {
			return nil, off, fmt.Errorf("unsupported GGUF value type %d", et)
		}
		if n > uint64(math.MaxInt64)/uint64(size) {
			return nil, off, fmt.Errorf("GGUF array size overflows")
		}
		next += int64(n) * size
		if next > r.Size() {
			return nil, off, fmt.Errorf("GGUF array runs past end of file")
		}
	}
	return SkippedArray{ElemType: et, Len: int64(n)}, next, nil
}

func tensorElements(t TensorDescriptor) (uint64, error) {
	if len(t.Dimensions) == 0 {
		return 0, nil
	}
	n := uint64(1)
	for _, dim := range t.Dimensions {
		if dim != 0 && n > math.MaxUint64/dim {
			return 0, fmt.Errorf("GGUF tensor %q parameter count overflows", t.Name)
		}
		n *= dim
	}
	return n, nil
}

func CountParameters(r RandomAccessReader) (int64, error) {
	scan, err := ScanGGUF(r, true)
	if err != nil {
		return 0, err
	}
	var total uint64
	for _, tensor := range scan.Tensors {
		n, err := tensorElements(tensor)
		if err != nil || n > math.MaxInt64 || total > uint64(math.MaxInt64)-n {
			if err != nil {
				return 0, err
			}
			return 0, fmt.Errorf("GGUF parameter count overflows")
		}
		total += n
	}
	return int64(total), nil
}

func isStructuralTensor(name string) bool {
	for _, prefix := range []string{"blk.", "enc.blk.", "dec.blk.", "v.blk.", "a.blk."} {
		if strings.HasPrefix(name, prefix) {
			return true
		}
	}
	return false
}

func PhysicalTensorHistogram(scan GGUFScan) (map[GGMLType]uint64, error) {
	histogram := make(map[GGMLType]uint64)
	for _, tensor := range scan.Tensors {
		if !isStructuralTensor(tensor.Name) {
			continue
		}
		n, err := tensorElements(tensor)
		if err != nil {
			return nil, err
		}
		if math.MaxUint64-histogram[tensor.Type] < n {
			return nil, fmt.Errorf("GGUF tensor histogram overflows")
		}
		histogram[tensor.Type] += n
	}
	return histogram, nil
}

func DominantPhysicalType(scan GGUFScan) (GGMLType, bool, error) {
	histogram, err := PhysicalTensorHistogram(scan)
	if err != nil {
		return 0, false, err
	}
	var dominant GGMLType
	var max uint64
	found := false
	for typ, count := range histogram {
		if !found || count > max || (count == max && typ < dominant) {
			dominant, max, found = typ, count, true
		}
	}
	return dominant, found, nil
}
