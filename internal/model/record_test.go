package model

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// gitBlobOIDOfHello is the Git object ID of a blob holding "hello\n". It is
// reproducible with `printf 'hello\n' | git hash-object --stdin`, which is what
// makes it a witness rather than a value copied from the implementation.
const gitBlobOIDOfHello = "ce013625030ba8dba906f756967f9e9ca394464a"

func TestGitBlobOIDMatchesGitsOwnFraming(t *testing.T) {
	path := filepath.Join(t.TempDir(), "hello.txt")
	if err := os.WriteFile(path, []byte("hello\n"), 0600); err != nil {
		t.Fatal(err)
	}
	oid, err := GitBlobOID(path)
	if err != nil {
		t.Fatal(err)
	}
	if oid != gitBlobOIDOfHello {
		t.Fatalf("git blob oid=%q want=%q; the framing is \"blob \"+length+NUL+bytes, not sha1 of the bytes", oid, gitBlobOIDOfHello)
	}
}

func TestReproducibleAcceptsOnlyDomainsCoquenaComputes(t *testing.T) {
	for _, tc := range []struct {
		name   string
		digest Digest
		want   bool
	}{
		{"raw-file sha256", Digest{Algorithm: AlgorithmSHA256, Domain: DomainRawFile}, true},
		{"git-blob sha1", Digest{Algorithm: AlgorithmSHA1, Domain: DomainGitBlob}, true},
		{"xet-cas sha256", Digest{Algorithm: AlgorithmSHA256, Domain: DomainXetCAS}, false},
		{"git-blob sha256", Digest{Algorithm: AlgorithmSHA256, Domain: DomainGitBlob}, false},
	} {
		if got := tc.digest.Reproducible(); got != tc.want {
			t.Errorf("%s: reproducible=%v want=%v", tc.name, got, tc.want)
		}
	}
}

func TestRepositoryMatchIgnoresAnUnreproducibleDomain(t *testing.T) {
	a := Artifact{}
	a.SetDigest(LocalDigest("abc", "2026-01-01T00:00:00Z"))
	a.SetDigest(PublishedDigest(AlgorithmSHA256, DomainXetCAS, "xet-value"))

	if _, ok := a.RepositoryMatch(); ok {
		t.Fatal("a xet-cas digest must not be returned as accepted repository-match evidence")
	}
	// It is still recorded: reporting "no evidence" when the truth is
	// "evidence Coquena does not compute" is the dishonesty being avoided.
	if _, ok := a.Digest(RoleRepositoryMatch, AlgorithmSHA256, DomainXetCAS); !ok {
		t.Fatal("the unaccepted digest should still be recorded with its domain")
	}
	if err := a.Validate(); err != nil {
		t.Fatalf("an unaccepted domain must not invalidate the sidecar: %v", err)
	}
}

func TestValidateRejectsTwoDigestsForOneTuple(t *testing.T) {
	a := Artifact{Digests: []Digest{
		LocalDigest("one", "2026-01-01T00:00:00Z"),
		LocalDigest("two", "2026-01-02T00:00:00Z"),
	}}
	err := a.Validate()
	if err == nil {
		t.Fatal("two digests sharing a tuple must be invalid rather than first-one-wins")
	}
	if !strings.Contains(err.Error(), string(RoleIntegrity)) {
		t.Fatalf("the error should name the ambiguous tuple, got %v", err)
	}
}

func TestValidateAcceptsBothRolesAtOnce(t *testing.T) {
	// The two roles hold the same value for an LFS-backed artifact. That
	// coincidence is exactly why one unlabelled field survived so long, so it
	// must not read as a duplicate.
	a := Artifact{}
	a.SetDigest(LocalDigest("same", "2026-01-01T00:00:00Z"))
	a.SetDigest(PublishedDigest(AlgorithmSHA256, DomainRawFile, "same"))
	if err := a.Validate(); err != nil {
		t.Fatalf("one digest per role is not a duplicate: %v", err)
	}
	if _, ok := a.RepositoryMatch(); !ok {
		t.Fatal("a raw-file sha256 published digest is accepted evidence")
	}
}

func TestSetDigestReplacesRatherThanAccumulates(t *testing.T) {
	a := Artifact{}
	a.SetDigest(LocalDigest("first", "2026-01-01T00:00:00Z"))
	a.SetDigest(LocalDigest("second", "2026-01-02T00:00:00Z"))
	if len(a.Digests) != 1 {
		t.Fatalf("digests=%d want=1", len(a.Digests))
	}
	if integrity, _ := a.Integrity(); integrity.Value != "second" {
		t.Fatalf("integrity=%q want=%q", integrity.Value, "second")
	}
	if err := a.Validate(); err != nil {
		t.Fatalf("replacing must not create the duplicate it exists to avoid: %v", err)
	}
}

func TestReadRecordRejectsAnyOtherSchemaVersion(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	body := emptyGGUF()
	path := writeReconcileFixture(t, root, body)
	if _, err := m.ReconcileAccept(path); err != nil {
		t.Fatal(err)
	}
	sidecar := strings.TrimSuffix(path, filepath.Ext(path)) + ".json"
	data, err := os.ReadFile(sidecar)
	if err != nil {
		t.Fatal(err)
	}
	var raw map[string]json.RawMessage
	if err = json.Unmarshal(data, &raw); err != nil {
		t.Fatal(err)
	}
	// The immediately preceding version is rejected like any other: there is
	// no "close enough" that gets partially admitted.
	raw["schemaVersion"] = json.RawMessage("3")
	rewritten, err := json.Marshal(raw)
	if err != nil {
		t.Fatal(err)
	}
	if err = os.WriteFile(sidecar, rewritten, 0600); err != nil {
		t.Fatal(err)
	}

	if _, err = ReadRecord(path); err == nil {
		t.Fatal("a sidecar at an earlier schema must be invalid, not translated")
	}
	assertRecoversWithoutTouchingBytes(t, m, path, body)
}

func TestReadRecordRejectsASidecarWithNoIntegrityDigest(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	body := emptyGGUF()
	path := writeReconcileFixture(t, root, body)
	if _, err := m.ReconcileAccept(path); err != nil {
		t.Fatal(err)
	}
	sidecar := strings.TrimSuffix(path, filepath.Ext(path)) + ".json"
	data, err := os.ReadFile(sidecar)
	if err != nil {
		t.Fatal(err)
	}
	var raw map[string]json.RawMessage
	if err = json.Unmarshal(data, &raw); err != nil {
		t.Fatal(err)
	}
	// The sidecar has to be emptied by hand: the write path computes an
	// integrity digest for any record that lacks one, so no Coquena operation
	// can produce this document. That the invariant has to be defeated
	// deliberately is the point of putting it there.
	var artifact map[string]json.RawMessage
	if err = json.Unmarshal(raw["artifact"], &artifact); err != nil {
		t.Fatal(err)
	}
	delete(artifact, "digests")
	if raw["artifact"], err = json.Marshal(artifact); err != nil {
		t.Fatal(err)
	}
	rewritten, err := json.Marshal(raw)
	if err != nil {
		t.Fatal(err)
	}
	if err = os.WriteFile(sidecar, rewritten, 0600); err != nil {
		t.Fatal(err)
	}

	if _, err = ReadRecord(path); err == nil {
		t.Fatal("a sidecar recording no integrity digest must be invalid, not an artifact of unknown integrity")
	}
	// Both invalid sidecars share one recovery path, which is the point of
	// refusing them the same way.
	assertRecoversWithoutTouchingBytes(t, m, path, body)
}

// assertRecoversWithoutTouchingBytes checks that an artifact whose sidecar was
// rejected is restored by importing it, and that recovery never modifies the
// GGUF itself. The model file is the thing that cannot be recomputed; the
// sidecar is derived data, which is what makes rejecting it affordable.
func assertRecoversWithoutTouchingBytes(t *testing.T, m *Manager, path string, body []byte) {
	t.Helper()
	before, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	if _, err = m.ReconcileAccept(path); err != nil {
		t.Fatalf("importing a rejected artifact should restore a current sidecar: %v", err)
	}
	record, err := ReadRecord(path)
	if err != nil {
		t.Fatalf("recovery should leave a readable sidecar: %v", err)
	}
	if record.SchemaVersion != RecordSchemaVersion {
		t.Fatalf("schemaVersion=%d want=%d", record.SchemaVersion, RecordSchemaVersion)
	}
	after, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	if string(before) != string(after) || string(after) != string(body) {
		t.Fatal("recovery must not modify, move or re-fetch the artifact's bytes")
	}
}

func TestObservationDoesNotDisturbAttestationOrOwnership(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	body := emptyGGUF()
	path := writeReconcileFixture(t, root, body)
	if _, err := m.ReconcileAccept(path); err != nil {
		t.Fatal(err)
	}
	record, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	integrity, _ := record.Artifact.Integrity()
	record.Attestation = &Attestation{
		Repository: "org/repo", ResolvedCommit: "deadbeef", ArtifactPath: "m.gguf",
		Digest: integrity, Method: MethodRemoteComparison, VerifiedAt: "2026-01-01T00:00:00Z",
	}
	record.Ownership = &Ownership{Accepted: true, AcceptedAt: "2026-01-01T00:00:00Z"}
	if _, err = PublishInPlace(m.ModelsDir, record); err != nil {
		t.Fatal(err)
	}

	stored, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	stored.Observation = &Observation{
		Repository: "org/repo", ResolvedCommit: "cafebabe", ArtifactPath: "m.gguf",
		ObservedAt: "2026-06-01T00:00:00Z",
	}
	if _, err = PublishInPlace(m.ModelsDir, stored); err != nil {
		t.Fatal(err)
	}

	after, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	if after.Attestation == nil || *after.Attestation != *record.Attestation {
		t.Fatalf("a later observation must not disturb the attestation: %+v", after.Attestation)
	}
	if after.Ownership == nil || *after.Ownership != *record.Ownership {
		t.Fatalf("a later observation must not disturb ownership: %+v", after.Ownership)
	}
	if after.Observation == nil || after.Observation.ResolvedCommit != "cafebabe" {
		t.Fatalf("observation=%+v", after.Observation)
	}
	if after.Standing() != StandingVerified {
		t.Fatalf("standing=%q want=%q; an observation never retracts an attestation", after.Standing(), StandingVerified)
	}
}

func TestExtraSurvivesARewrite(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	body := emptyGGUF()
	path := writeReconcileFixture(t, root, body)
	if _, err := m.ReconcileAccept(path); err != nil {
		t.Fatal(err)
	}
	sidecar := strings.TrimSuffix(path, filepath.Ext(path)) + ".json"
	data, err := os.ReadFile(sidecar)
	if err != nil {
		t.Fatal(err)
	}
	var raw map[string]json.RawMessage
	if err = json.Unmarshal(data, &raw); err != nil {
		t.Fatal(err)
	}
	raw["operatorNote"] = json.RawMessage(`"kept by hand"`)
	rewritten, err := json.Marshal(raw)
	if err != nil {
		t.Fatal(err)
	}
	if err = os.WriteFile(sidecar, rewritten, 0600); err != nil {
		t.Fatal(err)
	}

	record, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	if _, err = PublishInPlace(m.ModelsDir, record); err != nil {
		t.Fatal(err)
	}
	data, err = os.ReadFile(sidecar)
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(data), "kept by hand") {
		t.Fatal("a hand-written annotation should survive an operation that had no reason to discard it")
	}
}

// TestReplacingTheIntegrityDigestRetiresTheAttestation is the mirror of the same
// rule for verification. Both axes claim something about specific bytes, so both
// stop applying when those bytes stop being the recorded ones.
func TestReplacingTheIntegrityDigestRetiresTheAttestation(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	body := emptyGGUF()
	path := writeReconcileFixture(t, root, body)
	if _, err := m.ReconcileAccept(path); err != nil {
		t.Fatal(err)
	}
	record, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	integrity, _ := record.Artifact.Integrity()
	record.Attestation = &Attestation{
		Repository: "org/repo", ResolvedCommit: "revision-a", ArtifactPath: "m.gguf",
		Digest: integrity, Method: MethodRemoteComparison, VerifiedAt: "2026-01-01T00:00:00Z",
	}
	if _, applies := record.AppliedAttestation(); !applies {
		t.Fatal("an attestation bound to the artifact's own digest must apply")
	}
	if record.Standing() != StandingVerified {
		t.Fatalf("standing=%q want=%q", record.Standing(), StandingVerified)
	}

	record.SetIntegrity("0000000000000000000000000000000000000000000000000000000000000000", "2026-06-01T00:00:00Z")

	if _, applies := record.AppliedAttestation(); applies {
		t.Fatal("an attestation bound to a digest the artifact no longer carries must not apply")
	}
	if record.Standing() == StandingVerified {
		t.Fatal("the artifact must not report as attested to a revision its current bytes were never matched against")
	}
}
