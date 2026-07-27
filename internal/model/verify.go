package model

// VerifyOutcome distinguishes the three possible results of verifying an
// installed model's local integrity against its sidecar's recorded digest.
type VerifyOutcome string

const (
	// VerifyMatched indicates the computed digest agrees with the sidecar's
	// recorded Artifact.SHA256.
	VerifyMatched VerifyOutcome = "matched"
	// VerifyMismatched indicates the computed digest disagrees with the
	// sidecar's recorded Artifact.SHA256; the model should be considered
	// unusable until it is re-downloaded or reconciled again.
	VerifyMismatched VerifyOutcome = "mismatched"
	// VerifyNoBaseline indicates the sidecar has no recorded Artifact.SHA256
	// to compare against (an artifact installed, or reconciled, before
	// content hashing existed).
	VerifyNoBaseline VerifyOutcome = "no-baseline"
)

// VerifyResult reports the outcome of verifying an installed model's local
// file integrity against its sidecar's persisted content hash.
type VerifyResult struct {
	Outcome        VerifyOutcome
	Repository     string
	Filename       string
	TargetPath     string
	RecordedSHA256 string
	ComputedSHA256 string
}

// Verify resolves reference against the installed model library, re-hashes
// the resolved artifact's file on disk, and compares it against the
// sidecar's persisted Artifact.SHA256. It distinguishes three outcomes: a
// hash match (VerifyMatched), a hash mismatch (VerifyMismatched, meaning the
// model should be treated as unusable until re-downloaded or reconciled
// again), and the absence of any recorded baseline digest (VerifyNoBaseline,
// for artifacts installed or reconciled before content hashing existed).
// It refuses with whatever error ResolveInstalled reports when reference
// does not resolve to exactly one installed artifact.
func (m *Manager) Verify(reference string) (VerifyResult, error) {
	entry, err := m.ResolveInstalled(reference)
	if err != nil {
		return VerifyResult{}, err
	}
	digest, err := hashFile(entry.TargetPath)
	if err != nil {
		return VerifyResult{}, err
	}
	record, err := ReadRecord(entry.TargetPath)
	if err != nil {
		return VerifyResult{}, err
	}
	recorded := record.Artifact.SHA256
	result := VerifyResult{
		Repository:     entry.Source.Repository,
		Filename:       entry.Source.Filename,
		TargetPath:     entry.TargetPath,
		RecordedSHA256: recorded,
		ComputedSHA256: digest,
	}
	switch {
	case recorded == "":
		result.Outcome = VerifyNoBaseline
	case recorded == digest:
		result.Outcome = VerifyMatched
	default:
		result.Outcome = VerifyMismatched
	}
	return result, nil
}
