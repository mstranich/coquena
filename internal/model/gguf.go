package model

import (
	"encoding/binary"
	"fmt"
	"io"
	"math"
)

const maxMetadataBytes = int64(64 << 20)

func ReadMetadata(r RandomAccessReader) (map[string]any, error) {
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
		value, next, err := readValue(r, off, binary.LittleEndian.Uint32(t))
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
	sizes := map[uint32]int64{0: 1, 1: 1, 2: 2, 3: 2, 4: 4, 5: 4, 6: 4, 7: 1, 10: 8, 11: 8, 12: 8}
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
func math32(b []byte) float32 { return math.Float32frombits(binary.LittleEndian.Uint32(b)) }
func math64(b []byte) float64 { return math.Float64frombits(binary.LittleEndian.Uint64(b)) }
