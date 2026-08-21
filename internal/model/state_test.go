package model

import (
	"os"
	"path/filepath"
	"testing"
)

// stateRecord builds a record carrying an integrity digest, which every valid
// sidecar has. The axes each test needs are added by the test itself, so what
// produces a given state is visible at the call site rather than buried here.
func stateRecord() Record {
	r := Record{
		SchemaVersion: RecordSchemaVersion,
		Source:        Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "m.gguf", URL: "https://example.test/m.gguf"},
	}
	r.SetDerivation(Derivation{Version: DerivationVersion, Classification: Classification{Type: TypeModel}})
	r.SetIntegrity("aa", "2026-01-01T00:00:00Z")
	return r
}

// attestedAt binds the record to a revision, which is what an attestation does.
func attestedAt(r *Record, revision string) {
	integrity, _ := r.Artifact.Integrity()
	r.Attestation = &Attestation{
		Repository:     r.Source.Repository,
		ResolvedCommit: revision,
		ArtifactPath:   r.Source.Filename,
		Digest:         integrity,
		Method:         MethodRemoteComparison,
		VerifiedAt:     "2026-01-01T00:00:00Z",
	}
}

func observedAt(r *Record, revision string) {
	r.Observation = &Observation{
		Repository:     r.Source.Repository,
		ResolvedCommit: revision,
		ArtifactPath:   r.Source.Filename,
		ObservedAt:     "2026-01-02T00:00:00Z",
	}
}

func TestStateUntrackedWhenNoSidecarCouldBeRead(t *testing.T) {
	if got := stateOf(nil); got != StateUntracked {
		t.Fatalf("state=%q want untracked", got)
	}
}

func TestStateLocalWhenNoRepositoryWasEverConsulted(t *testing.T) {
	r := stateRecord()
	if got := stateOf(&r); got != StateLocal {
		t.Fatalf("state=%q want local", got)
	}
}

func TestStateDetachedWhenARepositoryWasConsultedAndNothingMatched(t *testing.T) {
	r := stateRecord()
	observedAt(&r, "def")
	if got := stateOf(&r); got != StateDetached {
		t.Fatalf("state=%q want detached", got)
	}
}

func TestStateSyncedWhenTheAttestationBindsTheObservedRevision(t *testing.T) {
	r := stateRecord()
	attestedAt(&r, "abc")
	observedAt(&r, "abc")
	if got := stateOf(&r); got != StateSynced {
		t.Fatalf("state=%q want synced", got)
	}
}

// A downloaded artifact is attested by construction and carries no separate
// observation. The revision its attestation names is the most recent thing
// Coquena learned from the repository, so it matches.
func TestStateSyncedWhenAttestedWithNoLaterObservation(t *testing.T) {
	r := stateRecord()
	attestedAt(&r, "abc")
	if got := stateOf(&r); got != StateSynced {
		t.Fatalf("state=%q want synced", got)
	}
}

func TestStateUpgradableWhenTheRepositoryMovedPastTheBoundRevision(t *testing.T) {
	r := stateRecord()
	attestedAt(&r, "abc")
	observedAt(&r, "def")
	if got := stateOf(&r); got != StateUpgradable {
		t.Fatalf("state=%q want upgradable", got)
	}
}

// The cascade's order is the point of these two, not the individual answers.

// Altered is asked before anything about the repository. An artifact whose bytes
// were found altered may carry a perfectly valid attestation, and reporting it
// as synced would describe bytes that are no longer there.
func TestStateAlteredOutranksEveryProvenanceState(t *testing.T) {
	r := stateRecord()
	attestedAt(&r, "abc")
	observedAt(&r, "abc")
	r.Verification = &Verification{Outcome: VerifyMismatched, CheckedAt: "2026-01-03T00:00:00Z", AgainstDigest: "aa"}
	if !r.Altered() {
		t.Fatal("fixture does not report altered bytes, so the ordering is not under test")
	}
	if got := stateOf(&r); got != StateAltered {
		t.Fatalf("state=%q want altered", got)
	}
}

// The incomplete gate holds its place in the cascade and nothing reaches it.
// When address-quantization-bundles supplies the predicate, this test is what
// says where the answer belongs.
func TestStateIncompleteIsUnreachableUntilBundlesExist(t *testing.T) {
	r := stateRecord()
	if principalIsIncomplete(&r) {
		t.Fatal("a predicate that cannot be evaluated reported true")
	}
}

func TestUsableIsDerivedFromTheStateAlone(t *testing.T) {
	usable := map[ArtifactState]bool{
		StateUntracked:  false,
		StateAltered:    false,
		StateIncomplete: false,
		StateLocal:      true,
		StateDetached:   true,
		StateSynced:     true,
		StateUpgradable: true,
	}
	for state, want := range usable {
		if state.Usable() != want {
			t.Fatalf("state %q usable=%v want %v", state, state.Usable(), want)
		}
	}
}

// Enumerating a library derives every state without reading a single artifact's
// bytes. The proof is destructive: the files are replaced with something that
// cannot be opened as GGUF after their sidecars exist. If enumeration hashed or
// parsed anything, the states would change or the walk would fail.
func TestEnumerationDerivesStatesWithoutReadingArtifactBytes(t *testing.T) {
	models := t.TempDir()
	managed := installArtifact(t, models, "org/repo/m.gguf", principalGGUF())
	installArtifact(t, models, "org/repo/mmproj.gguf", projectorGGUF())
	writeArtifact(t, models, "other/repo/foreign.gguf", principalGGUF())

	for _, path := range []string{managed, filepath.Join(models, "org", "repo", "mmproj.gguf")} {
		if err := os.WriteFile(path, []byte("no longer a GGUF"), 0600); err != nil {
			t.Fatal(err)
		}
	}

	entries := entriesByFilename(t, models)
	if got := entries["m.gguf"].State; got != StateLocal {
		t.Fatalf("m.gguf state=%q want local; enumeration read the artifact", got)
	}
	if got := entries["mmproj.gguf"].State; got != StateLocal {
		t.Fatalf("mmproj.gguf state=%q want local; enumeration read the artifact", got)
	}
	if got := entries["foreign.gguf"].State; got != StateUntracked {
		t.Fatalf("foreign.gguf state=%q want untracked", got)
	}
}

// A download in flight and an artifact on disk report in different fields. They
// were one string until this change, which is why every consumer had to
// discriminate the two vocabularies before it could use either.
func TestLibrarySeparatesJobStatusFromArtifactState(t *testing.T) {
	models := t.TempDir()
	m := NewManager(models)
	installArtifact(t, models, "org/repo/m.gguf", principalGGUF())
	if _, err := m.Create(Candidate{
		Source:   Source{Provider: "huggingface", Repository: "org/repo", RequestedRevision: "main", ResolvedRevision: "abc", Filename: "pending.gguf", URL: "https://example.test/pending.gguf"},
		Artifact: Artifact{SizeBytes: int64(len(principalGGUF()))},
	}); err != nil {
		t.Fatal(err)
	}

	entries, err := m.Library()
	if err != nil {
		t.Fatal(err)
	}
	byName := map[string]LibraryEntry{}
	for _, entry := range entries {
		byName[entry.Source.Filename] = entry
	}

	pending, ok := byName["pending.gguf"]
	if !ok {
		t.Fatal("the in-flight job was not enumerated")
	}
	if pending.JobStatus != Pending {
		t.Fatalf("job status=%q want pending", pending.JobStatus)
	}
	if pending.State != "" {
		t.Fatalf("a transfer that has not finished reported artifact state %q", pending.State)
	}

	settled := byName["m.gguf"]
	if settled.State != StateLocal {
		t.Fatalf("artifact state=%q want local", settled.State)
	}
	if settled.JobStatus != "" {
		t.Fatalf("an artifact on disk reported job status %q", settled.JobStatus)
	}
}
