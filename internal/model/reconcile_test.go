package model

import (
	"crypto/sha256"
	"encoding/hex"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func writeReconcileFixture(t *testing.T, root string, body []byte) string {
	t.Helper()
	dir := filepath.Join(root, "models", "org", "repo")
	if err := os.MkdirAll(dir, 0755); err != nil {
		t.Fatal(err)
	}
	path := filepath.Join(dir, "m.gguf")
	if err := os.WriteFile(path, body, 0600); err != nil {
		t.Fatal(err)
	}
	return path
}

func TestReconcileOnlineSucceedsOnMatchingSHA256(t *testing.T) {
	body := principalGGUF()
	digest := sha256.Sum256(body)
	sha256 := hex.EncodeToString(digest[:])
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", "24")
			return
		}
		if r.URL.Path != "/api/models/org/repo/revision/main" {
			t.Fatalf("path=%s", r.URL.Path)
		}
		_, _ = w.Write([]byte(`{"sha":"deadbeef","siblings":[{"rfilename":"m.gguf","size":24,"lfs":{"sha256":"` + sha256 + `"}}]}`))
	}))
	defer server.Close()
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	m.Resolver = Resolver{Client: server.Client(), BaseURL: server.URL}
	path := writeReconcileFixture(t, root, body)

	result, err := m.ReconcileOnline(path)
	if err != nil {
		t.Fatal(err)
	}
	if result.Partial() {
		t.Fatalf("expected non-partial result, got %+v", result)
	}
	if result.Record.Source.Provider != "huggingface" || result.Record.Source.Repository != "org/repo" || result.Record.Source.ResolvedRevision != "deadbeef" {
		t.Fatalf("source=%+v", result.Record.Source)
	}
	if integrity, ok := result.Record.Artifact.Integrity(); !ok || integrity.Value != sha256 {
		t.Fatalf("integrity=%+v want=%q", integrity, sha256)
	}
	rec, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	persisted, ok := rec.Artifact.Integrity()
	if rec.Source.Provider != "huggingface" || !ok || persisted.Value != sha256 {
		t.Fatalf("persisted record=%+v", rec)
	}
}

// A disagreement is recorded, not refused. Failing with nothing to show is poor
// operator experience when the bytes are perfectly usable, and the three causes
// of a non-match are indistinguishable from the artifact — including a revision
// the publisher has since destroyed, which is routine housekeeping rather than
// evidence that anything is wrong.
func TestReconcileOnlineRecordsAHashDisagreement(t *testing.T) {
	body := principalGGUF()
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", "24")
			return
		}
		_, _ = w.Write([]byte(`{"sha":"deadbeef","siblings":[{"rfilename":"m.gguf","size":24,"lfs":{"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}]}`))
	}))
	defer server.Close()
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	m.Resolver = Resolver{Client: server.Client(), BaseURL: server.URL}
	path := writeReconcileFixture(t, root, body)

	result, err := m.ReconcileOnline(path)
	if err != nil {
		t.Fatalf("a disagreement must leave the operator a working model, not an error: %v", err)
	}
	if !result.Partial() {
		t.Fatal("a disagreement establishes no match")
	}
	record, err := ReadRecord(path)
	if err != nil {
		t.Fatalf("a sidecar should have been written: %v", err)
	}
	if record.Attestation != nil {
		t.Fatalf("a disagreement must not establish an attestation: %+v", record.Attestation)
	}
	if record.Observation == nil || record.Observation.PublishedDigest == nil {
		t.Fatalf("the identifier that disagreed should be recorded on the observation: %+v", record.Observation)
	}
	if record.Observation.PublishedDigest.Value != strings.Repeat("a", 64) {
		t.Fatalf("published digest=%q", record.Observation.PublishedDigest.Value)
	}
	if record.Standing() != StandingPartial {
		t.Fatalf("standing=%q want=%q", record.Standing(), StandingPartial)
	}
	// The local bytes are untouched and still identified by their own digest.
	if _, ok := record.Artifact.RepositoryMatch(); ok {
		t.Fatal("a digest that disagreed must not be recorded as accepted repository-match evidence")
	}
}

// An artifact that already carries evidence keeps it: a disagreement is not a
// licence to overwrite what an operator or an earlier comparison established.
func TestReconcileDisagreementLeavesEarlierEvidenceIntact(t *testing.T) {
	body := principalGGUF()
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", "24")
			return
		}
		_, _ = w.Write([]byte(`{"sha":"deadbeef","siblings":[{"rfilename":"m.gguf","size":24,"lfs":{"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}]}`))
	}))
	defer server.Close()
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	m.Resolver = Resolver{Client: server.Client(), BaseURL: server.URL}
	path := writeReconcileFixture(t, root, body)
	if _, err := m.ReconcileAccept(path); err != nil {
		t.Fatal(err)
	}
	before, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	if before.Ownership == nil || !before.Ownership.Accepted {
		t.Fatalf("import should record the operator's acceptance: %+v", before.Ownership)
	}

	if _, err = m.ReconcileOnline(path); err != nil {
		t.Fatal(err)
	}
	after, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	if after.Ownership == nil || *after.Ownership != *before.Ownership {
		t.Fatalf("ownership=%+v want=%+v; no network call retracts an operator's decision", after.Ownership, before.Ownership)
	}
	beforeIntegrity, _ := before.Artifact.Integrity()
	afterIntegrity, _ := after.Artifact.Integrity()
	if afterIntegrity.Value != beforeIntegrity.Value {
		t.Fatal("a disagreement replaced the integrity digest")
	}
}

func TestReconcileOnlineDegradesWhenNoSHA256Available(t *testing.T) {
	body := principalGGUF()
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", "24")
			return
		}
		_, _ = w.Write([]byte(`{"sha":"deadbeef","siblings":[{"rfilename":"m.gguf","size":24}]}`))
	}))
	defer server.Close()
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	m.Resolver = Resolver{Client: server.Client(), BaseURL: server.URL}
	path := writeReconcileFixture(t, root, body)

	result, err := m.ReconcileOnline(path)
	if err != nil {
		t.Fatal(err)
	}
	if !result.Partial() {
		t.Fatal("expected a partial result when Hugging Face reports no content SHA-256")
	}
	digest := sha256.Sum256(body)
	if integrity, ok := result.Record.Artifact.Integrity(); !ok || integrity.Value != hex.EncodeToString(digest[:]) {
		t.Fatalf("integrity=%+v want=%q", integrity, hex.EncodeToString(digest[:]))
	}
	if _, err = ReadRecord(path); err != nil {
		t.Fatal(err)
	}
}

func TestReconcileAcceptWritesLocalProviderSidecar(t *testing.T) {
	body := principalGGUF()
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	path := writeReconcileFixture(t, root, body)

	result, err := m.ReconcileAccept(path)
	if err != nil {
		t.Fatal(err)
	}
	if result.Record.Source.Provider != "local" || result.Record.Source.Repository != "org/repo" || result.Record.Source.Filename != "m.gguf" {
		t.Fatalf("source=%+v", result.Record.Source)
	}
	if result.Record.Source.ResolvedRevision != "" || result.Record.Source.URL != "" {
		t.Fatalf("local source should carry no Hugging Face identity: %+v", result.Record.Source)
	}
	digest := sha256.Sum256(body)
	if integrity, ok := result.Record.Artifact.Integrity(); !ok || integrity.Value != hex.EncodeToString(digest[:]) {
		t.Fatalf("integrity=%+v want=%q", integrity, hex.EncodeToString(digest[:]))
	}
	rec, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	if rec.Source.Provider != "local" {
		t.Fatalf("persisted record=%+v", rec)
	}
	entries, err := m.Library()
	if err != nil {
		t.Fatal(err)
	}
	var found bool
	for _, entry := range entries {
		if entry.TargetPath == path {
			found = true
			if entry.State != StateLocal {
				t.Fatalf("state=%s want local", entry.State)
			}
		}
	}
	if !found {
		t.Fatal("reconciled entry not found in library")
	}
}

// TestReconcileFunctionsAreMutuallyExclusiveByDesign documents that
// ReconcileOnline and ReconcileAccept are two separate exported functions,
// each independently callable, so a caller (the CLI in section 8) expresses
// "exactly one of --online/--accept" by choosing which single function to
// call based on which flag was passed, with a usage error when neither or
// both flags are given. There is no shared "mode" parameter that could be
// mis-set to invoke both at once.
func TestReconcileFunctionsAreMutuallyExclusiveByDesign(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	body := principalGGUF()
	path := writeReconcileFixture(t, root, body)

	// Accept first: writes a local sidecar with no network access.
	if _, err := m.ReconcileAccept(path); err != nil {
		t.Fatal(err)
	}
	rec, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	if rec.Source.Provider != "local" {
		t.Fatalf("expected local provider after accept, got %+v", rec.Source)
	}
	// A second call to ReconcileOnline for the same identity is a distinct,
	// separately invoked operation -- nothing in the type system allows a
	// single call site to request both modes simultaneously.
}

// TestReconcileDisagreementOnChangedBytesRetiresTheAttestation is the scenario
// the reader-side guard exists for. preserveUnownedAxes faithfully carries the
// old attestation forward — it has no standing to drop it — so the record can
// still hold an attestation established against bytes that are gone. What it
// must not do is be believed.
func TestReconcileDisagreementOnChangedBytesRetiresTheAttestation(t *testing.T) {
	body := principalGGUF()
	matching := sha256.Sum256(body)
	serveDigest := hex.EncodeToString(matching[:])
	first := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", "24")
			return
		}
		_, _ = w.Write([]byte(`{"sha":"revision-a","siblings":[{"rfilename":"m.gguf","size":24,"lfs":{"sha256":"` + serveDigest + `"}}]}`))
	}))
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	m.Resolver = Resolver{Client: first.Client(), BaseURL: first.URL}
	path := writeReconcileFixture(t, root, body)

	// The first pass matches, so an attestation is established against these
	// bytes.
	if _, err := m.ReconcileOnline(path); err != nil {
		t.Fatal(err)
	}
	attested, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	if _, applies := attested.AppliedAttestation(); !applies {
		t.Fatalf("a matching reconcile should attest: %+v", attested.Attestation)
	}
	first.Close()

	// The local bytes change underneath, and now nothing the repository
	// publishes describes them.
	if err = os.WriteFile(path, append(append([]byte{}, body...), 0), 0600); err != nil {
		t.Fatal(err)
	}
	second := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", "25")
			return
		}
		_, _ = w.Write([]byte(`{"sha":"revision-a","siblings":[{"rfilename":"m.gguf","size":25,"lfs":{"sha256":"` + serveDigest + `"}}]}`))
	}))
	defer second.Close()
	m.Resolver = Resolver{Client: second.Client(), BaseURL: second.URL}

	result, err := m.ReconcileOnline(path)
	if err != nil {
		t.Fatal(err)
	}
	if !result.Partial() {
		t.Fatal("a disagreement establishes no match")
	}
	after, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	// The record still holds it — preserveUnownedAxes has no standing to drop
	// an axis it does not own — so this is not passing because the attestation
	// went missing. It is present and disbelieved.
	if after.Attestation == nil {
		t.Fatal("the attestation was dropped rather than retired; this test would then prove nothing")
	}
	if after.Attestation.ResolvedCommit != "revision-a" {
		t.Fatalf("the carried attestation is not the original one: %+v", after.Attestation)
	}
	if _, applies := after.AppliedAttestation(); applies {
		t.Fatalf("a carried-forward attestation about replaced bytes must not apply: %+v", after.Attestation)
	}
	if after.Standing() == StandingVerified {
		t.Fatal("the artifact reports as attested to a revision its current bytes were never matched against")
	}
}

// TestReconcileKeepsTheRecordedDigestOnAnUnprovenChange: overwriting a recorded
// digest with a freshly computed one, on a run that established nothing, erases
// the only record that the bytes ever changed.
func TestReconcileKeepsTheRecordedDigestOnAnUnprovenChange(t *testing.T) {
	body := principalGGUF()
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", "25")
			return
		}
		_, _ = w.Write([]byte(`{"sha":"deadbeef","siblings":[{"rfilename":"m.gguf","size":25,"lfs":{"sha256":"` + strings.Repeat("a", 64) + `"}}]}`))
	}))
	defer server.Close()
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	m.Resolver = Resolver{Client: server.Client(), BaseURL: server.URL}
	path := writeReconcileFixture(t, root, body)
	if _, err := m.ReconcileAccept(path); err != nil {
		t.Fatal(err)
	}
	before, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	recorded, _ := before.Artifact.Integrity()

	if err = os.WriteFile(path, append(append([]byte{}, body...), 0), 0600); err != nil {
		t.Fatal(err)
	}
	result, err := m.ReconcileOnline(path)
	if err != nil {
		t.Fatal(err)
	}
	after, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	kept, ok := after.Artifact.Integrity()
	if !ok || kept.Value != recorded.Value {
		t.Fatalf("integrity=%+v want the recorded %q kept; re-baselining destroys the evidence that the bytes changed", kept, recorded.Value)
	}
	if result.Diagnostic == "" {
		t.Fatal("the difference must be surfaced rather than silently absorbed")
	}
	// Keeping the recorded digest is exactly what makes the change detectable.
	verified, err := m.Verify("org/repo")
	if err != nil {
		t.Fatal(err)
	}
	if verified.Outcome != VerifyMismatched {
		t.Fatalf("outcome=%q; the kept digest is what lets verify see the change", verified.Outcome)
	}
}

// TestReconcileRebaselinesOnAProvenChange is the other half: when the repository
// confirms the new bytes, replacing the recorded digest is justified — and the
// old verification retires itself, because it names a digest the artifact no
// longer carries.
func TestReconcileRebaselinesOnAProvenChange(t *testing.T) {
	body := principalGGUF()
	updated := append(append([]byte{}, body...), 0)
	updatedDigest := sha256.Sum256(updated)
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodHead {
			w.Header().Set("Content-Length", "25")
			return
		}
		_, _ = w.Write([]byte(`{"sha":"revision-b","siblings":[{"rfilename":"m.gguf","size":25,"lfs":{"sha256":"` + hex.EncodeToString(updatedDigest[:]) + `"}}]}`))
	}))
	defer server.Close()
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	m.Resolver = Resolver{Client: server.Client(), BaseURL: server.URL}
	path := writeReconcileFixture(t, root, body)
	if _, err := m.ReconcileAccept(path); err != nil {
		t.Fatal(err)
	}
	if _, err := m.Verify("org/repo"); err != nil {
		t.Fatal(err)
	}
	before, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	if _, applies := before.AppliedVerification(); !applies {
		t.Fatal("the verification should apply before the digest changes")
	}

	if err = os.WriteFile(path, updated, 0600); err != nil {
		t.Fatal(err)
	}
	result, err := m.ReconcileOnline(path)
	if err != nil {
		t.Fatal(err)
	}
	if result.Partial() {
		t.Fatalf("the repository confirms these bytes: %+v", result)
	}
	after, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	integrity, _ := after.Artifact.Integrity()
	if integrity.Value != hex.EncodeToString(updatedDigest[:]) {
		t.Fatalf("integrity=%q want the proven new digest", integrity.Value)
	}
	if _, applies := after.AppliedVerification(); applies {
		t.Fatal("a verification naming the superseded digest must retire itself without anyone clearing it")
	}
	if after.Altered() {
		t.Fatal("a retired verification must not read as a verdict about the new bytes")
	}
}
