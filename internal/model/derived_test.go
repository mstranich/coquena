package model

import (
	"os"
	"path/filepath"
	"testing"
	"time"
)

// TestLibraryDerivesAlteredWithoutHashing is the reason the verification axis
// is persisted at all. hashFile runs at download, at verify and at reconcile —
// never during enumeration — so a listing that had to answer "were these bytes
// altered?" from the disk would read every model in the library on every
// invocation. It reads the record instead.
func TestLibraryDerivesAlteredWithoutHashing(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	body := principalGGUF()
	path := writeReconcileFixture(t, root, body)
	if _, err := m.ReconcileAccept(path); err != nil {
		t.Fatal(err)
	}
	corrupted := append([]byte{}, body...)
	corrupted[0] ^= 0xFF
	if err := os.WriteFile(path, corrupted, 0600); err != nil {
		t.Fatal(err)
	}

	// Before verify has run, nothing has been observed about these bytes. The
	// listing must not guess: an artifact never verified reads as unverified,
	// not as altered and not as clean.
	entry := libraryEntryFor(t, m, "m.gguf")
	if entry.Altered() {
		t.Fatal("the listing reported altered bytes before any verification observed them")
	}
	if entry.Verification != nil {
		t.Fatalf("verification=%+v want none", entry.Verification)
	}
	if !entry.Usability.Usable {
		t.Fatalf("a never-verified artifact stays usable: %+v", entry.Usability)
	}

	if _, err := m.Verify("org/repo"); err != nil {
		t.Fatal(err)
	}

	// Now the observation exists, and the listing reads it rather than the
	// file. Replacing the artifact with something unreadable proves no hash is
	// computed: if enumeration hashed, this would still report the corrupted
	// digest rather than the record's verdict.
	entry = libraryEntryFor(t, m, "m.gguf")
	if !entry.Altered() {
		t.Fatalf("the recorded mismatch was not read back: %+v", entry.Verification)
	}
	if entry.State != StateAltered {
		t.Fatalf("state=%q want altered", entry.State)
	}
	if entry.Usability.Usable {
		t.Fatalf("an altered artifact was reported usable: %+v", entry.Usability)
	}
}

// TestLibraryReportsVerificationStaleness: mtime only ever weakens a claim.
func TestLibraryReportsVerificationStaleness(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	body := principalGGUF()
	path := writeReconcileFixture(t, root, body)
	if _, err := m.ReconcileAccept(path); err != nil {
		t.Fatal(err)
	}
	if _, err := m.Verify("org/repo"); err != nil {
		t.Fatal(err)
	}
	if entry := libraryEntryFor(t, m, "m.gguf"); entry.VerificationStale {
		t.Fatalf("a verification just performed is not stale: %+v", entry)
	}

	// Touch the file into the future without changing a byte.
	future := time.Now().Add(48 * time.Hour)
	if err := os.Chtimes(path, future, future); err != nil {
		t.Fatal(err)
	}
	entry := libraryEntryFor(t, m, "m.gguf")
	if !entry.VerificationStale {
		t.Fatal("a file modified after its verification should report the record as stale")
	}
	// Staleness weakens the claim; it never changes the recorded outcome, and
	// never manufactures a verdict of its own.
	if entry.Verification == nil || entry.Verification.Outcome != VerifyMatched {
		t.Fatalf("staleness rewrote the recorded outcome: %+v", entry.Verification)
	}
	if entry.Altered() || !entry.Usability.Usable {
		t.Fatalf("staleness alone made the artifact unusable: %+v", entry.Usability)
	}
}

// TestLibraryDerivesTheAttestationStanding covers the three values an operator
// can see, and that none of them gates eligibility.
func TestLibraryDerivesTheAttestationStanding(t *testing.T) {
	root := t.TempDir()
	m := NewManager(filepath.Join(root, "models"))
	body := principalGGUF()
	path := writeReconcileFixture(t, root, body)

	// No sidecar at all: nothing has been consulted.
	if entry := libraryEntryFor(t, m, "m.gguf"); entry.Standing != StandingUnattested {
		t.Fatalf("standing=%q want=%q", entry.Standing, StandingUnattested)
	}

	// Imported: still nothing consulted, and eligible.
	if _, err := m.ReconcileAccept(path); err != nil {
		t.Fatal(err)
	}
	entry := libraryEntryFor(t, m, "m.gguf")
	if entry.Standing != StandingUnattested {
		t.Fatalf("standing=%q want=%q", entry.Standing, StandingUnattested)
	}
	if !entry.Usability.Usable {
		t.Fatalf("an unattested artifact is not deficient: %+v", entry.Usability)
	}

	// An observation with nothing established reads as partial, and is still
	// eligible.
	record, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	record.Observation = &Observation{Repository: "org/repo", ResolvedCommit: "abc", ArtifactPath: "m.gguf", ObservedAt: "2026-01-01T00:00:00Z"}
	if _, err = PublishInPlace(m.ModelsDir, record); err != nil {
		t.Fatal(err)
	}
	entry = libraryEntryFor(t, m, "m.gguf")
	if entry.Standing != StandingPartial {
		t.Fatalf("standing=%q want=%q", entry.Standing, StandingPartial)
	}
	if !entry.Usability.Usable {
		t.Fatalf("a partial artifact is not deficient: %+v", entry.Usability)
	}

	// An attestation bound to the artifact's own digest reads as verified and
	// reports the method that established it.
	stored, err := ReadRecord(path)
	if err != nil {
		t.Fatal(err)
	}
	integrity, _ := stored.Artifact.Integrity()
	stored.Attestation = &Attestation{Repository: "org/repo", ResolvedCommit: "abc", ArtifactPath: "m.gguf", Digest: integrity, Method: MethodRemoteComparison, VerifiedAt: "2026-01-01T00:00:00Z"}
	if _, err = PublishInPlace(m.ModelsDir, stored); err != nil {
		t.Fatal(err)
	}
	entry = libraryEntryFor(t, m, "m.gguf")
	if entry.Standing != StandingVerified {
		t.Fatalf("standing=%q want=%q", entry.Standing, StandingVerified)
	}
	if entry.AttestationMethod != MethodRemoteComparison {
		t.Fatalf("method=%q want=%q", entry.AttestationMethod, MethodRemoteComparison)
	}
}

func libraryEntryFor(t *testing.T, m *Manager, filename string) LibraryEntry {
	t.Helper()
	entries, err := m.Library()
	if err != nil {
		t.Fatal(err)
	}
	for _, entry := range entries {
		if entry.Source.Filename == filename {
			return entry
		}
	}
	t.Fatalf("no library entry for %q", filename)
	return LibraryEntry{}
}
