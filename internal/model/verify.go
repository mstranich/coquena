package model

import (
	"fmt"
	"os"
	"time"
)

// VerifyOutcome is what a verification found. There are two: a valid sidecar
// always carries an integrity digest, because writing one requires computing
// one, so "nothing to compare against" is not a state an artifact under
// Coquena's management can be in. The sidecar that would express it is invalid,
// and its artifact is not managed at all.
type VerifyOutcome string

const (
	// VerifyMatched indicates the computed digest agrees with the integrity
	// digest recorded for the artifact.
	VerifyMatched VerifyOutcome = "matched"
	// VerifyMismatched indicates the computed digest disagrees with it. The
	// bytes changed since they were recorded; verify cannot tell damage from
	// an edit, and does not claim to.
	VerifyMismatched VerifyOutcome = "mismatched"
)

// VerifyResult reports the outcome of verifying an installed model's local
// file integrity against its sidecar's persisted content hash.
type VerifyResult struct {
	Outcome        VerifyOutcome `json:"outcome"`
	Repository     string        `json:"repository"`
	Filename       string        `json:"filename"`
	TargetPath     string        `json:"targetPath"`
	RecordedSHA256 string        `json:"recordedSHA256,omitempty"`
	ComputedSHA256 string        `json:"computedSHA256"`
	CheckedAt      string        `json:"checkedAt,omitempty"`
	// Stale reports that the file was modified after this check ran, which can
	// only happen if something wrote to it between the hash and the record. It
	// is rare and worth saying: the result describes bytes that may already be
	// gone.
	Stale bool `json:"stale,omitempty"`
	// Standing and AttestationMethod carry the artifact's attestation standing
	// so an operator inspecting one artifact sees what is known about its
	// origin, not only about its integrity. Neither is a failure.
	Standing             Standing              `json:"standing"`
	AttestationMethod    AttestationMethod     `json:"attestationMethod,omitempty"`
	DeclaredRecipe       string                `json:"declaredRecipe,omitempty"`
	PhysicalQuantization *PhysicalQuantization `json:"physicalQuantization,omitempty"`
	DerivationStale      bool                  `json:"derivationStale"`
	Repaired             bool                  `json:"repaired,omitempty"`
}

type VerifyOptions struct{ RepairDerived bool }

// Verify resolves reference against the installed model library, re-hashes the
// resolved artifact's file on disk, compares it against the recorded integrity
// digest, and **writes down what it found**.
//
// Recording the outcome is the point, not a side effect. hashFile runs only
// here, at download and at reconciliation, and the listing never hashes — so if
// verify discarded its answer, reporting a damaged artifact would mean
// re-reading every model in the library on every listing. It records what it
// found the way chkdsk records that it found errors, and later readers consult
// the record instead of the disk.
//
// It refuses with whatever error ResolveInstalled reports when reference does
// not resolve to exactly one installed artifact.
func (m *Manager) Verify(reference string) (VerifyResult, error) {
	return m.VerifyWithOptions(reference, VerifyOptions{})
}

func (m *Manager) VerifyWithOptions(reference string, options VerifyOptions) (VerifyResult, error) {
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
	// ReadRecord rejects a sidecar carrying no integrity digest, so reaching
	// here means there is one to compare against.
	integrity, _ := record.Artifact.Integrity()
	recorded := integrity.Value
	result := VerifyResult{
		Repository:     entry.Source.Repository,
		Filename:       entry.Source.Filename,
		TargetPath:     entry.TargetPath,
		RecordedSHA256: recorded,
		ComputedSHA256: digest,
	}
	if record.Classification.FileType != nil {
		result.DeclaredRecipe = LlamaFileTypeDisplayName(*record.Classification.FileType)
	}
	result.PhysicalQuantization = record.Derivation.PhysicalQuantization
	result.DerivationStale = record.DerivationStale()
	if recorded == digest {
		result.Outcome = VerifyMatched
	} else {
		result.Outcome = VerifyMismatched
	}
	// The verification axis is written here and nowhere else. Binding the
	// record to the digest it checked is what lets that hold: an operation
	// that later replaces the artifact's evidence retires this reading by
	// identity, so no other command ever needs to reach in and clear it.
	record.Verification = &Verification{
		Outcome:       result.Outcome,
		CheckedAt:     time.Now().UTC().Format(time.RFC3339),
		AgainstDigest: recorded,
	}
	result.CheckedAt = record.Verification.CheckedAt
	if info, statErr := os.Stat(entry.TargetPath); statErr == nil {
		result.Stale = modifiedAfter(info.ModTime(), record.Verification.CheckedAt)
	}
	result.Standing = record.Standing()
	if attestation, applies := record.AppliedAttestation(); applies {
		result.AttestationMethod = attestation.Method
	}
	if err = writeRecordInPlace(entry.TargetPath, record); err != nil {
		return result, err
	}
	if options.RepairDerived {
		if result.Outcome != VerifyMatched {
			return result, fmt.Errorf("cannot repair derived evidence unless integrity matches the recorded baseline")
		}
		// A repair that cannot recompute the derivation leaves the existing
		// sidecar exactly as it was. The verification axis written above has
		// already been persisted, which is correct: the hash comparison did
		// happen and its outcome is what the listing reads.
		derivation, deriveErr := deriveFile(entry.TargetPath)
		if deriveErr != nil {
			return result, deriveErr
		}
		record.SchemaVersion = RecordSchemaVersion
		record.SetDerivation(derivation)
		if err = writeRecordInPlace(entry.TargetPath, record); err != nil {
			return result, err
		}
		result.DeclaredRecipe = ""
		if derivation.Classification.FileType != nil {
			result.DeclaredRecipe = LlamaFileTypeDisplayName(*derivation.Classification.FileType)
		}
		result.PhysicalQuantization = derivation.PhysicalQuantization
		result.DerivationStale = false
		result.Repaired = true
	}
	return result, nil
}
