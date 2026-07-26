package model

import (
	"encoding/binary"
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
