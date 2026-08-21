package model

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"testing"
)

// TestResumeStillSendsIfRangeFromTheJobsETag pins the reason the entity tag was
// kept on the wire rather than deleted outright: a resumable download needs an
// opaque validator, and If-Range is where it is used.
func TestResumeStillSendsIfRangeFromTheJobsETag(t *testing.T) {
	body := principalGGUF()
	var sentIfRange string
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", strconv.Itoa(len(principalGGUF())))
			w.Header().Set("ETag", `"transport-only"`)
			return
		}
		sentIfRange = r.Header.Get("If-Range")
		w.Header().Set("ETag", `"transport-only"`)
		w.Header().Set("Content-Range", "bytes 4-23/24")
		w.WriteHeader(http.StatusPartialContent)
		_, _ = w.Write(body[4:])
	}))
	defer server.Close()

	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	c := Candidate{
		Source:   Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: server.URL},
		Artifact: Artifact{SizeBytes: int64(len(principalGGUF()))},
		ETag:     `"transport-only"`,
	}
	j, err := m.Create(c)
	if err != nil {
		t.Fatal(err)
	}
	if j.ETag != `"transport-only"` {
		t.Fatalf("job etag=%q; the candidate's transport validator must reach the job", j.ETag)
	}
	if err = os.WriteFile(j.PartialPath, body[:4], 0600); err != nil {
		t.Fatal(err)
	}
	j.DownloadedBytes = 4
	j.Status = Paused
	if err = m.save(j); err != nil {
		t.Fatal(err)
	}
	if _, err = m.Resume(context.Background(), j.ID, nil); err != nil {
		t.Fatal(err)
	}
	if sentIfRange != `"transport-only"` {
		t.Fatalf("If-Range=%q want=%q", sentIfRange, `"transport-only"`)
	}
}

// TestNoSidecarCarriesATransportEntityTag is the whole point of moving the
// field: an entity tag's meaning depends on which hop answered, so it must not
// reach a document that records what is known about an artifact's bytes.
func TestNoSidecarCarriesATransportEntityTag(t *testing.T) {
	body := principalGGUF()
	digest := sha256.Sum256(body)
	// A Hugging Face artifact's final-hop ETag is the Xet CAS hash: a SHA-256
	// over chunked content, which looks exactly like a digest Coquena could
	// compare and is not one.
	xetLookalike := strings.Repeat("f", 64)
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("ETag", xetLookalike)
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", strconv.Itoa(len(principalGGUF())))
			return
		}
		_, _ = w.Write(body)
	}))
	defer server.Close()

	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	c := Candidate{
		Source:   Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: server.URL},
		Artifact: Artifact{SizeBytes: int64(len(principalGGUF()))},
		ETag:     xetLookalike,
	}
	j, err := m.Create(c)
	if err != nil {
		t.Fatal(err)
	}
	if _, err = m.Resume(context.Background(), j.ID, nil); err != nil {
		t.Fatal(err)
	}

	target := filepath.Join(root, "models", "org", "repo", "m.gguf")
	raw, err := os.ReadFile(strings.TrimSuffix(target, ".gguf") + ".json")
	if err != nil {
		t.Fatal(err)
	}
	if strings.Contains(strings.ToLower(string(raw)), "etag") {
		t.Fatalf("the sidecar records a transport entity tag: %s", raw)
	}
	if strings.Contains(string(raw), xetLookalike) {
		t.Fatalf("the entity tag's value reached the sidecar: %s", raw)
	}

	record, err := ReadRecord(target)
	if err != nil {
		t.Fatal(err)
	}
	for _, d := range record.Artifact.Digests {
		if d.Value == xetLookalike {
			t.Fatalf("a %s digest was populated from a transport header", d.Role)
		}
	}
	if integrity, ok := record.Artifact.Integrity(); !ok || integrity.Value != hex.EncodeToString(digest[:]) {
		t.Fatalf("integrity=%+v want the digest of the received bytes %q", integrity, hex.EncodeToString(digest[:]))
	}
}

// TestDownloadIsAttestedByConstruction pins RFC F2: bytes fetched from a
// revision-pinned URL have provenance from where they came from, not from a
// comparison. Recording such a download as unattested would understate what
// Coquena knows.
func TestDownloadIsAttestedByConstruction(t *testing.T) {
	body := principalGGUF()
	published := strings.Repeat("b", 64)
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", strconv.Itoa(len(principalGGUF())))
			return
		}
		_, _ = w.Write(body)
	}))
	defer server.Close()

	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	c := Candidate{
		Source:        Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: server.URL},
		Artifact:      Artifact{SizeBytes: int64(len(principalGGUF()))},
		ContentSHA256: published,
	}
	j, err := m.Create(c)
	if err != nil {
		t.Fatal(err)
	}
	if _, err = m.Resume(context.Background(), j.ID, nil); err != nil {
		t.Fatal(err)
	}

	record, err := ReadRecord(filepath.Join(root, "models", "org", "repo", "m.gguf"))
	if err != nil {
		t.Fatal(err)
	}
	if record.Attestation == nil {
		t.Fatal("a managed download must be attested")
	}
	if record.Attestation.Method != MethodDownload {
		t.Fatalf("method=%q want=%q", record.Attestation.Method, MethodDownload)
	}
	if record.Attestation.ResolvedCommit != "abc" || record.Attestation.Repository != "org/repo" || record.Attestation.ArtifactPath != "m.gguf" {
		t.Fatalf("attestation is not bound to the revision it came from: %+v", record.Attestation)
	}
	if record.Standing() != StandingVerified {
		t.Fatalf("standing=%q want=%q", record.Standing(), StandingVerified)
	}
	// The published hash is observation evidence only. Nothing compared the
	// received bytes against it, so it must not be recorded as an accepted
	// repository match — asserting a comparison that did not happen is the
	// defect this record exists to prevent.
	if record.Observation == nil || record.Observation.PublishedDigest == nil || record.Observation.PublishedDigest.Value != published {
		t.Fatalf("observation=%+v", record.Observation)
	}
	if _, ok := record.Artifact.RepositoryMatch(); ok {
		t.Fatal("no comparison was performed, so no repository-match digest may be recorded")
	}
}
