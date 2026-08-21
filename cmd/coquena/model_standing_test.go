package main

import (
	"bytes"
	"os"
	"path/filepath"
	"strings"
	"testing"

	modelapp "github.com/mstranich/coquena/internal/model"
)

// An artifact whose recorded verification found altered bytes is ineligible,
// and a caller can tell that apart from an artifact that simply has no sidecar.
// The two have different remedies: re-download or re-reconcile one, reconcile
// the other.
func TestRunModelListDistinguishesAlteredFromUnreconciled(t *testing.T) {
	m := withModelManager(t)
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	publishArtifact(t, m, "altered.gguf", cliPrincipalGGUF())

	// Corrupt the bytes and let verify record what it found.
	target := filepath.Join(m.ModelsDir, "org", "repo", "altered.gguf")
	corrupted := append([]byte{}, cliPrincipalGGUF()...)
	corrupted[0] ^= 0xFF
	if err := os.WriteFile(target, corrupted, 0600); err != nil {
		t.Fatal(err)
	}
	if _, err := m.Verify("org/repo:altered.gguf"); err != nil {
		t.Fatal(err)
	}

	// A foreign file with no sidecar at all.
	foreign := filepath.Join(m.ModelsDir, "other", "repo", "foreign.gguf")
	if err := os.MkdirAll(filepath.Dir(foreign), 0755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(foreign, cliPrincipalGGUF(), 0600); err != nil {
		t.Fatal(err)
	}

	entries := listedEntries(t)
	altered := entries["altered.gguf"]
	if altered.Usable {
		t.Fatalf("an artifact whose verification found altered bytes is not eligible: %+v", altered)
	}
	if altered.State != string(modelapp.StateAltered) {
		t.Fatalf("state=%q want=%q", altered.State, modelapp.StateAltered)
	}
	if altered.VerificationOutcome != string(modelapp.VerifyMismatched) {
		t.Fatalf("the listing should carry what verify recorded: %+v", altered)
	}
	if altered.VerificationCheckedAt == "" {
		t.Fatal("the listing should carry when the check ran")
	}

	untracked := entries["foreign.gguf"]
	if untracked.Usable {
		t.Fatalf("foreign=%+v", untracked)
	}
	// The two are ineligible for different reasons, and the state is what says
	// so: altered bytes and an absent sidecar have different remedies.
	if untracked.State != string(modelapp.StateUntracked) {
		t.Fatalf("state=%q want=%q", untracked.State, modelapp.StateUntracked)
	}
}

// The three standings reach structured output as values a caller can branch on,
// and none of them is reported as a failure.
func TestRunModelListCarriesTheAttestationStanding(t *testing.T) {
	m := withModelManager(t)
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	publishArtifact(t, m, "principal.gguf", cliPrincipalGGUF())

	entry := listedEntries(t)["principal.gguf"]
	if entry.Standing != string(modelapp.StandingUnattested) {
		t.Fatalf("standing=%q want=%q", entry.Standing, modelapp.StandingUnattested)
	}
	if !entry.Usable {
		t.Fatalf("an unattested artifact is not deficient: %+v", entry)
	}

	// The human listing deliberately gains no column for it; the state
	// vocabulary that will render it is a separate change.
	var human bytes.Buffer
	if err := runModel([]string{"list"}, &human); err != nil {
		t.Fatal(err)
	}
	if strings.Contains(human.String(), "STANDING") || strings.Contains(human.String(), "ATTESTATION") {
		t.Fatalf("the listing grew a column it was not meant to:\n%s", human.String())
	}
}

// `model verify` reports the standing beside the integrity outcome, phrased as
// what is known rather than as something missing.
func TestRunModelVerifyReportsStandingAndCheckTime(t *testing.T) {
	m := withModelManager(t)
	old := modelManagerFactory
	modelManagerFactory = func() (*modelapp.Manager, error) { return m, nil }
	t.Cleanup(func() { modelManagerFactory = old })
	publishArtifact(t, m, "principal.gguf", cliPrincipalGGUF())

	var out bytes.Buffer
	if err := runModel([]string{"verify", "org/repo:principal.gguf"}, &out); err != nil {
		t.Fatal(err)
	}
	rendered := out.String()
	for _, expected := range []string{"verified", "Recorded at:", "Attestation: unattested"} {
		if !strings.Contains(rendered, expected) {
			t.Fatalf("missing %q in:\n%s", expected, rendered)
		}
	}
	if strings.Contains(rendered, "baseline") {
		t.Fatalf("the retired word survived in:\n%s", rendered)
	}
}
