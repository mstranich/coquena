package model

import "fmt"

// UpdateOutcome distinguishes the three possible results of checking an
// installed model's recorded identity against Hugging Face's current state.
type UpdateOutcome string

const (
	// UpdateCurrent indicates the commit SHA Hugging Face currently reports
	// still matches the sidecar's recorded ResolvedRevision, and, when a remote
	// content SHA-256 was available, it still matches Artifact.SHA256.
	UpdateCurrent UpdateOutcome = "up-to-date"
	// UpdateAvailable indicates the commit SHA has changed, or a content hash was
	// available and disagrees with the sidecar's recorded Artifact.SHA256.
	UpdateAvailable UpdateOutcome = "update-available"
	// UpdatePartial indicates Hugging Face reported no content SHA-256 for the
	// matched sibling, so only the commit-SHA comparison could be performed.
	UpdatePartial UpdateOutcome = "partial"
)

// CheckUpdatesResult reports the outcome of checking an installed model's
// recorded Hugging Face identity against Hugging Face's current state.
type CheckUpdatesResult struct {
	Outcome          UpdateOutcome
	Repository       string
	Filename         string
	RecordedRevision string
	CurrentRevision  string
	RecordedSHA256   string
	RemoteSHA256     string `json:"RemoteOID"`
}

// CheckUpdates resolves reference against the installed model library, then
// queries Hugging Face's current revision info using only the sidecar's
// recorded Repository, RequestedRevision, and Filename -- it never opens or
// hashes the local .gguf file. It compares the current commit SHA, and, when
// available, the matched sibling's content SHA-256, against the sidecar's recorded
// ResolvedRevision and Artifact.SHA256, reporting one of three outcomes:
// UpdateCurrent (nothing changed), UpdateAvailable (the commit SHA changed,
// or a content hash was available and disagrees with the recorded digest), or
// UpdatePartial (no content hash was available, so only the commit-SHA comparison
// could be performed). It refuses with a clear diagnostic when reference does
// not resolve to exactly one installed artifact, or when the resolved entry
// is a "local"-provider entry, since there is no Hugging Face identity
// recorded to compare against.
func (m *Manager) CheckUpdates(reference string) (CheckUpdatesResult, error) {
	entry, err := m.ResolveInstalled(reference)
	if err != nil {
		return CheckUpdatesResult{}, err
	}
	source := entry.Source
	if source.Provider == "local" {
		return CheckUpdatesResult{}, fmt.Errorf("model %q was reconciled with no Hugging Face identity recorded; there is nothing to check for updates against", reference)
	}
	record, err := ReadRecord(entry.TargetPath)
	if err != nil {
		return CheckUpdatesResult{}, err
	}
	candidates, err := m.Resolver.ResolveRevision(source.Repository, source.RequestedRevision, source.Filename)
	if err != nil {
		return CheckUpdatesResult{}, err
	}
	if len(candidates) != 1 {
		return CheckUpdatesResult{}, fmt.Errorf("check-updates: expected exactly one Hugging Face artifact matching %q in %s, got %d", source.Filename, source.Repository, len(candidates))
	}
	candidate := candidates[0]
	recordedIntegrity, _ := record.Artifact.Integrity()
	result := CheckUpdatesResult{
		Repository:       source.Repository,
		Filename:         source.Filename,
		RecordedRevision: record.Source.ResolvedRevision,
		CurrentRevision:  candidate.Source.ResolvedRevision,
		RecordedSHA256:   recordedIntegrity.Value,
		RemoteSHA256:     candidate.ContentSHA256,
	}
	switch {
	case candidate.ContentSHA256 == "":
		// No content hash available from Hugging Face for this sibling; only
		// the commit-SHA comparison could be performed, so this is reported
		// as a distinct outcome rather than silently treated as a match.
		result.Outcome = UpdatePartial
	case result.RecordedRevision != result.CurrentRevision || candidate.ContentSHA256 != result.RecordedSHA256:
		result.Outcome = UpdateAvailable
	default:
		result.Outcome = UpdateCurrent
	}
	return result, nil
}
