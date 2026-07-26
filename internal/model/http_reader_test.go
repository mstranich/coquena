package model

import (
	"fmt"
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestHTTPReaderUsesRanges(t *testing.T) {
	data := []byte("abcdef")
	s := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", "6")
			w.Header().Set("ETag", "x")
			return
		}
		if r.Header.Get("Range") != "bytes=1-5" {
			t.Errorf("range=%q", r.Header.Get("Range"))
		}
		w.WriteHeader(206)
		_, _ = w.Write(data[1:4])
	}))
	defer s.Close()
	r, e := OpenHTTP(s.Client(), s.URL)
	if e != nil {
		t.Fatal(e)
	}
	b := make([]byte, 3)
	_, e = r.ReadAt(b, 1)
	if e != nil || string(b) != "bcd" {
		t.Fatalf("%q %v", b, e)
	}
}
func TestHTTPReaderRejectsNonRangeResponse(t *testing.T) {
	s := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", "4")
			return
		}
		_, _ = w.Write([]byte("full"))
	}))
	defer s.Close()
	r, err := OpenHTTP(s.Client(), s.URL)
	if err != nil {
		t.Fatal(err)
	}
	if _, err = r.ReadAt(make([]byte, 1), 0); err == nil {
		t.Fatal("full response accepted")
	}
}
func TestHTTPReaderBoundsFinalRange(t *testing.T) {
	s := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", "3")
			return
		}
		if r.Header.Get("Range") != "bytes=2-2" {
			t.Fatalf("range=%q", r.Header.Get("Range"))
		}
		w.WriteHeader(http.StatusPartialContent)
		_, _ = w.Write([]byte("c"))
	}))
	defer s.Close()
	r, err := OpenHTTP(s.Client(), s.URL)
	if err != nil {
		t.Fatal(err)
	}
	b := make([]byte, 2)
	n, err := r.ReadAt(b, 2)
	if n != 1 || err == nil {
		t.Fatalf("n=%d err=%v", n, err)
	}
}
func TestHTTPReaderCachesAdjacentReads(t *testing.T) {
	requests := 0
	data := []byte("abcdef")
	s := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", fmt.Sprint(len(data)))
			return
		}
		requests++
		w.WriteHeader(http.StatusPartialContent)
		_, _ = w.Write(data)
	}))
	defer s.Close()
	reader, err := OpenHTTP(s.Client(), s.URL)
	if err != nil {
		t.Fatal(err)
	}
	for offset := int64(0); offset < 3; offset++ {
		b := make([]byte, 1)
		if _, err = reader.ReadAt(b, offset); err != nil {
			t.Fatal(err)
		}
	}
	if requests != 1 {
		t.Fatalf("range requests=%d", requests)
	}
}
