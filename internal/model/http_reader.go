package model

import (
	"fmt"
	"io"
	"net/http"
	"strconv"
	"strings"
	"sync"
)

const httpReadAheadBytes = int64(1 << 20)

type HTTPReader struct {
	client             *http.Client
	url                string
	size               int64
	etag, lastModified string
	mu                 sync.Mutex
	cacheStart         int64
	cache              []byte
}

func OpenHTTP(client *http.Client, url string) (*HTTPReader, error) {
	if client == nil {
		client = http.DefaultClient
	}
	req, e := http.NewRequest(http.MethodHead, url, nil)
	if e != nil {
		return nil, e
	}
	resp, e := client.Do(req)
	if e != nil {
		return nil, e
	}
	defer resp.Body.Close()
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return nil, fmt.Errorf("inspect remote GGUF: %s", resp.Status)
	}
	n, e := strconv.ParseInt(resp.Header.Get("Content-Length"), 10, 64)
	if e != nil || n < 0 {
		return nil, fmt.Errorf("remote GGUF has no valid Content-Length")
	}
	return &HTTPReader{client: client, url: url, size: n, etag: resp.Header.Get("ETag"), lastModified: resp.Header.Get("Last-Modified")}, nil
}
func (r *HTTPReader) Size() int64          { return r.size }
func (r *HTTPReader) ETag() string         { return r.etag }
func (r *HTTPReader) LastModified() string { return r.lastModified }
func (r *HTTPReader) ReadAt(p []byte, off int64) (int, error) {
	r.mu.Lock()
	defer r.mu.Unlock()
	if off < 0 || off >= r.size {
		return 0, io.EOF
	}
	if off >= r.cacheStart && off+int64(len(p)) <= r.cacheStart+int64(len(r.cache)) {
		n := copy(p, r.cache[off-r.cacheStart:])
		return n, nil
	}
	length := int64(len(p))
	if length < httpReadAheadBytes {
		length = httpReadAheadBytes
	}
	end := off + length - 1
	if end >= r.size {
		end = r.size - 1
	}
	q, e := http.NewRequest(http.MethodGet, r.url, nil)
	if e != nil {
		return 0, e
	}
	q.Header.Set("Range", fmt.Sprintf("bytes=%d-%d", off, end))
	resp, e := r.client.Do(q)
	if e != nil {
		return 0, e
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusPartialContent {
		return 0, fmt.Errorf("remote GGUF does not support byte ranges: %s", resp.Status)
	}
	data, e := io.ReadAll(io.LimitReader(resp.Body, end-off+1))
	if e != nil {
		return 0, e
	}
	r.cacheStart, r.cache = off, data
	n := copy(p, data)
	if n < len(p) {
		return n, io.EOF
	}
	return n, nil
}
func isGGUF(name string) bool { return strings.HasSuffix(strings.ToLower(name), ".gguf") }
